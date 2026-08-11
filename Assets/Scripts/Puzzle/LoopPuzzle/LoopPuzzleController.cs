using System;
using System.Collections;
using System.Reflection;
using Unity.Cinemachine;
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
public class LoopPuzzleController : MonoBehaviour, ISaveable, IPowerConsumer
{
    [Header("Save")]
    [SerializeField] private string _saveId = "loop_puzzle_controller";

    [Header("References")]
    [SerializeField] private LoopPuzzlePowerCircuit    _powerCircuit;
    [SerializeField] private DrawerDrag                _rewardDrawer;
    [SerializeField] private PaintingRoomLightSwitch   _roomLightSwitch;

    [Header("Power — General Electricity")]
    [Tooltip("TV camera controller. Disabled (blackout) when general power is off.")]
    [SerializeField] private PeepholeTVCamera _tvCamera;

    [Tooltip("TV channel switch button. Disabled when general power is off.")]
    [SerializeField] private TVChannelButton _tvChannelButton;

    [Tooltip("Spotlights parent GameObject. Deactivated when general power is off.")]
    [SerializeField] private GameObject _spotlightsParent;

    [Tooltip("Column buttons (Q1–Q4). Disabled when general power is off.")]
    [SerializeField] private PaintingColumnTrigger[] _columnButtons;

    [Tooltip("Power switch buttons (S1–S6). Disabled when general power is off.")]
    [SerializeField] private LoopPuzzleButton[] _powerButtons;

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

    [Header("Solved Cinematic")]
    [Tooltip("CinemachineCamera that frames the solved painting room. Must start inactive in the hierarchy.")]
    [SerializeField] private CinemachineCamera _solvedCamera;

    [Tooltip("Duration of the screen fade to/from black.")]
    [SerializeField, Min(0f)] private float _fadeDuration = 1f;

    [Tooltip("How long the solved camera stays active before fading back to the player (seconds).")]
    [SerializeField, Min(0f)] private float _solvedCameraDuration = 3f;

    [Tooltip("Duration of the reward drawer opening animation during the cinematic (seconds).")]
    [SerializeField, Min(0.1f)] private float _drawerOpenDuration = 2f;

    private const int CinematicCameraPriority = 3000;

    private bool _isSolved;
    private bool _roomLightOff;

    private CinemachineBrain _brain;
    private float _originalBlendTime;

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

        if (_solvedCamera != null)
            _solvedCamera.gameObject.SetActive(false);

