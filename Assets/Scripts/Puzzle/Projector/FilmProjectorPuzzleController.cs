using System;
using ChemicalPuzzle;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет загадкой кинопроектора. Бабина устанавливается на якорь BabinaInput,
/// после чего нажатие кнопки Cylinder сразу включает проектор:
/// DecalSlideshow.ReelInstalled = true — слайдшоу, зацикленный звук и луч света
/// запускаются от питания (IPowerConsumer). Без мини-игры.
/// Работает совместно с PuzzleModeController.
/// </summary>
[DefaultExecutionOrder(-7)]
public class FilmProjectorPuzzleController : MonoBehaviour,
    IPuzzleDropHandler, IPuzzleDropTarget, IPuzzleExitGuard, ISaveable
{
    private const string DefaultSaveId = "film_projector_puzzle";
    private const float RaycastDistance = 100f;
    private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";

    [Header("Save Settings")]
    [SerializeField] private string _saveId = DefaultSaveId;

    [Header("References")]
    [SerializeField] private PuzzleModeController _controller;

    [Header("Projector")]
    [Tooltip("Слайдшоу на объекте Film. При установке бабины ReelInstalled = true.")]
    [SerializeField] private DecalSlideshow _slideshow;

    [Header("Drop Slot")]
    [Tooltip("Предмет-бабина (Data_Babina).")]
    [SerializeField] private ItemData _babinaItem;

    [Tooltip("Коллайдер якоря дропа — цель рейкаста при отпускании предмета.")]
    [SerializeField] private Collider _anchorCollider;

    [Tooltip("Точка спавна визуала / ghost-превью.")]
    [SerializeField] private Transform _anchorTransform;

    [Tooltip("Prefab для ghost-превью. Если пуст — берётся inspectionPrefab предмета.")]
    [SerializeField] private GameObject _ghostPrefab;

    [Tooltip("Меши бабин, скрытые до установки (например babina 1 / babina 2). " +
             "Включаются при установке бабины вместо спавна префаба.")]
    [SerializeField] private GameObject[] _reelVisuals;

    [Tooltip("Спиннеры бабин. При установке бабины им разрешается вращение через SetSpinning.")]
    [SerializeField] private ReelSpinner[] _reelSpinners;

    [Header("Audio")]
    [Tooltip("Звук установки бабины.")]
    [SerializeField] private AudioClip _insertClip;

    [SerializeField, Range(0f, 1f)] private float _insertVolume = 1f;

    [Header("Start Button")]
    [Tooltip("Кнопка запуска проектора (Cylinder с ButtonPressAnimation).")]
    [SerializeField] private ButtonPressAnimation _startButton;

    [Header("Common")]
    [Tooltip("Слой якоря дропа для Raycast (например Interactable Layer).")]
    [SerializeField] private LayerMask _anchorLayer;

    [Tooltip("Материал ghost-превью предмета. Если пуст — загружается CardLamp_Ghost.mat из Resources.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Подсказка при наведении предмета на якорь проектора.")]
    [SerializeField] private string _dropHint = "Установить бабину";

    [Tooltip("Подсказка при нажатии кнопки без установленной бабины.")]
    [SerializeField] private string _hintNoItem = "Установите бабину в проектор.";

    [Header("Stop Button")]
    [Tooltip("Кнопка выключения проектора (Cylinder с ButtonPressAnimation).")]
    [SerializeField] private ButtonPressAnimation _stopButton;

    // ── State ───────────────────────────────────────────────────────────────

    private bool _isPlaced;
    private bool _isSolved;
    private bool _isProjectorRunning;
    private GameObject _ghostPreview;
    private Material _runtimeGhostMaterial;
    private bool _ghostVisible;

    // ── ISaveable ───────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { solved = _isSolved, placed = _isPlaced });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.solved;
        _isPlaced = data.placed || data.solved; // решённый пазл всегда с бабиной
    }

    [Serializable]
    private struct SaveData
    {
        public bool solved;
        public bool placed;
    }

    // ── Unity Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponent<PuzzleModeController>();

        SaveManager.Instance?.Register(this);
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnEntered += HandleEntered;
            _controller.OnExited += HandleExited;
        }

        if (_startButton != null)
            _startButton.OnPressed += HandleStartButtonPressed;

        if (_stopButton != null)
            _stopButton.OnPressed += HandleStopButtonPressed;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited -= HandleExited;
        }

        if (_startButton != null)
            _startButton.OnPressed -= HandleStartButtonPressed;

        if (_stopButton != null)
            _stopButton.OnPressed -= HandleStopButtonPressed;
    }

    private void Start()
    {
        // Восстановление после загрузки.
        if (_isPlaced)
            SetReelVisualsActive(true);

        if (_isSolved)
        {
            ApplyReelInstalled();
            _controller?.SetSolved();
        }
    }

    private void OnDestroy()
    {
        if (_ghostPreview != null)
            Destroy(_ghostPreview);

        SaveManager.Instance?.Unregister(this);
    }

    // ── Ghost Preview ───────────────────────────────────────────────────────

    private void Update()
    {
        if (_isSolved || _isPlaced || _controller == null || !_controller.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false);
            return;
        }

        bool hovering = PuzzleInventoryBar.IsDragging
            && PuzzleInventoryBar.DraggedItem != null
            && CanAccept(PuzzleInventoryBar.DraggedItem)
            && IsMouseOverAnchor();

        if (hovering && !_ghostVisible)
            SetGhostVisible(true);
        else if (!hovering && _ghostVisible)
            SetGhostVisible(false);
    }

    private bool IsMouseOverAnchor()
    {
        if (Mouse.current == null || _anchorCollider == null) return false;
        if (Camera.main == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        return Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, _anchorLayer)
               && hit.collider == _anchorCollider;
    }

    private void SetGhostVisible(bool visible)
    {
        if (visible)
        {
            if (_ghostPreview == null)
            {
                CreateGhostPreview();
                if (_ghostPreview == null) return;
            }

            _ghostPreview.SetActive(true);
            _ghostVisible = true;
        }
        else
        {
            if (_ghostPreview != null)
                _ghostPreview.SetActive(false);

            _ghostVisible = false;
        }
    }

    private void CreateGhostPreview()
    {
        if (_anchorTransform == null || _babinaItem == null) return;

        if (_runtimeGhostMaterial == null)
        {
            _runtimeGhostMaterial = _ghostMaterial != null
                ? _ghostMaterial
                : Resources.Load<Material>(GhostMaterialPath);
        }

        var prefab = _ghostPrefab != null ? _ghostPrefab : _babinaItem.inspectionPrefab;
        if (prefab == null) return;

        _ghostPreview = Instantiate(prefab, _anchorTransform.position,
                                    _anchorTransform.rotation, _anchorTransform);
        _ghostPreview.name = _babinaItem.itemName + "Ghost";

        foreach (var col in _ghostPreview.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        if (_runtimeGhostMaterial != null)
        {
            foreach (var rend in _ghostPreview.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = _runtimeGhostMaterial;
                rend.sharedMaterials = mats;
            }
        }

        _ghostPreview.SetActive(false);
    }

    // ── Puzzle Flow ─────────────────────────────────────────────────────────

    private void HandleEntered() { }

    private void HandleExited() => SetGhostVisible(false);

    private void HandleStartButtonPressed()
    {
        if (_isSolved)
        {
            // Повторный запуск после Stop
            if (!_isProjectorRunning)
            {
                _isProjectorRunning = true;
                _slideshow?.StartProjector();
                AllowReelSpinning(true);
            }
            return;
        }

        if (_controller == null || !_controller.IsActive) return;

        if (!_isPlaced)
        {
            PopupMessageSystem.Instance?.Show(_hintNoItem, PopupMessageType.Hint);
            return;
        }

        // Бабина установлена — запуск проектора сразу после нажатия кнопки.
        _isSolved = true;
        ApplyReelInstalled();
        _controller?.SetSolved(); // выходит из режима пазла и сохраняет
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Вызывается при нажатии кнопки Stop. Выключает слайдшоу, звук, луч
    /// и останавливает вращение бабин. Питание при этом проектор не будит —
    /// перезапуск только кнопкой Start.
    /// </summary>
    private void HandleStopButtonPressed()
    {
        if (!_isSolved || !_isProjectorRunning) return;
        if (_controller == null || !_controller.IsActive) return;

        _isProjectorRunning = false;
        _slideshow?.StopProjector();
        AllowReelSpinning(false);
    }

    /// <summary>
    /// Включает проектор: слайдшоу + звук + луч стартуют от текущего питания.
    /// DecalSlideshow и ReelSpinner сами реагируют на смену питания через IPowerConsumer.
    /// </summary>
    private void ApplyReelInstalled()
    {
        if (_slideshow == null) return;

        _isProjectorRunning = true;
        _slideshow.ReelInstalled = true;
        AllowReelSpinning(true);

        // Повторно применяем текущее состояние питания, чтобы слайдшоу/звук
        // запустились сразу, если свет уже включён.
        _slideshow.OnPowerStateChanged(
            LightingSystem.Instance == null || LightingSystem.Instance.IsPowered);
    }

    // ── IPuzzleExitGuard / IPuzzleDropTarget / IPuzzleDropHandler ───────────

    /// <summary>Проект не блокирует выход из пазла.</summary>
    public bool CanExitPuzzle() => true;

    /// <summary>Текст-подсказка при наведении предмета на якорь проектора.</summary>
    public string GetDropHint() => _dropHint;

    /// <summary>True, если бабина подходит и ещё не установлена.</summary>
    public bool CanAccept(ItemData item)
    {
        if (item == null || _isSolved || _isPlaced) return false;
        return _babinaItem != null && item.ItemId == _babinaItem.ItemId;
    }

    /// <summary>
    /// Принимает бабину, брошенную из инвентарного бара на якорь проектора.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;

        if (!CanAccept(item)) return false;
        if (Camera.main == null) return false;

        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, RaycastDistance, _anchorLayer)
            || hit.collider != _anchorCollider)
            return false;

        SetGhostVisible(false);

        // Помечаем как установленную до спавна — корректно для системы сохранений.
        _isPlaced = true;
        SaveManager.Instance?.Save();

        SpawnPlacedVisual();
        AudioManager.Instance?.PlaySFX(_insertClip, _insertVolume);
        return true;
    }

    private void SpawnPlacedVisual()
    {
        SetReelVisualsActive(true);
    }

    /// <summary>Включает/выключает меши бабин на проекторе (_reelVisuals).</summary>
    private void SetReelVisualsActive(bool active)
    {
        if (_reelVisuals == null) return;
        foreach (var visual in _reelVisuals)
        {
            if (visual != null)
                visual.SetActive(active);
        }
    }

    /// <summary>
    /// Разрешает спиннерам бабин вращение. Свет всё равно обязателен:
    /// пока питания нет, спиннеры стоят, но при его включении закрутятся.
    /// </summary>
    private void AllowReelSpinning(bool allowed)
    {
        if (_reelSpinners == null) return;
        foreach (var spinner in _reelSpinners)
        {
            if (spinner != null)
                spinner.SetSpinning(allowed);
        }
    }
}
