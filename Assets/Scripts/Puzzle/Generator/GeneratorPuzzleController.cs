using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Управляет загадкой генератора. Принимает нужные предметы из PuzzleInventoryBar,
/// брошенные на якорь SpartkPlugInput, спавнит их визуал и после установки всех
/// предметов запускает timing-мини-игру. По завершению мини-игры пазл считается решённым.
///
/// Работает совместно с PuzzleModeController: тот входит/выходит из режима пазла,
/// показывает инвентарный бар и находит этот компонент как IPuzzleDropHandler.
/// </summary>
[DefaultExecutionOrder(-7)]
public class GeneratorPuzzleController : MonoBehaviour, IPuzzleDropHandler, ISaveable
{
    private const string SaveIdConst = "generator_puzzle";

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

    private const float RaycastDistance = 100f;

    private readonly HashSet<string> _placedItemIds = new HashSet<string>();
    private bool _isSolved;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => SaveIdConst;

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

    private void OnDestroy() => SaveManager.Instance?.Unregister(this);

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