        _brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();

        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;
    }

    private void Start()
    {
        // _roomLightOff must be initialised before both solved and unsolved paths.
        if (LightingSystem.Instance != null)
            _roomLightOff = !LightingSystem.Instance.GetZoneSwitchState(_roomLightZoneId);

        // Register as power consumer — receives current power state immediately.
        LightingSystem.Instance?.RegisterConsumer(this);

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
        LightingSystem.Instance?.UnregisterConsumer(this);

        // Emergency cleanup — if the cinematic is interrupted, restore everything.
        if (_solvedCamera != null)
        {
            _solvedCamera.Priority = 0;
            _solvedCamera.gameObject.SetActive(false);
        }

        SetBlendDuration(_originalBlendTime);
        InputManager.Instance?.SetPlayerInputEnabled(true);
        InteractionUI.Instance?.SetVisible(true);

        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeOut(0f);
    }

    // ── IPowerConsumer ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by LightingSystem when general power changes.
    /// When power is off: disables all puzzle interaction — buttons, TV, spotlights,
    /// column triggers, and the power circuit itself. Everything stays visible but
    /// non-interactive. When power is restored: everything re-enables.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        if (_isSolved) return;

        // TV — PeepholeTVCamera.OnDisable blacks out the screen,
        // OnEnable restores the RT material and camera.
        if (_tvCamera != null)
            _tvCamera.enabled = isPowered;

        // TV channel button — CanInteract() checks enabled.
        if (_tvChannelButton != null)
            _tvChannelButton.enabled = isPowered;

        // TV glitch effect.
        var glitch = _tvCamera != null ? _tvCamera.GetComponent<TVGlitchEffect>() : null;
        if (glitch != null)
            glitch.enabled = isPowered;

        // Spotlights — fully deactivate parent (all 4 lights).
        if (_spotlightsParent != null)
            _spotlightsParent.SetActive(isPowered);

        // Column buttons (Q1–Q4) — CanInteract() checks enabled on PaintingColumnTrigger.
        if (_columnButtons != null)
            foreach (var btn in _columnButtons)
                if (btn != null) btn.enabled = isPowered;

        // Power switch buttons (S1–S6) — CanInteract() checks enabled.
        if (_powerButtons != null)
            foreach (var btn in _powerButtons)
                if (btn != null) btn.enabled = isPowered;

        // Power circuit — stops evaluating spotlight power.
        if (_powerCircuit != null)
            _powerCircuit.enabled = isPowered;
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
        Debug.Log("[LoopPuzzleController] Puzzle solved — starting cinematic sequence.");
        StartCoroutine(SolvedCinematicRoutine());
    }

    /// <summary>
    /// Cinematic sequence played when the puzzle is solved:
    /// fade to black → instant camera switch → fade in → open drawer →
    /// wait → fade to black → instant camera switch back → fade in.
    /// Reuses ScreenFader for the UI darkening. Cinemachine blends are
    /// temporarily set to 0 so camera switches are instant (hidden by the fade).
    /// </summary>
    private IEnumerator SolvedCinematicRoutine()
    {
        // Disable player input and hide the interaction HUD for the duration of the cinematic.
        InputManager.Instance?.SetPlayerInputEnabled(false);
        InteractionUI.Instance?.SetVisible(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // ── Phase 1: Fade to black ──────────────────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 2: Instant switch to solved camera (screen is black) ───────────
        SetBlendDuration(0f);

        if (_solvedCamera != null)
        {
            _solvedCamera.Priority = CinematicCameraPriority;
            _solvedCamera.gameObject.SetActive(true);
        }

        // Wait one frame so the brain processes the instant cut.
        yield return null;

        // ── Phase 3: Fade from black — player sees the solved room ───────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 4: Play solved sound and open the reward drawer ────────────────
        if (_solvedClip != null)
            AudioManager.Instance?.PlaySFX(_solvedClip, _solvedVolume);

        _rewardDrawer?.AutoOpen(_drawerOpenDuration);

        // ── Phase 5: Hold the shot while the drawer opens ────────────────────────
        yield return new WaitForSeconds(_solvedCameraDuration);

        // ── Phase 6: Fade to black again ─────────────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 7: Instant switch back to the player camera (screen is black) ──
        if (_solvedCamera != null)
        {
            _solvedCamera.Priority = 0;
            _solvedCamera.gameObject.SetActive(false);
        }

        // Wait one frame so the brain processes the instant cut.
        yield return null;

        // Restore the original Cinemachine blend for normal gameplay.
        SetBlendDuration(_originalBlendTime);

        // ── Phase 8: Fade from black — player regains control ────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        InputManager.Instance?.SetPlayerInputEnabled(true);
        InteractionUI.Instance?.SetVisible(true);

        SaveManager.Instance?.Save();
        Debug.Log("[LoopPuzzleController] Cinematic complete — reward drawer opened, control returned.");
    }

    /// <summary>Sets the DefaultBlend duration on the CinemachineBrain (0 = instant cut).</summary>
    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend = _brain.DefaultBlend;
        blend.Time = duration;
        _brain.DefaultBlend = blend;
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

    // ── Cheats ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instantly solves the puzzle: sets all switches, lenses, and column heights
    /// to the correct solution, reveals all symbols, and fires the cinematic.
    /// Intended for the Tools/PuzzlesCheats menu — only works in Play Mode.
    /// </summary>
    public void AutoSolve()
    {
        if (_isSolved) return;
        if (!Application.isPlaying) return;

        // 1. Power on the master switch (S6).
        var powerButtonField = _powerCircuit?.GetType()
            .GetField("_switches", BindingFlags.NonPublic | BindingFlags.Instance);
        if (powerButtonField?.GetValue(_powerCircuit) is not LoopPuzzleButton[] switches)
        {
            Debug.LogWarning("[LoopPuzzleCheats] Could not access _switches on LoopPuzzlePowerCircuit.");
            return;
        }

        // Turn on S6 (last element) silently.
        if (switches.Length > 0 && switches[^1] != null)
            switches[^1].SetStateSilent(true);

        // Find a switch combination that powers all spotlights.
        bool[] solution = FindSolvingSwitchStates();
        for (int i = 0; i < solution.Length && i < switches.Length - 1; i++)
            switches[i]?.SetStateSilent(solution[i]);

        // Apply power state.
        _powerCircuit?.EvaluateAndApply();

        // 2. Set lens colors on each condition's spotlight.
        foreach (var cond in _conditions)
        {
            if (cond.spotlight != null && cond.requiredColor != LensColor.None)
                cond.spotlight.SetLens(cond.requiredColor);
        }

        // 3. Set each column to the required height.
        foreach (var cond in _conditions)
        {
            if (cond.column != null)
                cond.column.SetInitialHeight(cond.requiredHeight);
        }

        // 4. Show all symbols immediately.
        ShowAllSymbols();

        // 5. Lock all interactions and fire the cinematic.
        OnPuzzleSolved();

        Debug.Log("<color=green>[LoopPuzzleCheats] Paint Puzzle has been force-solved!</color>");
    }

    /// <summary>
    /// Brute-forces all 2^5 combinations of S1–S5 to find one that powers all spotlights.
    /// </summary>
    private bool[] FindSolvingSwitchStates()
    {
        int nonMasterCount = _powerCircuit?.SwitchCount - 1 ?? 5;

        for (int mask = 0; mask < (1 << nonMasterCount); mask++)
        {
            var states = new bool[nonMasterCount];
            for (int i = 0; i < nonMasterCount; i++)
                states[i] = (mask & (1 << i)) != 0;

            if (_powerCircuit != null && _powerCircuit.CheckAllPoweredWith(states))
                return states;
        }

        Debug.LogWarning("[LoopPuzzleCheats] No valid switch combination found — using all ON.");
        var fallback = new bool[nonMasterCount];
        for (int i = 0; i < nonMasterCount; i++) fallback[i] = true;
        return fallback;
    }
}
