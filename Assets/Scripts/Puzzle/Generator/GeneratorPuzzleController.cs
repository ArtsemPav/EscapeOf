using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет загадкой генератора. Принимает нужные предметы из PuzzleInventoryBar,
/// брошенные на якорь SpartkPlugInput, спавнит их визуал и после установки всех
/// предметов запускает timing-мини-игру. По завершению мини-игры пазл считается решённым.
///
/// Работает совместно с PuzzleModeController: тот входит/выходит из режима пазла,
/// показывает инвентарный бар и находит этот компонент как IPuzzleDropHandler.
/// </summary>
[DefaultExecutionOrder(-7)]
public class GeneratorPuzzleController : MonoBehaviour, IPuzzleDropHandler, IPuzzleDropTarget, ISaveable
{
    private const string DefaultSaveId = "generator_puzzle";

    [Header("Save Settings")]
    [Tooltip("Стабильный уникальный идентификатор сохранения. Не меняй после назначения — по нему сопоставляются данные при загрузке.")]
    [SerializeField] private string _saveId = DefaultSaveId;

    [Header("References")]
    [SerializeField] private PuzzleModeController _controller;

    [Tooltip("Коллайдер якоря дропа (SpartkPlugInput).")]
    [SerializeField] private Collider _inputAnchorCollider;

    [Tooltip("Точка спавна визуала вставленного предмета.")]
    [SerializeField] private Transform _inputAnchorTransform;

    [Tooltip("Prefab визуала вставленного предмета. Если пуст — берётся inspectionPrefab предмета.")]
    [SerializeField] private GameObject _placedItemPrefab;

    [SerializeField] private GeneratorTimingMinigame _minigame;

    [Tooltip("Корневой UI мини-игры. Выключен по умолчанию.")]
    [SerializeField] private GameObject _minigamePanel;

    [Header("Items")]
    [Tooltip("Предметы, которые нужно перенести на генератор (например SparkPlug.asset).")]
    [SerializeField] private ItemData[] _requiredItems;

    [Tooltip("Слой якоря дропа для Raycast (например Interactable Layer).")]
    [SerializeField] private LayerMask _anchorLayer;

    [Header("Audio")]
    [Tooltip("Звук установки предмета в генератор.")]
    [SerializeField] private AudioClip _insertClip;
    [SerializeField, Range(0f, 1f)] private float _insertVolume = 1f;

    [Header("Ghost Preview")]
    [Tooltip("Материал ghost-превью предмета. Если пуст — загружается CardLamp_Ghost.mat из Resources.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Подсказка при наведении предмета на якорь генератора.")]
    [SerializeField] private string _dropHint = "Установить в генератор";

    private const float RaycastDistance = 100f;
    private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";

    private readonly HashSet<string> _placedItemIds = new HashSet<string>();
    private bool _isSolved;

    private GameObject _ghostPreview;
    private Material _runtimeGhostMaterial;
    private bool _ghostVisible;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        var ids = new string[_placedItemIds.Count];
        _placedItemIds.CopyTo(ids);
        return JsonUtility.ToJson(new SaveData { solved = _isSolved, placedIds = ids });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.solved;

