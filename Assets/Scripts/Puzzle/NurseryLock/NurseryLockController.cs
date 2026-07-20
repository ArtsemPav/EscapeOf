using System;
using System.Collections;
using ChemicalPuzzle;
using Escape.Core;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Универсальный контроллер загадки "Взлом замка отмычкой".
/// Игрок перетаскивает отмычку из инвентаря на замок → анимация вставки →
/// 2D мини-игра с концентрическими кольцами → анимация открытия → двери открываются.
///
/// Префаб самодостаточен: все ссылки находятся автоматически из дочерних объектов.
/// Панель мини-игры (_minigamePanel) должна существовать в Canvas сцены и быть
/// назначена в Inspector. Для нового экземпляра достаточно сменить _saveId,
/// назначить _requiredItem / _minigamePanel / двери (опционально).
///
/// Требует PuzzleModeController на том же объекте.
/// Animator на дочернем объекте с параметрами-триггерами: "InsertLockpick", "OpenLock"
/// и стейтами: Idle → Inserting → LockPickIdle → opening.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class NurseryLockController : MonoBehaviour,
    IPuzzleDropHandler, IPuzzleExitGuard, ISaveable
{
    // ── Constants ───────────────────────────────────────────────────────────────

    private const string ParamInsertLockpick = "InsertLockpick";
    private const string ParamOpenLock = "OpenLock";

    private const string StateInserting = "Inserting";
    private const string StateLockPickIdle = "LockPickIdle";
    private const string StateOpening = "opening";

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Save")]
    [Tooltip("Уникальный ID для системы сохранений. Должен отличаться для каждого экземпляра загадки.")]
    [SerializeField] private string _saveId = "nursery_lock_puzzle";

    [Header("Item")]
    [Tooltip("ItemData отмычки, которую нужно перетащить на замок.")]
    [SerializeField] private ItemData _requiredItem;

    [Header("Auto-Resolved (override if needed)")]
    [Tooltip("Collider замка — цель рейкаста при дропе. Если null — ищется первый Collider на дочернем объекте с Animator.")]
    [SerializeField] private Collider _lockCollider;

    [Tooltip("Animator замка. Если null — ищется GetComponentInChildren<Animator>().")]
    [SerializeField] private Animator _lockAnimator;

    [Tooltip("MeshRenderer 3D-модели отмычки. Если null — ищется по имени 'Lockpick' среди дочерних объектов.")]
    [SerializeField] private MeshRenderer _lockpickRenderer;

    [Tooltip("Material для ghost-превью отмычки. Если null — создаётся программно.")]
    [SerializeField] private Material _ghostMaterial;

    [Header("Minigame")]
    [Tooltip("Панель UI мини-игры в Canvas сцены. Должна содержать компонент LockPickMinigame.")]
    [SerializeField] private GameObject _minigamePanel;

    [Tooltip("Компонент LockPickMinigame на панели. Если null — берётся GetComponent<LockPickMinigame>() с _minigamePanel.")]
    [SerializeField] private LockPickMinigame _minigame;

    [Header("Doors (optional)")]
    [Tooltip("Левая дверь. Опционально — если null, ищется автоматически среди дочерних DoorInteraction.")]
    [SerializeField] private DoorInteraction _doorLeft;

    [Tooltip("Правая дверь. Опционально — если null, ищется автоматически среди дочерних DoorInteraction.")]
    [SerializeField] private DoorInteraction _doorRight;

    [Header("Audio")]
    [SerializeField] private AudioClip _insertClip;
    [SerializeField] private AudioClip _openClip;
    [SerializeField, Range(0f, 1f)] private float _insertVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _openVolume = 1f;

    // ── State ───────────────────────────────────────────────────────────────────

    private bool _isSolved;
    private bool _isLockpickInserted;
    private bool _isProcessing;

    private PuzzleModeController _puzzleMode;
    private Material _runtimeGhostMaterial;
    private Material[] _lockpickOriginalMaterials;
    private bool _ghostVisible;

    // ── Public API ──────────────────────────────────────────────────────────────

    public bool IsSolved => _isSolved;

    // ── ISaveable ───────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData
        {
            isSolved = _isSolved,
            lockpickInserted = _isLockpickInserted
        });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.isSolved;
        _isLockpickInserted = data.lockpickInserted;
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
        public bool lockpickInserted;
    }

    // ── Unity Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        AutoResolveReferences();

        // Скрываем 3D-модель отмычки до дропа
        if (_lockpickRenderer != null)
        {
            _lockpickOriginalMaterials = _lockpickRenderer.sharedMaterials;
            _lockpickRenderer.enabled = false;
        }

        EnsureGhostMaterial();

        // Находим компонент мини-игры на панели
        if (_minigame == null && _minigamePanel != null)
            _minigame = _minigamePanel.GetComponent<LockPickMinigame>();

        // Панель скрыта по умолчанию
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
            _minigame.OnCompleted += HandleMinigameCompleted;

        if (_isSolved)
        {
            RestoreSolvedState();
        }
        else if (_isLockpickInserted)
        {
            // Отмычка уже вставлена — пропускаем к LockPickIdle
            if (_lockAnimator != null)
                _lockAnimator.Play(StateLockPickIdle, 0, 0f);
        }
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered -= HandlePuzzleEntered;
            _puzzleMode.OnExited -= HandlePuzzleExited;
        }

        if (_minigame != null)
            _minigame.OnCompleted -= HandleMinigameCompleted;

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_isSolved || _isProcessing || _puzzleMode == null || !_puzzleMode.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false);
            return;
        }

        // Ghost-превью отмычки при перетаскивании
        if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem == _requiredItem)
        {
            bool hovering = IsMouseOverLock();
            if (hovering && !_ghostVisible)
                SetGhostVisible(true);
            else if (!hovering && _ghostVisible)
                SetGhostVisible(false);
        }
        else if (_ghostVisible)
        {
            SetGhostVisible(false);
        }
    }

    // ── Auto-Resolve ────────────────────────────────────────────────────────────

    /// <summary>Находит все ссылки из дочерних объектов, если они не назначены вручную.</summary>
    private void AutoResolveReferences()
    {
        if (_lockAnimator == null)
            _lockAnimator = GetComponentInChildren<Animator>();

        if (_lockCollider == null && _lockAnimator != null)
        {
            // Collider на том же объекте что и Animator, или на его родителе/детях
            _lockCollider = _lockAnimator.GetComponent<Collider>();
            if (_lockCollider == null)
                _lockCollider = _lockAnimator.GetComponentInChildren<Collider>();
            if (_lockCollider == null)
                _lockCollider = _lockAnimator.GetComponentInParent<Collider>();
        }

        if (_lockCollider == null)
            _lockCollider = GetComponentInChildren<Collider>();

        if (_lockpickRenderer == null)
        {
            // Ищем дочерний объект по имени "Lockpick" (case-insensitive)
            var allRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in allRenderers)
            {
                if (r.name.Equals("Lockpick", StringComparison.OrdinalIgnoreCase))
                {
                    _lockpickRenderer = r;
                    break;
                }
            }
        }

        if (_doorLeft == null || _doorRight == null)
        {
            // Ищем DoorInteraction среди дочерних объектов
            var doors = GetComponentsInChildren<DoorInteraction>(true);
            if (doors.Length >= 1 && _doorLeft == null)
                _doorLeft = doors[0];
            if (doors.Length >= 2 && _doorRight == null)
                _doorRight = doors[1];
        }
    }

    // ── IPuzzleDropHandler ──────────────────────────────────────────────────────

    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = item; // отмычка не расходуется до победы

        if (_isProcessing || _isSolved || _isLockpickInserted)
            return false;

        if (item == null || item != _requiredItem)
            return false;

        if (!PerformRaycast(screenPosition))
            return false;

        // Отмычка принята, но не расходуется — стартуем последовательность
        _isLockpickInserted = true;
        _isProcessing = true;

        SetGhostVisible(false);

        // Скрываем инвентарь-бар — отмычка больше не нужна до конца
        PuzzleInventoryBar.Instance?.Hide();

        SaveManager.Instance?.Save();

        StartCoroutine(InsertSequence());
        return true;
    }

    // ── IPuzzleExitGuard ────────────────────────────────────────────────────────

    public bool CanExitPuzzle()
    {
        // Блокируем выход во время анимации вставки и мини-игры
        return !_isProcessing;
    }

    // ── Puzzle Flow ─────────────────────────────────────────────────────────────

    private void HandlePuzzleEntered()
    {
        if (_isSolved) return;

        if (_isLockpickInserted)
        {
            // Отмычка уже вставлена (после загрузки) — сразу в мини-игру
            _isProcessing = true;
            PuzzleInventoryBar.Instance?.Hide();
            StartCoroutine(StartMinigameAfterFrame());
        }
    }

    private void HandlePuzzleExited()
    {
        if (_minigame != null)
            _minigame.StopMinigame();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        // Если отмычка вставлена, но мини-игра не решена — сбрасываем processing
        // чтобы при следующем входе сразу запустить мини-игру
        if (_isProcessing && !_isSolved)
            _isProcessing = false;
    }

    private IEnumerator InsertSequence()
    {
        // Включаем 3D-модель отмычки с оригинальными материалами
        if (_lockpickRenderer != null)
        {
            _lockpickRenderer.enabled = true;
            if (_lockpickOriginalMaterials != null)
                _lockpickRenderer.sharedMaterials = _lockpickOriginalMaterials;
        }

        AudioManager.Instance?.PlaySFX(_insertClip, _insertVolume);

        // Триггерим анимацию вставки
        if (_lockAnimator != null)
            _lockAnimator.SetTrigger(ParamInsertLockpick);

        // Ждём пока аниматор перейдёт в LockPickIdle (через Inserting с Has Exit Time)
        if (_lockAnimator != null)
        {
            yield return null; // ждём кадр чтобы триггер сработал

            // Ждём входа в Inserting
            float timeout = 5f;
            float elapsed = 0f;
            while (!_lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateInserting) && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ждём перехода в LockPickIdle
            while (_lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateInserting))
                yield return null;
        }

        // Запускаем мини-игру
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
        StartCoroutine(OpeningSequence());
    }

    private IEnumerator OpeningSequence()
    {
        if (_minigame != null)
            _minigame.StopMinigame();

        if (_minigamePanel != null)
            _minigamePanel.SetActive(false);

        AudioManager.Instance?.PlaySFX(_openClip, _openVolume);

        // Триггерим анимацию открытия замка
        if (_lockAnimator != null)
            _lockAnimator.SetTrigger(ParamOpenLock);

        // Ждём анимацию открытия
        if (_lockAnimator != null)
        {
            yield return null;

            float timeout = 10f;
            float elapsed = 0f;
            while (!_lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateOpening) && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ждём завершения opening
            while (_lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(StateOpening) &&
                   _lockAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
        }

        // Расходуем отмычку
        if (_requiredItem != null && InventorySystem.Instance != null)
            InventorySystem.Instance.RemoveItem(_requiredItem);

        // Открываем двери (опционально)
        _doorLeft?.UnlockAndOpen();
        _doorRight?.UnlockAndOpen();

        _isSolved = true;
        _isProcessing = false;

        SaveManager.Instance?.Save();
        _puzzleMode?.SetSolved();
    }

    // ── Solved Restore ──────────────────────────────────────────────────────────

    private void RestoreSolvedState()
    {
        // Перематываем аниматор в конец opening
        if (_lockAnimator != null)
        {
            _lockAnimator.Play(StateOpening, 0, 1f);
        }

        // Разблокируем и открываем двери (опционально)
        if (_doorLeft != null)
        {
            _doorLeft.Unlock();
            _doorLeft.UnlockAndOpen();
        }
        if (_doorRight != null)
        {
            _doorRight.Unlock();
            _doorRight.UnlockAndOpen();
        }

        // Скрываем отмычку (она уже использована)
        if (_lockpickRenderer != null)
            _lockpickRenderer.enabled = false;
    }

    // ── Ghost Preview ───────────────────────────────────────────────────────────

    private void EnsureGhostMaterial()
    {
        if (_ghostMaterial != null)
        {
            _runtimeGhostMaterial = _ghostMaterial;
            return;
        }

        // Создаём полупрозрачный материал для ghost-превью
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        _runtimeGhostMaterial = new Material(shader);
        _runtimeGhostMaterial.color = new Color(0.8f, 0.9f, 1f, 0.35f);

        // Для URP/Unlit включаем прозрачность
        if (_runtimeGhostMaterial.HasProperty("_Surface"))
        {
            _runtimeGhostMaterial.SetFloat("_Surface", 1); // Transparent
            _runtimeGhostMaterial.SetFloat("_Blend", 0);   // Alpha
        }
    }

    private void SetGhostVisible(bool visible)
    {
        _ghostVisible = visible;
        if (_lockpickRenderer == null) return;

        if (visible)
        {
            _lockpickRenderer.enabled = true;

            if (_runtimeGhostMaterial != null && _lockpickOriginalMaterials != null)
            {
                var ghostMats = new Material[_lockpickOriginalMaterials.Length];
                for (int i = 0; i < ghostMats.Length; i++)
                    ghostMats[i] = _runtimeGhostMaterial;
                _lockpickRenderer.sharedMaterials = ghostMats;
            }
        }
        else
        {
            if (!_isProcessing && !_isLockpickInserted)
            {
                _lockpickRenderer.enabled = false;
            }
            else if (_lockpickOriginalMaterials != null)
            {
                _lockpickRenderer.sharedMaterials = _lockpickOriginalMaterials;
            }
        }
    }

    // ── Raycast ─────────────────────────────────────────────────────────────────

    private bool IsMouseOverLock()
    {
        if (Mouse.current == null) return false;
        return PerformRaycast(Mouse.current.position.ReadValue());
    }

    private bool PerformRaycast(Vector2 screenPos)
    {
        if (Camera.main == null || _lockCollider == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider == _lockCollider ||
                   (hit.collider.transform != null && hit.collider.transform.IsChildOf(_lockCollider.transform));
        }
        return false;
    }
}
