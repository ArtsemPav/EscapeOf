using System;
using UnityEngine;

[Serializable]
public struct PaintingCondition
{
    [Tooltip("The painting column (Q1–Q4) this condition applies to.")]
    public PaintingColumn column;

    [Tooltip("Height the painting must be at for the symbol to appear.")]
    public PaintingHeight requiredHeight;

    [Tooltip("Spotlight that must be powered. Leave null if no spotlight required.")]
    public PaintingSpotlight spotlight;

    [Tooltip("Color the spotlight must be emitting for the symbol to appear. " +
             "For L3 set Green — it is synthesized from L2+L4. Set None to skip the color check.")]
    public LensColor requiredColor;

    [Tooltip("If true, the painting room light zone must be OFF for the symbol to appear.")]
    public bool requireRoomLightOff;

    [Tooltip("The symbol GameObject to show/hide (SpriteRenderer child on the painting).")]
    public GameObject symbolObject;
}

/// <summary>
/// Central controller for the Loop Puzzle. Evaluates all four PaintingConditions
/// on every state change and opens the hidden door when all symbols are visible.
/// Room light state is read from LightingSystem by zone ID — no direct reference needed,
/// so RoomLightSwitch and room lights can live in any prefab.
/// Persists the solved state via ISaveable.
/// </summary>
public class LoopPuzzleController : MonoBehaviour, ISaveable
{
    [Header("Save")]
    [SerializeField] private string _saveId = "loop_puzzle_controller";

    [Header("References")]
    [SerializeField] private LoopPuzzlePowerCircuit _powerCircuit;
    [SerializeField] private LoopPuzzleHiddenDoor _hiddenDoor;

    [Header("Room Light Zone")]
    [Tooltip("ZoneId of the painting room lights in LightingSystem. " +
             "Must match the ZoneId on LightZone components and PaintingRoomLightSwitch.")]
    [SerializeField] private string _roomLightZoneId = "painting_room";

    [Header("Painting Conditions (Q1–Q4)")]
    [SerializeField] private PaintingCondition[] _conditions;

    private bool _isSolved;
    private bool _roomLightOff;

    /// <summary>True if the puzzle has been solved.</summary>
    public bool IsSolved => _isSolved;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { isSolved = _isSolved });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.isSolved;
    }

    [Serializable]
    private struct SaveData { public bool isSolved; }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_isSolved)
        {
            HideAllSymbols();
            return;
        }

        SubscribeToEvents();

        // Read initial room light state from LightingSystem
        if (LightingSystem.Instance != null)
            _roomLightOff = !LightingSystem.Instance.GetZoneSwitchState(_roomLightZoneId);

        RefreshAllSymbols();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        SaveManager.Instance?.Unregister(this);
    }

    // ── Event Subscriptions ────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (_powerCircuit != null)
            _powerCircuit.OnPowerChanged += OnAnyStateChanged;

        foreach (var cond in _conditions)
        {
            if (cond.column != null)
                cond.column.OnHeightChanged += OnAnyStateChanged;

            if (cond.spotlight != null)
                cond.spotlight.OnLensChanged += OnAnyStateChanged;
        }

        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnZoneSwitchChanged += OnZoneSwitchChanged;
    }

    private void UnsubscribeFromEvents()
    {
        if (_powerCircuit != null)
            _powerCircuit.OnPowerChanged -= OnAnyStateChanged;

        foreach (var cond in _conditions)
        {
            if (cond.column != null)
                cond.column.OnHeightChanged -= OnAnyStateChanged;

            if (cond.spotlight != null)
                cond.spotlight.OnLensChanged -= OnAnyStateChanged;
        }

        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnZoneSwitchChanged -= OnZoneSwitchChanged;
    }

    // ── State Change Handlers ──────────────────────────────────────────────────

    private void OnAnyStateChanged()
    {
        RefreshAllSymbols();
        CheckWinCondition();
    }

    private void OnZoneSwitchChanged(string zoneId, bool isSwitchedOn)
    {
        if (zoneId != _roomLightZoneId) return;
        _roomLightOff = !isSwitchedOn;
        RefreshAllSymbols();
        CheckWinCondition();
    }

    // ── Symbol Logic ───────────────────────────────────────────────────────────

    private void RefreshAllSymbols()
    {
        foreach (var cond in _conditions)
            RefreshSymbol(cond);
    }

    private void RefreshSymbol(PaintingCondition cond)
    {
        if (cond.symbolObject == null)
        {
            Debug.LogWarning("[LoopPuzzle] symbolObject is NULL — assign it in the Inspector.");
            return;
        }

        bool heightOk    = cond.column != null && cond.column.CurrentHeight == cond.requiredHeight;
        bool spotlightOk = cond.spotlight == null || cond.spotlight.IsPowered;
        bool colorOk     = cond.spotlight == null
                           || cond.requiredColor == LensColor.None
                           || cond.spotlight.GetEffectiveColor() == cond.requiredColor;
        bool roomLightOk = !cond.requireRoomLightOff || _roomLightOff;

        bool result = heightOk && spotlightOk && colorOk && roomLightOk;

        cond.symbolObject.SetActive(result);
    }

    private void HideAllSymbols()
    {
        foreach (var cond in _conditions)
            if (cond.symbolObject != null) cond.symbolObject.SetActive(false);
    }

    // ── Win Condition ──────────────────────────────────────────────────────────

    private void CheckWinCondition()
    {
        if (_isSolved) return;

        foreach (var cond in _conditions)
            if (cond.symbolObject == null || !cond.symbolObject.activeSelf) return;

        OnPuzzleSolved();
    }

    private void OnPuzzleSolved()
    {
        _isSolved = true;
        _hiddenDoor?.Open();
        Debug.Log("[LoopPuzzleController] Puzzle solved — hidden door opened.");
        SaveManager.Instance?.Save();
    }
}