        _placedItemIds.Clear();
        if (data.placedIds != null)
        {
            foreach (var id in data.placedIds)
            {
                if (!string.IsNullOrEmpty(id))
                    _placedItemIds.Add(id);
            }
        }
    }

    [Serializable]
    private struct SaveData
    {
        public bool solved;
        public string[] placedIds;
    }

    // ── Unity Lifecycle ──────────────────────────────────────────────────────────

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

        if (_minigame != null)
            _minigame.OnCompleted += HandleMinigameCompleted;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited -= HandleExited;
        }

        if (_minigame != null)
            _minigame.OnCompleted -= HandleMinigameCompleted;
    }

    private void Start()
    {
        // Восстановление визуала уже вставленных предметов после загрузки.
        for (int i = 0; i < _placedItemIds.Count; i++)
            SpawnPlacedVisual(FindItemById(GetPlacedIdAt(i)));

        SetMinigameVisible(false);

        if (_isSolved)
            _controller?.SetSolved();
    }

    private void OnDestroy()
    {
        if (_ghostPreview != null)
            Destroy(_ghostPreview);

        SaveManager.Instance?.Unregister(this);
    }

    // ── Ghost Preview ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (_isSolved || _controller == null || !_controller.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false);
            return;
        }

        if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem != null
            && CanAccept(PuzzleInventoryBar.DraggedItem))
        {
            bool isHovering = IsMouseOverAnchor();
            if (isHovering && !_ghostVisible)
                SetGhostVisible(true);
            else if (!isHovering && _ghostVisible)
                SetGhostVisible(false);
        }
        else if (_ghostVisible)
        {
            SetGhostVisible(false);
        }
    }

    private bool IsMouseOverAnchor()
    {
        if (Mouse.current == null || _inputAnchorCollider == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        if (Camera.main == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, _anchorLayer))
            return hit.collider == _inputAnchorCollider;

        return false;
    }

    private void SetGhostVisible(bool visible)
    {
        _ghostVisible = visible;

        if (visible)
        {
            if (_ghostPreview == null)
                CreateGhostPreview();
            if (_ghostPreview != null)
                _ghostPreview.SetActive(true);
        }
        else
        {
            if (_ghostPreview != null)
                _ghostPreview.SetActive(false);
        }
    }

    private void CreateGhostPreview()
    {
        if (_inputAnchorTransform == null) return;
        if (PuzzleInventoryBar.DraggedItem == null) return;

        EnsureGhostMaterial();

        var prefab = _placedItemPrefab != null ? _placedItemPrefab
                    : PuzzleInventoryBar.DraggedItem.inspectionPrefab;
        if (prefab == null) return;

        _ghostPreview = Instantiate(prefab, _inputAnchorTransform.position,
                                    _inputAnchorTransform.rotation, _inputAnchorTransform);
        _ghostPreview.name = "SparkPlugGhost";

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

    private void EnsureGhostMaterial()
    {
        if (_runtimeGhostMaterial != null) return;

        if (_ghostMaterial != null)
        {
            _runtimeGhostMaterial = _ghostMaterial;
            return;
        }

        _runtimeGhostMaterial = Resources.Load<Material>(GhostMaterialPath);
    }

    // ── Puzzle Flow ──────────────────────────────────────────────────────────────

    private void HandleEntered()
    {
        // Если все нужные предметы уже установлены — сразу открываем мини-игру.
        if (AllItemsPlaced())
            StartMinigame();
        else
            SetMinigameVisible(false);
    }

    private void HandleExited()
    {
        SetGhostVisible(false);
        SetMinigameVisible(false);
        _minigame?.StopMinigame();
    }

    private void HandleMinigameCompleted()
    {
        if (_isSolved) return;

        _isSolved = true;
        SetMinigameVisible(false);
        _controller?.SetSolved(); // выходит из режима пазла и сохраняет прогресс
    }

    // ── IPuzzleDropTarget ──────────────────────────────────────────────────────

    /// <summary>Возвращает текст-подсказку при наведении предмета на якорь генератора.</summary>
    public string GetDropHint() => _dropHint;

    /// <summary>True, если предмет подходит для установки и ещё не размещён.</summary>
    public bool CanAccept(ItemData item)
    {
        if (item == null || _isSolved) return false;
        return IsRequired(item) && !_placedItemIds.Contains(item.ItemId);
    }

    // ── IPuzzleDropHandler ─────────────────────────────────────────────────────

    /// <summary>
    /// Принимает предмет, брошенный из инвентарного бара на якорь SpartkPlugInput.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;

        if (item == null || !IsRequired(item) || _placedItemIds.Contains(item.ItemId))
            return false;

        var cam = Camera.main;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, RaycastDistance, _anchorLayer))
            return false;

        if (hit.collider != _inputAnchorCollider)
            return false;

        SetGhostVisible(false);

        _placedItemIds.Add(item.ItemId);
        SpawnPlacedVisual(item);
        AudioManager.Instance?.PlaySFX(_insertClip, _insertVolume);
        SaveManager.Instance?.Save();

        if (AllItemsPlaced() && _controller != null && _controller.IsActive)
            StartMinigame();

        return true; // предмет принят и удаляется из инвентаря
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool IsRequired(ItemData item)
    {
        if (_requiredItems == null) return false;
        foreach (var req in _requiredItems)
        {
            if (req != null && req.ItemId == item.ItemId)
                return true;
        }
        return false;
    }

    private bool AllItemsPlaced()
    {
        int required = _requiredItems != null ? _requiredItems.Length : 0;
        return required > 0 && _placedItemIds.Count >= required;
    }

    private ItemData FindItemById(string id)
    {
        if (string.IsNullOrEmpty(id) || _requiredItems == null) return null;
        foreach (var req in _requiredItems)
        {
            if (req != null && req.ItemId == id)
                return req;
        }
        return null;
    }

    private string GetPlacedIdAt(int index)
    {
        int i = 0;
        foreach (var id in _placedItemIds)
        {
            if (i == index) return id;
            i++;
        }
        return null;
    }

    private void SpawnPlacedVisual(ItemData item)
    {
        if (_inputAnchorTransform == null) return;

        var prefab = _placedItemPrefab != null ? _placedItemPrefab
                                               : (item != null ? item.inspectionPrefab : null);
        if (prefab == null) return;

        Instantiate(prefab, _inputAnchorTransform.position,
                    _inputAnchorTransform.rotation, _inputAnchorTransform);
    }

    private void StartMinigame()
    {
        SetMinigameVisible(true);
        _minigame?.StartMinigame();
    }

    private void SetMinigameVisible(bool visible)
    {
        if (_minigamePanel != null)
            _minigamePanel.SetActive(visible);
    }
}
