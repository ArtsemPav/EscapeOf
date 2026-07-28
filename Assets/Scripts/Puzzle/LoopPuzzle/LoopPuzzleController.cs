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
    [SerializeField] private LoopPuzzlePowerCircuit    _powerCircuit;
    [SerializeField] private DrawerDrag                _rewardDrawer;
    [SerializeField] private PaintingRoomLightSwitch   _roomLightSwitch;

    [Header("Room Light Zone")]
    [Tooltip("ZoneId of the painting room lights in LightingSystem. " +
             "Must match the ZoneId on LightZone components and PaintingRoomLightSwitch.")]
    [SerializeField] private string _roomLightZoneId = "painting_room";

    [Header("Painting Conditions (Q1–Q4)")]
    [SerializeField] private PaintingCondition[] _conditions;

    [Header("Audio")]
    [Tooltip("Sound played once through AudioManager when the puzzle is solved.")]
    [SerializeField] private AudioClip _solvedClip;
    [SerializeField, Range(0f, 1f)] private float _solvedVolume = 1f;

    private bool _isSolved;
    private bool _roomLightOff;

    // Cached from save data — used to restore the solved visual state on load.
    private bool[] _savedSwitchStates;
    private int[]  _savedConditionLenses;

    /// <summary>True if the puzzle has been solved.</summary>
    public bool IsSolved => _isSolved;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        var data = new SaveData { isSolved = _isSolved };

        if (_isSolved)
        {
            // Persist the winning configuration so it can be restored visually on next load.
            data.switchStates    = _powerCircuit?.GetAllSwitchStates();
            data.conditionLenses = CollectConditionLenses();
        }

        return JsonUtility.ToJson(data);
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved            = data.isSolved;
        _savedSwitchStates   = data.switchStates;
        _savedConditionLenses = data.conditionLenses;
    }

    [Serializable]
    private struct SaveData
    {
        public bool   isSolved;
        public bool[] switchStates;     // S1–S6 states; null when puzzle is not yet solved
        public int[]  conditionLenses;  // LensColor cast to int per _conditions entry; -1 = no spotlight
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // _roomLightOff must be initialised before both solved and unsolved paths.
        if (LightingSystem.Instance != null)
            _roomLightOff = !LightingSystem.Instance.GetZoneSwitchState(_roomLightZoneId);

        if (_isSolved)
        {
            RestoreSolvedState();
            return;
        }

        // Randomize each column to a solvable starting height.
        // Columns that were loaded from a save keep their saved position.
        RandomizeColumns();

        SubscribeToEvents();
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
        {
            _powerCircuit.OnPowerChanged  += OnAnyStateChanged;
            _powerCircuit.OnMasterToggled += OnMasterToggled;
        }

        foreach (var cond in _conditions)
        {
            if (cond.column != null)
                cond.column.OnHeightChanged += OnAnyStateChanged;

            if (cond.spotlight != null)
                cond.spotlight.OnLensChanged += OnAnyStateChanged;
        }

        PaintingColumn.OnAnyMovingChanged += OnAnyColumnMovingChanged;

        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnZoneSwitchChanged += OnZoneSwitchChanged;
    }

    private void UnsubscribeFromEvents()
    {
        if (_powerCircuit != null)
        {
            _powerCircuit.OnPowerChanged  -= OnAnyStateChanged;
            _powerCircuit.OnMasterToggled -= OnMasterToggled;
        }

        foreach (var cond in _conditions)
        {
            if (cond.column != null)
                cond.column.OnHeightChanged -= OnAnyStateChanged;

            if (cond.spotlight != null)
                cond.spotlight.OnLensChanged -= OnAnyStateChanged;
        }

        PaintingColumn.OnAnyMovingChanged -= OnAnyColumnMovingChanged;

        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnZoneSwitchChanged -= OnZoneSwitchChanged;
    }

    // ── State Change Handlers ──────────────────────────────────────────────────

    private void OnAnyStateChanged()
    {
        RefreshAllSymbols();
        // Win condition is checked in OnAnyColumnMovingChanged when all columns stop.
        // For non-column state changes (spotlights, power, room light) check immediately
        // but only if no columns are currently moving.
        if (!PaintingColumn.IsAnyMoving)
            CheckWinCondition();
    }

    private void OnAnyColumnMovingChanged(bool anyMoving)
    {
        if (!anyMoving)
        {
            // All columns have stopped — safe to evaluate the win condition now.
            RefreshAllSymbols();
            CheckWinCondition();
        }
    }

    private void OnMasterToggled(bool isOn)
    {
        if (!isOn)
            ResetPuzzleState();
    }

    private void OnZoneSwitchChanged(string zoneId, bool isSwitchedOn)
    {
        if (zoneId != _roomLightZoneId) return;
        _roomLightOff = !isSwitchedOn;
        RefreshAllSymbols();
        if (!PaintingColumn.IsAnyMoving)
            CheckWinCondition();
    }

    // ── Solved-state helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Restores the full visual state of a solved puzzle from save data:
    /// powers the correct spotlights, applies lens colors, reveals symbols,
    /// and locks all player interactions.
    /// </summary>
    private void RestoreSolvedState()
    {
        // 1. Restore switch states → EvaluateAndApply lights the correct spotlights.
        if (_savedSwitchStates != null && _powerCircuit != null)
            _powerCircuit.RestoreSwitchStates(_savedSwitchStates);

        // 2. Restore lens colors so spotlights show the correct tint.
        if (_savedConditionLenses != null)
        {
            for (int i = 0; i < _conditions.Length && i < _savedConditionLenses.Length; i++)
            {
                if (_conditions[i].spotlight == null || _savedConditionLenses[i] < 0) continue;
                _conditions[i].spotlight.SetLens((LensColor)_savedConditionLenses[i]);
            }
        }

        // 3. Show all symbols (faded in if SymbolFader is present).
        ShowAllSymbols();

        // 4. Unlock and snap-open the reward drawer to its solved state.
        _rewardDrawer?.SnapOpen();

        // 5. Prevent any further interaction with the puzzle controls.
        LockAllInteractions();
    }

    /// <summary>Shows all condition symbols. Uses SymbolFader fade-in when available.</summary>
    private void ShowAllSymbols()
    {
        foreach (var cond in _conditions)
        {
            if (cond.symbolObject == null) continue;
            var fader = cond.symbolObject.GetComponent<SymbolFader>();
            if (fader != null) fader.Show();
            else               cond.symbolObject.SetActive(true);
        }
    }

    /// <summary>Locks all puzzle buttons and the room light switch.</summary>
    private void LockAllInteractions()
    {
        _powerCircuit?.LockAllSwitches();
        _roomLightSwitch?.SetLocked(true);

        foreach (var t in GetComponentsInChildren<PaintingColumnTrigger>())
            t.SetLocked(true);

        foreach (var b in GetComponentsInChildren<SpotlightLensButton>())
            b.SetLocked(true);
    }

    /// <summary>Collects the current lens color of each condition's spotlight as an int array.</summary>
    private int[] CollectConditionLenses()
    {
        var lenses = new int[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
            lenses[i] = _conditions[i].spotlight != null
                ? (int)_conditions[i].spotlight.CurrentLens
                : -1;
        return lenses;
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
        bool shouldShow  = heightOk && spotlightOk && colorOk && roomLightOk;

        var fader = cond.symbolObject.GetComponent<SymbolFader>();
        if (fader != null)
        {
            if (shouldShow) fader.Show();
            else            fader.Hide();
        }
        else
        {
            cond.symbolObject.SetActive(shouldShow);
        }
    }

    private void HideAllSymbols()
    {
        foreach (var cond in _conditions)
        {
            if (cond.symbolObject == null) continue;
            var fader = cond.symbolObject.GetComponent<SymbolFader>();
            if (fader != null) fader.HideImmediate();
            else               cond.symbolObject.SetActive(false);
        }
    }

    // ── Win Condition ──────────────────────────────────────────────────────────

    private void CheckWinCondition()
    {
        if (_isSolved) return;

        foreach (var cond in _conditions)
        {
            if (cond.symbolObject == null) return;

            var fader   = cond.symbolObject.GetComponent<SymbolFader>();
            bool visible = fader != null ? fader.IsTargetVisible : cond.symbolObject.activeSelf;
            if (!visible) return;
        }

        OnPuzzleSolved();
    }

    private void OnPuzzleSolved()
    {
        _isSolved = true;
        LockAllInteractions();
        _rewardDrawer?.AutoOpen();
        if (_solvedClip != null)
            AudioManager.Instance?.PlaySFX(_solvedClip, _solvedVolume);
        Debug.Log("[LoopPuzzleController] Puzzle solved — reward drawer opened.");
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Randomizes all column starting heights while preserving the puzzle's movement invariant
    /// (h0 - h1 + h2 - h3) mod 3, which is required for the puzzle to always be solvable.
    /// Falls back to per-column randomization if any column was loaded from a save —
    /// saved states are always reachable, so the invariant is already correct for that session.
    /// </summary>
    private void RandomizeColumns()
    {
        if (_conditions.Length == 0) return;

        bool anyLoaded = false;
        foreach (var cond in _conditions)
        {
            if (cond.column != null && cond.column.WasLoaded)
            {
                anyLoaded = true;
                break;
            }
        }

        if (anyLoaded || _conditions.Length != 4)
        {
            foreach (var cond in _conditions)
                cond.column?.RandomizeStartingHeight(cond.requiredHeight);
            return;
        }

        // Invariant of the solution: (sol0 - sol1 + sol2 - sol3) mod 3.
        // All reachable states share this value — the starting state must too.
        int solInvariant = (
              (int)_conditions[0].requiredHeight
            - (int)_conditions[1].requiredHeight
            + (int)_conditions[2].requiredHeight
            - (int)_conditions[3].requiredHeight + 9) % 3;

        var heights = new PaintingHeight[4];

        // Out of the 8 possible (h0,h1,h2) combos, exactly 2 produce h3 == sol3 (unsolvable).
        // The loop converges in ~1.3 attempts on average.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            for (int i = 0; i < 3; i++)
            {
                int offset = UnityEngine.Random.Range(1, 3);
                heights[i] = (PaintingHeight)(((int)_conditions[i].requiredHeight + offset) % 3);
            }

            // Derive h3 to preserve the invariant: h0 - h1 + h2 - h3 ≡ solInvariant
            // => h3 = h0 - h1 + h2 - solInvariant (mod 3)
            int h3 = ((int)heights[0] - (int)heights[1] + (int)heights[2] - solInvariant + 9) % 3;
            heights[3] = (PaintingHeight)h3;

            if (heights[3] != _conditions[3].requiredHeight)
                break;
        }

        for (int i = 0; i < 4; i++)
            _conditions[i].column?.SetInitialHeight(heights[i]);
    }

    /// <summary>Resets the puzzle solved state and re-subscribes to all events. Call from Inspector context menu in Play Mode.</summary>
    [ContextMenu("Reset Puzzle")]
    private void ResetPuzzle()
    {
        _isSolved = false;
        SubscribeToEvents();

        if (LightingSystem.Instance != null)
            _roomLightOff = !LightingSystem.Instance.GetZoneSwitchState(_roomLightZoneId);

        RefreshAllSymbols();
        SaveManager.Instance?.Save();
        Debug.Log("[LoopPuzzleController] Puzzle reset.");
    }

    /// <summary>
    /// Resets all painting columns to a random non-solution height and resets all S1–S5
    /// switch states to off. Called automatically when the player turns off the master S6.
    /// </summary>
    private void ResetPuzzleState()
    {
        // Reset S1–S5 to off without triggering cascade or power events.
        _powerCircuit?.ResetSwitchesToOff();

        // Reset each column back to its starting position for this session.
        foreach (var cond in _conditions)
            cond.column?.ResetToInitialState();

        HideAllSymbols();
    }
}
