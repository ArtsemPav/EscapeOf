using System;
using System.Collections;
using ChemicalPuzzle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Загадка "Ремонт отвёрткой". Игрок перетаскивает отвёртку из инвентаря на объект →
/// объект проигрывает анимацию ремонта в цикле → на экране в случайном месте появляется
/// круговая QTE-шкала (<see cref="ScrewdriverMinigamePanel"/>) → по завершении прогресса
/// проигрывается финальная анимация → загадка решена.
///
/// Требует PuzzleModeController на том же объекте.
/// Animator на ремонтируемом объекте с параметрами:
///   "IsRepairing" (Bool)     — включает цикл ремонта (looping state)
///   "RepairComplete" (Trigger) — запускает финальную анимацию
/// и стейтом "Repaired" (финальная анимация).
/// 
/// Мини-игра использует <see cref="ScrewdriverMinigamePanel"/>: при превышении лимита
/// промахов срабатывает OnFailed — ремонт прерывается, и игрок может повторить попытку.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ScrewdriverRepairController : MonoBehaviour,
    IPuzzleDropHandler, IPuzzleDropTarget, IPuzzleExitGuard, ISaveable
{
    // ── Constants ────────────────────────────────────────────────────────────────

    private const string ParamIsRepairing = "IsRepairing";
    private const string ParamRepairComplete = "RepairComplete";
    private const string StateRepaired = "Repaired";
    private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";
    private const float RaycastDistance = 20f;

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Save")]
    [Tooltip("Уникальный ID для системы сохранений. Должен отличаться для каждого экземпляра загадки.")]
    [SerializeField] private string _saveId = "screwdriver_repair_puzzle";

    [Header("Item")]
    [Tooltip("ItemData отвёртки, которую нужно перетащить на объект.")]
    [SerializeField] private ItemData _requiredItem;

    [Header("Auto-Resolved (override if needed)")]
    [Tooltip("Collider ремонтируемого объекта — цель рейкаста при дропе. Если null — ищется первый Collider на дочернем объекте с Animator.")]
    [SerializeField] private Collider _targetCollider;

    [Tooltip("Animator ремонтируемого объекта. Если null — ищется GetComponentInChildren<Animator>().")]
    [SerializeField] private Animator _targetAnimator;

    [Header("Ghost Preview")]
    [Tooltip("Материал ghost-превью отвёртки. Если пуст — загружается CardLamp_Ghost.mat из Resources.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Префаб для ghost-превью. Если пуст — используется inspectionPrefab из _requiredItem.")]
    [SerializeField] private GameObject _ghostPrefab;

    [Tooltip("Точка спавна ghost-превью. Если пуст — используется позиция _targetCollider.")]
    [SerializeField] private Transform _ghostSpawnPoint;

    [Header("Drop Hint")]
    [Tooltip("Подсказка при наведении отвёртки на ремонтируемый объект.")]
    [SerializeField] private string _dropHint = "Использовать отвёртку";

    [Header("Minigame")]
    [Tooltip("Панель UI мини-игры в Canvas сцены. Должна содержать компонент ScrewdriverMinigamePanel.")]
    [SerializeField] private GameObject _minigamePanel;

    [Tooltip("Компонент ScrewdriverMinigamePanel на панели. Если null — берётся GetComponent с _minigamePanel.")]
    [SerializeField] private ScrewdriverMinigamePanel _minigame;

    [Header("Audio")]
    [SerializeField] private AudioClip _repairStartClip;
    [SerializeField] private AudioClip _repairCompleteClip;
    [SerializeField, Range(0f, 1f)] private float _repairStartVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _repairCompleteVolume = 1f;

    // ── State ────────────────────────────────────────────────────────────────────

    private bool _isSolved;
    private bool _isProcessing;
    private bool _isCompleting;

    private PuzzleModeController _puzzleMode;

    // Ghost preview
    private GameObject _ghostPreview;
    private Material _runtimeGhostMaterial;
    private bool _ghostVisible;

    // ── Public API ───────────────────────────────────────────────────────────────

    public bool IsSolved => _isSolved;

    // ── IPuzzleDropTarget ────────────────────────────────────────────────────────

    public string GetDropHint() => _dropHint;

    public bool CanAccept(ItemData item)
    {
        if (item == null || _isSolved || _isProcessing)
            return false;

        return item == _requiredItem;
    }

    // ── ISaveable ────────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isSolved = _isSolved });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.isSolved;
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
    }

    // ── Unity Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        AutoResolveReferences();

        if (_minigame == null && _minigamePanel != null)
            _minigame = _minigamePanel.GetComponent<ScrewdriverMinigamePanel>();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered += HandlePuzzleEntered;
            _puzzleMode.OnExited += HandlePuzzleExited;
        }

        if (_minigame != null)
        {
            _minigame.OnCompleted += HandleMinigameCompleted;
            _minigame.OnHit += HandleMinigameHit;
            _minigame.OnMiss += HandleMinigameMiss;
            _minigame.OnFailed += HandleMinigameFailed;
        }

        if (_isSolved)
            RestoreSolvedState();
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered -= HandlePuzzleEntered;
            _puzzleMode.OnExited -= HandlePuzzleExited;
        }

        if (_minigame != null)
        {
            _minigame.OnCompleted -= HandleMinigameCompleted;
            _minigame.OnHit -= HandleMinigameHit;
            _minigame.OnMiss -= HandleMinigameMiss;
            _minigame.OnFailed -= HandleMinigameFailed;
        }

        SaveManager.Instance?.Unregister(this);

        if (_ghostPreview != null)
            Destroy(_ghostPreview);
    }

    // ── Ghost Preview Update ─────────────────────────────────────────────────────

    private void Update()
    {
        if (_isSolved || _isProcessing || _puzzleMode == null || !_puzzleMode.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false);
            return;
        }

        if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem != null
            && CanAccept(PuzzleInventoryBar.DraggedItem))
        {
            bool isHovering = IsMouseOverTarget();
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

    private bool IsMouseOverTarget()
    {
        if (Mouse.current == null || _targetCollider == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        if (Camera.main == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider == _targetCollider ||
                   (hit.collider.transform != null && hit.collider.transform.IsChildOf(_targetCollider.transform));
        }

        return false;
    }

    private void SetGhostVisible(bool visible)
    {
        if (visible)
        {
            if (_ghostPreview == null)
                CreateGhostPreview();

            if (_ghostPreview != null)
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
        EnsureGhostMaterial();

        var prefab = _ghostPrefab != null ? _ghostPrefab
                    : (_requiredItem != null ? _requiredItem.inspectionPrefab : null);
        if (prefab == null) return;

        var spawnPoint = _ghostSpawnPoint != null ? _ghostSpawnPoint
                        : (_targetCollider != null ? _targetCollider.transform : transform);
        if (spawnPoint == null) return;

        _ghostPreview = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation, spawnPoint);
        _ghostPreview.name = "ScrewdriverGhost";

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

    // ── Auto-Resolve ─────────────────────────────────────────────────────────────

    private void AutoResolveReferences()
    {
        if (_targetAnimator == null)
            _targetAnimator = GetComponentInChildren<Animator>();

        if (_targetCollider == null && _targetAnimator != null)
        {
            _targetCollider = _targetAnimator.GetComponent<Collider>();
            if (_targetCollider == null)
                _targetCollider = _targetAnimator.GetComponentInChildren<Collider>();
            if (_targetCollider == null)
                _targetCollider = _targetAnimator.GetComponentInParent<Collider>();
        }

        if (_targetCollider == null)
            _targetCollider = GetComponentInChildren<Collider>();
    }

    // ── IPuzzleDropHandler ───────────────────────────────────────────────────────

    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = item; // отвёртка — инструмент, не расходуется

        if (_isProcessing || _isSolved)
            return false;

        if (item == null || item != _requiredItem)
            return false;

        if (!PerformRaycast(screenPosition))
            return false;

        _isProcessing = true;

        if (_ghostVisible) SetGhostVisible(false);

        PuzzleInventoryBar.Instance?.Hide();

        StartCoroutine(RepairSequence());
        return true;
    }

    // ── IPuzzleExitGuard ─────────────────────────────────────────────────────────

    public bool CanExitPuzzle()
    {
        // Разрешаем выход во время мини-игры (Esc прерывает ремонт),
        // но блокируем выход во время финальной анимации (CompletionSequence).
        return !_isCompleting;
    }

    // ── Puzzle Flow ──────────────────────────────────────────────────────────────

    private void HandlePuzzleEntered()
    {
        // Ничего не запускаем автоматически — ремонт начинается только после
        // перетаскивания отвёртки на объект (HandleDrop).
    }

    private void HandlePuzzleExited()
    {
        // Останавливаем все корутины контроллера (на случай прерывания во время ремонта)
        StopAllCoroutines();

        if (_ghostVisible) SetGhostVisible(false);

        if (_minigame != null)
            _minigame.StopMinigame();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        if (_isProcessing && !_isSolved)
        {
            _isProcessing = false;
            _isCompleting = false;
            if (_targetAnimator != null)
                _targetAnimator.SetBool(ParamIsRepairing, false);
        }
    }

    private IEnumerator RepairSequence()
    {
        AudioManager.Instance?.PlaySFX(_repairStartClip, _repairStartVolume);

        // Запускаем цикл анимации ремонта — проигрывается непрерывно во время мини-игры
        if (_targetAnimator != null)
            _targetAnimator.SetBool(ParamIsRepairing, true);

        yield return StartMinigameAfterFrame();
    }

    private IEnumerator StartMinigameAfterFrame()
    {
        if (_minigamePanel != null)
            _minigamePanel.SetActive(true);

        // Ждём кадр чтобы Layout закончил
        yield return null;

        if (_minigame != null)
            _minigame.StartMinigame();
    }

    private void HandleMinigameCompleted()
    {
        StartCoroutine(CompletionSequence());
    }

    /// <summary>Вызывается при попадании в сектор мини-игры.</summary>
    private void HandleMinigameHit()
    {
        AudioManager.Instance?.PlaySFX(_repairStartClip, _repairStartVolume * 0.5f);
    }

    /// <summary>Вызывается при промахе в мини-игре.</summary>
    private void HandleMinigameMiss()
    {
        // Промахи обрабатываются внутри панели; здесь можно добавить
        // дополнительные эффекты (тряска камеры, звук и т.п.) при необходимости.
    }

    /// <summary>Вызывается при провале мини-игры (превышен лимит промахов). Прерывает ремонт и позволяет повторить.</summary>
    private void HandleMinigameFailed()
    {
        StartCoroutine(FailureSequence());
    }

    private IEnumerator FailureSequence()
    {
        if (_minigame != null)
            _minigame.StopMinigame();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        if (_targetAnimator != null)
            _targetAnimator.SetBool(ParamIsRepairing, false);

        _isProcessing = false;
        PuzzleInventoryBar.Instance?.Show(this);

        yield return null;
    }

    private IEnumerator CompletionSequence()
    {
        _isCompleting = true;

        if (_minigame != null)
            _minigame.StopMinigame();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        AudioManager.Instance?.PlaySFX(_repairCompleteClip, _repairCompleteVolume);

        // Останавливаем цикл ремонта и запускаем финальную анимацию
        if (_targetAnimator != null)
        {
            _targetAnimator.SetBool(ParamIsRepairing, false);
            _targetAnimator.SetTrigger(ParamRepairComplete);

            yield return null; // ждём кадр чтобы триггер сработал

            float timeout = 10f;
            float elapsed = 0f;
            while (!_targetAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateRepaired) && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            while (_targetAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateRepaired) &&
                   _targetAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
        }

        _isSolved = true;
        _isProcessing = false;
        _isCompleting = false;

        Debug.Log("Пазл решен");

        SaveManager.Instance?.Save();
        _puzzleMode?.SetSolved();
    }

    // ── Solved Restore ───────────────────────────────────────────────────────────

    private void RestoreSolvedState()
    {
        if (_targetAnimator != null)
        {
            _targetAnimator.SetBool(ParamIsRepairing, false);
            _targetAnimator.Play(StateRepaired, 0, 1f);
        }
    }

    // ── Raycast ──────────────────────────────────────────────────────────────────

    private bool PerformRaycast(Vector2 screenPos)
    {
        if (Camera.main == null || _targetCollider == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider == _targetCollider ||
                   (hit.collider.transform != null && hit.collider.transform.IsChildOf(_targetCollider.transform));
        }
        return false;
    }
}
