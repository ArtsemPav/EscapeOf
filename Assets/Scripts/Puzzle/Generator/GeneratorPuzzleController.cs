using System;
using System.Collections;
using System.Collections.Generic;
using ChemicalPuzzle;
using Effects;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет загадкой генератора. Каждый предмет устанавливается на свой якорь:
/// SparkPlug → SpartkPlugInput (статичный спавн),
/// Canister → CanisterInput (анимация залития топлива).
/// После установки всех предметов мини-игра запускается нажатием кнопки Cylinder.
///
/// Работает совместно с PuzzleModeController: тот входит/выходит из режима пазла,
/// показывает инвентарный бар и находит этот компонент как IPuzzleDropHandler.
/// </summary>
[DefaultExecutionOrder(-7)]
public class GeneratorPuzzleController : MonoBehaviour,
    IPuzzleDropHandler, IPuzzleDropTarget, IPuzzleExitGuard, ISaveable
{
    private const string DefaultSaveId = "generator_puzzle";
    private const float RaycastDistance = 100f;
    private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";
    private const float HitShakeDuration = 2f;
    private const float MissShakeDuration = 0.5f;

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Save Settings")]
    [Tooltip("Стабильный уникальный идентификатор сохранения. Не меняй после назначения — по нему сопоставляются данные при загрузке.")]
    [SerializeField] private string _saveId = DefaultSaveId;

    [Header("References")]
    [SerializeField] private PuzzleModeController _controller;

    [SerializeField] private GeneratorTimingMinigame _minigame;

    [Tooltip("Корневой UI мини-игры. Выключен по умолчанию.")]
    [SerializeField] private GameObject _minigamePanel;

    [Header("Hit VFX")]
    [Tooltip("Particle System, проигрывается при попадании в зелёную зону мини-игры.")]
    [SerializeField] private ParticleSystem _hitVfx;

    [Header("Audio")]
    [Tooltip("Звук попадания в зелёную зону.")]
    [SerializeField] private AudioClip _successClip;

    [Tooltip("Звук промаха (красная зона).")]
    [SerializeField] private AudioClip _failClip;

    [SerializeField, Range(0f, 1f)] private float _successVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _failVolume = 1f;

    [Header("Completion Effects")]
    [Tooltip("Particle System, запускается при успешном завершении мини-игры (например дым работающего генератора).")]
    [SerializeField] private ParticleSystem _completionVfx;

    [Tooltip("ObjectShake на генераторе — включается при завершении мини-игры (постоянное дрожание работающего двигателя).")]
    [SerializeField] private ObjectShake _generatorShake;

    [Header("Hit Shake")]
    [Tooltip("Амплитуда смещения позиции при импульсной тряске генератора при попадании в зелёную зону (метры).")]
    [SerializeField, Min(0f)] private float _generatorShakePositionAmplitude = 0.05f;

    [Tooltip("Амплитуда вращения при импульсной тряске генератора при попадании в зелёную зону (градусы).")]
    [SerializeField, Min(0f)] private float _generatorShakeRotationAmplitude = 2f;

    [Header("Miss Shake")]
    [Tooltip("Амплитуда смещения позиции при импульсной тряске генератора при промахе (метры).")]
    [SerializeField, Min(0f)] private float _missShakePositionAmplitude = 0.08f;

    [Tooltip("Амплитуда вращения при импульсной тряске генератора при промахе (градусы).")]
    [SerializeField, Min(0f)] private float _missShakeRotationAmplitude = 3f;

    [Header("Start Button")]
    [Tooltip("Кнопка запуска мини-игры (Cylinder с ButtonPressAnimation). " +
             "Мини-игра запускается только при нажатии этой кнопки, когда все предметы установлены.")]
    [SerializeField] private ButtonPressAnimation _startButton;

    [Header("Drop Slots")]
    [Tooltip("Слоты установки предметов. Каждый слот — свой предмет, свой якорь, опциональная анимация заливки.")]
    [SerializeField] private GeneratorDropSlot[] _dropSlots;

    [Header("Common")]
    [Tooltip("Слой якорей дропа для Raycast (например Interactable Layer).")]
    [SerializeField] private LayerMask _anchorLayer;

    [Tooltip("Материал ghost-превью предмета. Если пуст — загружается CardLamp_Ghost.mat из Resources.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Подсказка при наведении предмета на якорь генератора.")]
    [SerializeField] private string _dropHint = "Установить в генератор";

    [Header("Missing Item Hints")]
    [Tooltip("Подсказка при нажатии Cylinder, если не установлен ни один предмет.")]
    [SerializeField] private string _hintNoItems = "Установите оба предмета в генератор.";

    [Tooltip("Подсказка при нажатии Cylinder, если установлен только первый предмет (Item 1).")]
    [SerializeField] private string _hintOnlyFirstItem = "Установите второй предмет в генератор.";

    [Tooltip("Подсказка при нажатии Cylinder, если установлен только второй предмет (Item 2).")]
    [SerializeField] private string _hintOnlySecondItem = "Установите первый предмет в генератор.";

    // ── Slot Definition ─────────────────────────────────────────────────────────

    [Serializable]
    private class GeneratorDropSlot
    {
        [Tooltip("Предмет, который нужно установить в этот слот.")]
        public ItemData item;

        [Tooltip("Коллайдер якоря дропа — цель рейкаста при отпускании предмета.")]
        public Collider anchorCollider;

        [Tooltip("Точка спавна визуала / ghost-превью.")]
        public Transform anchorTransform;

        [Tooltip("Отдельная точка спавна для анимации заливки. Если пуст — используется anchorTransform.")]
        public Transform pourSpawnTransform;

        [Tooltip("Prefab визуала вставленного предмета. Если пуст — берётся inspectionPrefab предмета.")]
        public GameObject placedPrefab;

        [Tooltip("Звук установки предмета в генератор.")]
        public AudioClip insertClip;

        [Tooltip("Громкость звука установки.")]
        [Range(0f, 1f)] public float insertVolume = 1f;

        [Header("Pour Animation (optional)")]
        [Tooltip("Включить анимацию залития топлива через Animator вместо статичного спавна.")]
        public bool playPourAnimation;

        [Tooltip("Имя trigger-параметра в Animator Controller для запуска анимации заливки.")]
        public string pourAnimTrigger = "Pour";

        [Tooltip("Имя состояния анимации заливки в Animator Controller (для отслеживания завершения).")]
        public string pourStateName = "Pour";

        [Tooltip("Звук заливки топлива. Проигрывается в момент запуска анимации.")]
        [SerializeField] private AudioClip _pourClip;

        [Tooltip("Громкость звука заливки.")]
        [Range(0f, 1f)] [SerializeField] private float _pourVolume = 1f;

        [Header("Pour VFX (optional)")]
        [Tooltip("Эффект наливания бензина (GasolinePourEffect). " +
                 "Запускается синхронно с началом анимации заливки и останавливается по её завершении.")]
        [SerializeField] private GasolinePourEffect _pourEffect;

        // ── Public accessors ──

        public AudioClip PourClip => _pourClip;
        public float PourVolume => _pourVolume;
        public GasolinePourEffect PourEffect => _pourEffect;
    }

    // ── State ───────────────────────────────────────────────────────────────────

    private readonly HashSet<string> _placedItemIds = new HashSet<string>();
    private bool _isSolved;
    private bool _isProcessing; // true во время анимации заливки
    private bool _allItemsReady; // true когда все предметы установлены — кнопка активна

    // Ghost preview
    private GameObject _ghostPreview;
    private Material _runtimeGhostMaterial;
    private bool _ghostVisible;
    private string _ghostItemId; // для пересоздания ghost при смене предмета

    // ── ISaveable ───────────────────────────────────────────────────────────────

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
        {
            _minigame.OnCompleted += HandleMinigameCompleted;
            _minigame.OnHit += HandleMinigameHit;
            _minigame.OnMiss += HandleMinigameMiss;
        }

        if (_startButton != null)
            _startButton.OnPressed += HandleStartButtonPressed;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited -= HandleExited;
        }

        if (_minigame != null)
        {
            _minigame.OnCompleted -= HandleMinigameCompleted;
            _minigame.OnHit -= HandleMinigameHit;
            _minigame.OnMiss -= HandleMinigameMiss;
        }

        if (_startButton != null)
            _startButton.OnPressed -= HandleStartButtonPressed;
    }

    private void Start()
    {
        // Восстановление визуала уже вставленных предметов после загрузки.
        // Слоты с анимацией заливки не восстанавливают визуал — канистра была израсходована.
        foreach (var slot in _dropSlots)
        {
            if (slot == null || slot.item == null) continue;
            if (_placedItemIds.Contains(slot.item.ItemId) && !slot.playPourAnimation)
                SpawnPlacedVisual(slot, slot.item);
        }

        SetMinigameVisible(false);

        if (_isSolved)
        {
            PlayCompletionVfx();
            EnableGeneratorShake();
            _controller?.SetSolved();
        }
        else
        {
            StopCompletionVfx();
        }
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
        if (_isSolved || _isProcessing || _controller == null || !_controller.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false, null);
            return;
        }

        if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem != null
            && CanAccept(PuzzleInventoryBar.DraggedItem))
        {
            var slot = FindSlotForItem(PuzzleInventoryBar.DraggedItem);
            if (slot == null)
            {
                if (_ghostVisible) SetGhostVisible(false, null);
                return;
            }

            bool isHovering = IsMouseOverSlot(slot);
            if (isHovering && !_ghostVisible)
                SetGhostVisible(true, slot);
            else if (!isHovering && _ghostVisible)
                SetGhostVisible(false, null);
        }
        else if (_ghostVisible)
        {
            SetGhostVisible(false, null);
        }
    }

    private bool IsMouseOverSlot(GeneratorDropSlot slot)
    {
        if (Mouse.current == null || slot == null || slot.anchorCollider == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        if (Camera.main == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, _anchorLayer))
            return hit.collider == slot.anchorCollider;

        return false;
    }

    private void SetGhostVisible(bool visible, GeneratorDropSlot slot)
    {
        if (visible && slot != null)
        {
            // Пересоздаём ghost если предмет сменился
            if (_ghostPreview == null || _ghostItemId != slot.item.ItemId)
            {
                if (_ghostPreview != null)
                    Destroy(_ghostPreview);

                CreateGhostPreview(slot);
                _ghostItemId = slot.item.ItemId;
            }

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

    private void CreateGhostPreview(GeneratorDropSlot slot)
    {
        var ghostPoint = slot.pourSpawnTransform != null ? slot.pourSpawnTransform : slot.anchorTransform;
        if (ghostPoint == null || slot.item == null) return;

        EnsureGhostMaterial();

        var prefab = slot.placedPrefab != null ? slot.placedPrefab
                    : slot.item.inspectionPrefab;
        if (prefab == null) return;

        _ghostPreview = Instantiate(prefab, ghostPoint.position,
                                    ghostPoint.rotation, ghostPoint);
        _ghostPreview.name = slot.item.itemName + "Ghost";

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
        _allItemsReady = AllItemsPlaced();
        SetMinigameVisible(false);
    }

    private void HandleExited()
    {
        SetGhostVisible(false, null);
        SetMinigameVisible(false);
        _minigame?.StopMinigame();
        StopHitVfx();
        _allItemsReady = false;
    }

    private void HandleMinigameCompleted()
    {
        if (_isSolved) return;

        _isSolved = true;
        _allItemsReady = false;
        SetMinigameVisible(false);
        StopHitVfx();
        PlayCompletionVfx();
        EnableGeneratorShake();
        _controller?.SetSolved(); // выходит из режима пазла и сохраняет прогресс
    }

    /// <summary>Запускает VFX завершения мини-игры (дым работающего генератора).</summary>
    private void PlayCompletionVfx()
    {
        if (_completionVfx == null) return;
        _completionVfx.gameObject.SetActive(true);
        _completionVfx.Play(true);
    }

    /// <summary>Останавливает VFX завершения мини-игры и деактивирует его GameObject.</summary>
    private void StopCompletionVfx()
    {
        if (_completionVfx == null) return;
        _completionVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _completionVfx.gameObject.SetActive(false);
    }

    /// <summary>Включает постоянную тряску генератора (работающий двигатель).</summary>
    private void EnableGeneratorShake()
    {
        if (_generatorShake != null)
            _generatorShake.SetContinuous(true);
    }

    /// <summary>Выключает постоянную тряску генератора.</summary>
    private void DisableGeneratorShake()
    {
        if (_generatorShake != null)
            _generatorShake.SetContinuous(false);
    }

    /// <summary>Проигрывает звук и VFX попадания в зелёную зону, запускает импульсную тряску генератора.</summary>
    private void HandleMinigameHit()
    {
        if (_minigame != null && _minigame.SuccessStreak < _minigame.RequiredSuccesses)
            AudioManager.Instance?.PlaySFXExclusive(_successClip, _successVolume);

        if (_hitVfx != null)
        {
            _hitVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _hitVfx.Play(true);
        }

        if (_generatorShake != null)
            _generatorShake.Shake(_generatorShakePositionAmplitude, _generatorShakeRotationAmplitude, HitShakeDuration);
    }

    /// <summary>Проигрывает звук промаха, останавливает ранее запущенный звук успеха и запускает импульсную тряску генератора.</summary>
    private void HandleMinigameMiss()
    {
        AudioManager.Instance?.PlaySFXExclusive(_failClip, _failVolume);

        if (_generatorShake != null)
            _generatorShake.Shake(_missShakePositionAmplitude, _missShakeRotationAmplitude, MissShakeDuration);
    }

    /// <summary>Останавливает и очищает VFX попадания.</summary>
    private void StopHitVfx()
    {
        if (_hitVfx != null)
            _hitVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ── Start Button ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Вызывается при нажатии кнопки запуска (ButtonPressAnimation.OnPressed).
    /// Запускает мини-игру если все предметы установлены.
    /// Если предметы установлены не полностью — показывает подсказку в зависимости от того, каких предметов не хватает.
    /// </summary>
    private void HandleStartButtonPressed()
    {
        if (_isSolved || _isProcessing)
            return;

        if (_controller == null || !_controller.IsActive)
            return;

        if (_allItemsReady)
        {
            StartMinigame();
            _allItemsReady = false; // предотвращаем повторный запуск
            return;
        }

        ShowMissingItemHint();
    }

    /// <summary>
    /// Показывает подсказку в зависимости от того, какие предметы установлены в генератор:
    /// 1) ни один не установлен — _hintNoItems,
    /// 2) установлен только Item 1 — _hintOnlyFirstItem,
    /// 3) установлен только Item 2 — _hintOnlySecondItem.
    /// </summary>
    private void ShowMissingItemHint()
    {
        if (_dropSlots == null || _dropSlots.Length < 2)
            return;

        bool item1Placed = IsSlotItemPlaced(_dropSlots[0]);
        bool item2Placed = IsSlotItemPlaced(_dropSlots[1]);

        string hint = null;

        if (!item1Placed && !item2Placed)
            hint = _hintNoItems;
        else if (item1Placed && !item2Placed)
            hint = _hintOnlyFirstItem;
        else if (!item1Placed && item2Placed)
            hint = _hintOnlySecondItem;

        if (!string.IsNullOrEmpty(hint))
            PopupMessageSystem.Instance?.Show(hint, PopupMessageType.Hint);
    }

    /// <summary>Возвращает true, если предмет из указанного слота уже установлен в генератор.</summary>
    private bool IsSlotItemPlaced(GeneratorDropSlot slot)
    {
        return slot != null && slot.item != null && _placedItemIds.Contains(slot.item.ItemId);
    }

    // ── IPuzzleExitGuard ────────────────────────────────────────────────────────

    /// <summary>Блокирует выход из пазла во время анимации заливки топлива.</summary>
    public bool CanExitPuzzle() => !_isProcessing;

    // ── IPuzzleDropTarget ───────────────────────────────────────────────────────

    /// <summary>Возвращает текст-подсказку при наведении предмета на якорь генератора.</summary>
    public string GetDropHint() => _dropHint;

    /// <summary>True, если предмет подходит для установки и ещё не размещён.</summary>
    public bool CanAccept(ItemData item)
    {
        if (item == null || _isSolved || _isProcessing) return false;
        return FindSlotForItem(item) != null && !_placedItemIds.Contains(item.ItemId);
    }

    // ── IPuzzleDropHandler ──────────────────────────────────────────────────────

    /// <summary>
    /// Принимает предмет, брошенный из инвентарного бара на соответствующий якорь.
    /// SparkPlug → SpartkPlugInput (статичный спавн).
    /// Canister → CanisterInput (анимация залития топлива).
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;

        if (item == null || _isSolved || _isProcessing)
            return false;

        var slot = FindSlotForItem(item);
        if (slot == null || _placedItemIds.Contains(item.ItemId))
            return false;

        var cam = Camera.main;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, RaycastDistance, _anchorLayer))
            return false;

        if (hit.collider != slot.anchorCollider)
            return false;

        SetGhostVisible(false, null);

        // Помечаем как установленный ДО анимации — корректно для системы сохранений.
        _placedItemIds.Add(item.ItemId);
        SaveManager.Instance?.Save();

        if (slot.playPourAnimation)
        {
            StartCoroutine(PourAnimationRoutine(slot, item));
        }
        else
        {
            SpawnPlacedVisual(slot, item);
            AudioManager.Instance?.PlaySFX(slot.insertClip, slot.insertVolume);
            TryStartMinigameIfReady();
        }

        return true; // предмет принят и удаляется из инвентаря
    }

    // ── Pour Animation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Анимация залития топлива через Animator: канистра появляется,
    /// проигрывает анимацию заливки, затем удаляется.
    /// </summary>
    private IEnumerator PourAnimationRoutine(GeneratorDropSlot slot, ItemData item)
    {
        _isProcessing = true;

        // Точка спавна — отдельная pourSpawnTransform, если назначена; иначе anchorTransform.
        var spawnPoint = slot.pourSpawnTransform != null ? slot.pourSpawnTransform : slot.anchorTransform;

        var prefab = slot.placedPrefab != null ? slot.placedPrefab
                    : (item != null ? item.inspectionPrefab : null);
        if (prefab == null || spawnPoint == null)
        {
            _isProcessing = false;
            TryStartMinigameIfReady();
            yield break;
        }

        var canisterObj = Instantiate(prefab, spawnPoint.position,
                                       spawnPoint.rotation, spawnPoint);
        canisterObj.name = "CanisterPour";

        foreach (var col in canisterObj.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Звук установки
        AudioManager.Instance?.PlaySFX(slot.insertClip, slot.insertVolume);

        var animator = canisterObj.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"[{nameof(GeneratorPuzzleController)}] На префабе канистры нет Animator. Анимация заливки не будет проиграна.", this);
            _isProcessing = false;
            TryStartMinigameIfReady();
            yield break;
        }

        // Запускаем анимацию заливки через trigger
        animator.SetTrigger(slot.pourAnimTrigger);

        // Ждём перехода в состояние анимации заливки
        yield return null;
        float timeout = 5f;
        float elapsed = 0f;
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(slot.pourStateName) && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Проигрываем звук заливки в начале анимации
        AudioManager.Instance?.PlaySFX(slot.PourClip, slot.PourVolume);

        // Запускаем эффект наливания бензина синхронно с анимацией
        slot.PourEffect?.StartPour();

        // Ждём завершения анимации заливки
        while (animator.GetCurrentAnimatorStateInfo(0).IsName(slot.pourStateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            yield return null;
        }

        // Останавливаем эффект наливания — анимация завершена
        slot.PourEffect?.StopPour();

        // Удаляем канистру — топливо залито, предмет израсходован
        Destroy(canisterObj);

        _isProcessing = false;
        TryStartMinigameIfReady();
    }

    /// <summary>Помечает что все предметы установлены — кнопка запуска становится активной.</summary>
    private void TryStartMinigameIfReady()
    {
        if (AllItemsPlaced())
            _allItemsReady = true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private GeneratorDropSlot FindSlotForItem(ItemData item)
    {
        if (item == null || _dropSlots == null) return null;

        foreach (var slot in _dropSlots)
        {
            if (slot != null && slot.item != null && slot.item.ItemId == item.ItemId)
                return slot;
        }

        return null;
    }

    private bool AllItemsPlaced()
    {
        if (_dropSlots == null || _dropSlots.Length == 0) return false;
        return _placedItemIds.Count >= _dropSlots.Length;
    }

    private void SpawnPlacedVisual(GeneratorDropSlot slot, ItemData item)
    {
        if (slot.anchorTransform == null) return;

        var prefab = slot.placedPrefab != null ? slot.placedPrefab
                                               : (item != null ? item.inspectionPrefab : null);
        if (prefab == null) return;

        Instantiate(prefab, slot.anchorTransform.position,
                    slot.anchorTransform.rotation, slot.anchorTransform);
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
