using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Escape.Core;

/// <summary>
/// Pressure puzzle controller.
///
/// The dial goes from 0° (start) to 360°. The arrow starts at 0° with all
/// levers OFF (zero pressure). Each lever adds a hidden positive magnitude
/// to the total pressure when switched ON. The player must find the right
/// combination to land the arrow at exactly 180° — the target.
///
/// If the arrow reaches 300° (the red zone) a reset fires: all levers snap
/// OFF, the arrow sweeps back down through 180° to 0° — it does NOT take
/// the short path (300° → 360° → 0°) but the long descending path
/// (300° → 250° → … → 180° → … → 0°), passing through the target on the
/// way home. Levers stay locked during the reset cooldown.
///
/// Each session a random solution combination is chosen. The pressure-to-
/// angle scale is derived from the solution so that the solution total maps
/// to exactly 180°. The solution total is constrained to keep the danger
/// zone (300°) reachable and the maximum angle below 360° (no visual wrap).
/// </summary>
public class PressurePuzzle : MonoBehaviour, ISaveable
{
    // ── References ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Transform of the arrow inside the dial (child of 'screen').")]
    [SerializeField] private Transform _arrow;

    [Header("Activation Conditions")]
    [Tooltip("Door the player enters through (opened by CardLock). " +
             "The puzzle activates only when this door is closed and locked.")]
    [SerializeField] private DoorInteraction _entryDoor;

    [Tooltip("Room trigger inside the laboratory. The door can only lock " +
             "if the player is inside this trigger volume.")]
    [SerializeField] private RoomTrigger _roomTrigger;

    [Tooltip("Whether steam supply is active (valve in the basement). " +
             "PLACEHOLDER — will be controlled by a separate valve puzzle. " +
             "Set to true for testing.")]
    [SerializeField] private bool _steamSupplied = true;

    [Header("Save")]
    [Tooltip("Unique identifier for the save system. Must be unique across the entire game.")]
    [SerializeField] private string _saveId = "pressure_puzzle";

    // ── Dial Settings ──────────────────────────────────────────────────────────

    [Header("Dial Settings")]
    [Tooltip("Base X rotation added to all arrow positions. Set so the arrow " +
             "visually points to 0° on the dial face when the puzzle starts.")]
    [SerializeField] private float _arrowBaseAngle = 0f;

    [Tooltip("How fast the arrow sweeps toward the target angle. Lower = more inertial.")]
    [SerializeField] private float _pressureSmoothSpeed = 3f;

    [Tooltip("Allowed deviation from the solve angle in degrees.")]
    [SerializeField] private float _solveAngleTolerance = 10f;

    // ── Target & Danger ────────────────────────────────────────────────────────

    [Header("Target & Danger")]
    [Tooltip("Angle the player must reach to solve the puzzle.")]
    [SerializeField] private float _solveAngle = 180f;

    [Tooltip("Angle at which the red zone begins. Reaching this triggers a reset.")]
    [SerializeField] private float _dangerAngle = 300f;

    [Tooltip("Steam starts ramping up at this fraction of the danger angle. " +
             "E.g. 0.8 → steam begins at 240° and reaches max at 300°.")]
    [SerializeField] [Range(0f, 1f)] private float _warningFraction = 0.8f;

    [Tooltip("Maximum total lever actions (toggle ON or OFF) allowed per attempt. " +
             "Every toggle counts as one action. When the limit is exceeded, " +
             "the system triggers a reset and the counter restarts. " +
             "Forces the player to think before each action.")]
    [SerializeField] [Range(1, 30)] private int _maxActions = 5;

    // ── Solution ───────────────────────────────────────────────────────────────

    [Header("Solution")]
    [Tooltip("Minimum number of levers that must be ON in the randomly chosen solution. " +
             "Enforces a one-sided minimum — does NOT require the same for OFF levers.")]
    [SerializeField] [Range(1, 5)] private int _minLeversOnInSolution = 4;

    [Tooltip("Minimum lever flips from the all-OFF start to ANY valid solution. " +
             "Prevents trivially easy shortcuts through alternative combinations.")]
    [SerializeField] [Range(1, 10)] private int _minFlipsFromSolution = 4;

    [Tooltip("Minimum solution total as a fraction of max total. Prevents the " +
             "maximum angle from exceeding 360° (visual wrap). " +
             "0.5 = solution must be at least half the max total.")]
    [SerializeField] [Range(0.1f, 0.9f)] private float _minSolutionFraction = 0.5f;

    [Tooltip("Maximum solution total as a fraction of max total. Ensures the " +
             "danger zone is reachable. 0.65 = solution must be below 65% of max.")]
    [SerializeField] [Range(0.1f, 0.9f)] private float _maxSolutionFraction = 0.65f;

    // ── Lever Value Generation ─────────────────────────────────────────────────

    [Header("Lever Value Generation")]
    [Tooltip("How many levers get SMALL magnitudes. These provide fine-tuning — " +
             "the player can nudge the arrow without overshooting. The rest get LARGE magnitudes.")]
    [SerializeField] [Range(1, 5)] private int _smallLeverCount = 2;

    [Tooltip("Base magnitude for the smallest small lever.")]
    [SerializeField] [Min(1f)] private float _smallLeverBase = 5f;

    [Tooltip("Spacing between consecutive small lever magnitudes. " +
             "With 2 small levers, base = 5 and step = 5 → small magnitudes: 5, 10.")]
    [SerializeField] [Min(1f)] private float _smallLeverStep = 5f;

    [Tooltip("Base magnitude for the smallest large lever. Should be much bigger than small levers " +
             "so that toggling a large lever causes a big jump on the dial.")]
    [SerializeField] [Min(1f)] private float _largeLeverBase = 25f;

    [Tooltip("Spacing between consecutive large lever magnitudes. " +
             "With 4 large levers, base = 25 and step = 10 → large magnitudes: 25, 35, 45, 55.")]
    [SerializeField] [Min(1f)] private float _largeLeverStep = 10f;

    // ── Reset ──────────────────────────────────────────────────────────────────

    [Header("Reset")]
    [Tooltip("Minimum seconds levers stay blocked after a reset.")]
    [SerializeField] private float _resetCooldown = 3f;

    [Tooltip("How slowly the arrow returns to 0° during reset. Lower = slower.")]
    [SerializeField] private float _resetPressureSpeed = 1f;

    [Tooltip("Sound played when a reset is triggered.")]
    [SerializeField] private AudioClip _resetSound;
    [SerializeField] [Range(0f, 1f)] private float _resetSoundVolume = 0.8f;

    // ── Steam VFX ──────────────────────────────────────────────────────────────

    [Header("Steam VFX")]
    [Tooltip("Steam particle systems that ramp up as pressure approaches the danger angle.")]
    [SerializeField] private ParticleSystem[] _steamEmitters;
    [Tooltip("Maximum emission rate when pressure is at the danger angle.")]
    [SerializeField] private float _maxSteamEmission = 50f;
    [Tooltip("Emission rate forced during a reset — much higher than warning.")]
    [SerializeField] private float _resetSteamEmission = 200f;
    [Tooltip("Instant particle burst on each emitter when a reset triggers.")]
    [SerializeField] private int _resetBurstCount = 100;

    // ── Steam Audio ────────────────────────────────────────────────────────────

    [Header("Audio Sources")]
    [Tooltip("AudioSource for one-shot sounds (reset, steam fade). " +
             "Assign a child GameObject with an AudioSource you positioned in the scene. " +
             "If left empty, one is created on this GameObject at runtime.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("AudioSource for the looping steam ambient sound. " +
             "Assign a child GameObject with an AudioSource you positioned in the scene. " +
             "If left empty, one is created on this GameObject at runtime.")]
    [SerializeField] private AudioSource _steamAudioSource;

    [Header("Steam Audio Clips")]
    [Tooltip("Looping ambient steam sound — volume scales with emission intensity.")]
    [SerializeField] private AudioClip _steamLoopClip;
    [Tooltip("Maximum volume of the steam loop at full emission.")]
    [SerializeField] [Range(0f, 1f)] private float _steamLoopMaxVolume = 0.6f;
    [Tooltip("One-shot sound played when steam dissipates after a reset ends.")]
    [SerializeField] private AudioClip _steamFadeClip;
    [SerializeField] [Range(0f, 1f)] private float _steamFadeVolume = 0.5f;

    // ── Events & Reward ────────────────────────────────────────────────────────

    [Header("Events")]
    [Tooltip("Fired exactly once when the player reaches the target angle.")]
    [SerializeField] private UnityEvent _onSolved;

    [Header("Reward")]
    [Tooltip("GameObjects to activate when the puzzle is solved (e.g. lights, doors).")]
    [SerializeField] private GameObject[] _rewardObjects;

    // ── Runtime state ─────────────────────────────────────────────────────────

    /// <summary>True once the puzzle has been solved.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>True while levers are locked during a pressure reset.</summary>
    public bool IsResetting { get; private set; }

    /// <summary>True when both activation conditions are met (door locked + steam supplied).
    /// Levers are only interactable while this is true.</summary>
    public bool IsActivated { get; private set; }

    /// <summary>Remaining actions before the system resets. Read-only for UI.</summary>
    public int RemainingActions => Mathf.Max(0, _maxActions - _actionCount);

    private readonly List<PressureLever> _levers = new();
    private readonly List<int> _validSolutionMasks = new();
    private float _maxTotal;
    private int   _solutionMask;
    private float _solutionTotal;
    private float _currentArrowAngle;
    private float _targetArrowAngle;
    private float _arrowVelocity;
    private float _arrowBaseEulerY;
    private float _arrowBaseEulerZ;
    private bool  _loadedIsSolved;
    private bool[] _loadedLeverStates;

    private int   _actionCount;     // total toggles since last reset / start
    private float _resetTimer;
    private bool  _wasInDanger;
    private bool  _solveLocked;
    private bool _fadeClipPlayed;
    private bool _wasDoorOpen;      // tracks door state for close-detection
    private float _activationLogTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);

        // Use assigned AudioSources or create fallbacks on this GameObject.
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake  = false;
            _audioSource.spatialBlend  = 1f;
            _audioSource.loop          = false;
        }

        if (_steamAudioSource == null)
        {
            _steamAudioSource = gameObject.AddComponent<AudioSource>();
            _steamAudioSource.playOnAwake  = false;
            _steamAudioSource.spatialBlend  = 1f;
            _steamAudioSource.loop          = true;
            _steamAudioSource.volume        = 0f;
        }

        if (_steamLoopClip != null)
            _steamAudioSource.clip = _steamLoopClip;
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Start()
    {
        _levers.Clear();
        GetComponentsInChildren(includeInactive: false, _levers);

        if (_arrow != null)
        {
            Vector3 baseEuler = _arrow.localEulerAngles;
            _arrowBaseEulerY  = baseEuler.y;
            _arrowBaseEulerZ  = baseEuler.z;
        }

        if (_loadedIsSolved)
        {
            RestoreSolvedState();
            return;
        }

        GenerateAndAssignLeverValues();

        _maxTotal = 0f;
        foreach (var lever in _levers)
            _maxTotal += Mathf.Max(lever.OffValue, lever.OnValue);

        if (Mathf.Approximately(_maxTotal, 0f))
            _maxTotal = 1f;

        PickRandomSolution();

        // All levers start OFF — arrow at 0° (the starting position).
        foreach (var lever in _levers)
            lever.SetStateQuiet(false);

        _targetArrowAngle  = 0f;
        _currentArrowAngle = 0f;
        ApplyArrow(0f);

        _wasInDanger  = false;
        _solveLocked  = false;
        _actionCount  = 0;

        // Track door state for activation detection.
        _wasDoorOpen = _entryDoor != null && !_entryDoor.IsFullyClosed;

        foreach (var lever in _levers)
            lever.SnapVisual();

        float maxAngle = _maxTotal * _solveAngle / _solutionTotal;
        Debug.Log($"[PressurePuzzle] {_levers.Count} levers. MaxTotal={_maxTotal}. " +
                  $"Solution total={_solutionTotal} → {_solveAngle}°. " +
                  $"Danger at {_dangerAngle}°. Max angle={maxAngle:F1}°. " +
                  $"Valid solutions: {_validSolutionMasks.Count}.");

        Debug.Log($"[PressurePuzzle] Activation refs — " +
                  $"_entryDoor: {(_entryDoor != null ? _entryDoor.gameObject.name : "NULL")}, " +
                  $"_roomTrigger: {(_roomTrigger != null ? _roomTrigger.gameObject.name : "NULL")}, " +
                  $"_steamSupplied: {_steamSupplied}, " +
                  $"door fully closed: {(_entryDoor != null ? _entryDoor.IsFullyClosed.ToString() : "N/A")}");
    }

    // ── Lever value generation ────────────────────────────────────────────────

    /// <summary>
    /// Generates a two-tier set of magnitudes: a few SMALL levers for fine-tuning
    /// and the rest LARGE levers for big pressure jumps. Then Fisher-Yates shuffles
    /// the assignment order so the player can't predict which lever is which.
    /// Each lever gets onValue = +magnitude, offValue = 0.
    /// </summary>
    private void GenerateAndAssignLeverValues()
    {
        int n = _levers.Count;
        if (n == 0) return;

        int smallCount = Mathf.Clamp(_smallLeverCount, 1, n - 1);
        int largeCount = n - smallCount;

        float[] magnitudes = new float[n];

        // Small levers: _smallLeverBase, _smallLeverBase + step, ...
        for (int i = 0; i < smallCount; i++)
            magnitudes[i] = _smallLeverBase + _smallLeverStep * i;

        // Large levers: _largeLeverBase, _largeLeverBase + step, ...
        for (int i = 0; i < largeCount; i++)
            magnitudes[smallCount + i] = _largeLeverBase + _largeLeverStep * i;

        // Fisher-Yates shuffle — assign magnitudes in random order to levers.
        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (magnitudes[i], magnitudes[j]) = (magnitudes[j], magnitudes[i]);
        }

        for (int i = 0; i < n; i++)
            _levers[i].AssignValues(0f, magnitudes[i]);

        Debug.Log($"[PressurePuzzle] Lever magnitudes: [{string.Join(", ", magnitudes)}] " +
                  $"({smallCount} small, {largeCount} large)");
    }

    // ── Solution picking ──────────────────────────────────────────────────────

    /// <summary>
    /// Picks a random lever combination as the session's solution.
    /// The total must fall within [_minSolutionFraction, _maxSolutionFraction)
    /// of _maxTotal so that:
    ///   - The danger angle is reachable (solution not too large).
    ///   - The maximum angle stays below 360° (no visual wrap).
    /// Also ensures at least _minFlipsFromSolution flips from the all-OFF
    /// start to ANY valid solution.
    /// </summary>
    private void PickRandomSolution()
    {
        int n      = _levers.Count;
        int minOn  = _minLeversOnInSolution;
        int maxOn  = n - 1; // one-sided: allow up to n-1 ON (prevent all-ON trivial)

        float minSol = _maxTotal * _minSolutionFraction;
        float maxSol = _maxTotal * _maxSolutionFraction;

        if (maxOn < minOn)
        {
            _solutionTotal = _maxTotal * 0.55f;
            FindAllValidSolutions();
            Debug.LogWarning("[PressurePuzzle] Not enough levers for minLeversOnInSolution. Using fallback.");
            return;
        }

        const int maxAttempts = 500;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int mask    = UnityEngine.Random.Range(0, 1 << n);
            int onCount = CountBits(mask);

            if (onCount < minOn || onCount > maxOn) continue;

            float total = 0f;
            for (int i = 0; i < n; i++)
                total += ((mask & (1 << i)) != 0) ? _levers[i].OnValue : _levers[i].OffValue;

            if (total < minSol || total >= maxSol) continue;

            _solutionMask  = mask;
            _solutionTotal = total;

            FindAllValidSolutions();

            if (MinFlipsToAnySolution(0) >= _minFlipsFromSolution)
            {
                Debug.Log($"[PressurePuzzle] Solution: {onCount}/{n} ON, " +
                          $"total={total}, mask={Convert.ToString(mask, 2).PadLeft(n, '0')}");
                return;
            }
        }

        _solutionTotal = _maxTotal * 0.55f;
        FindAllValidSolutions();
        Debug.LogWarning("[PressurePuzzle] Could not find ideal solution within constraints. " +
                         "Using fallback total.");
    }

    /// <summary>
    /// Brute-forces all 2^N lever combinations and records every mask whose
    /// arrow angle falls within _solveAngleTolerance of _solveAngle AND has at
    /// least _minLeversOnInSolution levers ON (one-sided, up to n-1 ON).
    /// The ON-count filter prevents short shortcuts when most levers have
    /// large magnitudes — a pair of big levers could hit the target angle
    /// but would trivialise the puzzle.
    /// </summary>
    private void FindAllValidSolutions()
    {
        _validSolutionMasks.Clear();
        int n     = _levers.Count;
        int minOn = _minLeversOnInSolution;
        int maxOn = n - 1;

        for (int mask = 0; mask < (1 << n); mask++)
        {
            int onCount = CountBits(mask);
            if (onCount < minOn || onCount > maxOn) continue;

            float total = 0f;
            for (int i = 0; i < n; i++)
                total += ((mask & (1 << i)) != 0) ? _levers[i].OnValue : _levers[i].OffValue;

            float angle = PressureToAngle(total);
            if (Mathf.Abs(angle - _solveAngle) <= _solveAngleTolerance)
                _validSolutionMasks.Add(mask);
        }

        Debug.Log($"[PressurePuzzle] {_validSolutionMasks.Count} valid solution(s) " +
                  $"within ±{_solveAngleTolerance}° of {_solveAngle}° " +
                  $"(ON count {minOn}–{maxOn}).");
    }

    /// <summary>
    /// Returns the minimum number of lever flips needed to reach ANY valid
    /// solution from the given starting mask.
    /// </summary>
    private int MinFlipsToAnySolution(int startMask)
    {
        int min = int.MaxValue;
        foreach (int sol in _validSolutionMasks)
            min = Mathf.Min(min, CountBits(startMask ^ sol));
        return min == int.MaxValue ? 0 : min;
    }

    // ── Update loop ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (IsSolved) return;

        // ── Activation logic ───────────────────────────────────────────────
        // Check if the entry door just transitioned from open to closed.
        // If the player is inside the room and steam is supplied, lock the door
        // and activate the puzzle. This runs before the reset/sim logic so
        // levers become interactive the moment conditions are met.
        CheckActivation();

        if (!IsActivated) return;

        if (IsResetting)
        {
            UpdateReset();
            return;
        }

        _currentArrowAngle = Mathf.SmoothDamp(
            _currentArrowAngle, _targetArrowAngle,
            ref _arrowVelocity, 1f / _pressureSmoothSpeed);

        ApplyArrow(_currentArrowAngle);

        CheckDanger();
        UpdateSteam();
        CheckSolve();
    }

    /// <summary>
    /// Checks activation conditions every frame:
    /// 1. Entry door must transition from not-fully-closed to fully closed.
    /// 2. Player must be inside the room trigger volume.
    /// 3. Steam supply must be active (placeholder — always true for now).
    /// When all conditions are met, the door locks and the puzzle activates.
    /// </summary>
    /// <summary>
    /// Checks activation conditions every frame:
    /// 1. Entry door must transition from not-fully-closed to fully closed.
    /// 2. If _roomTrigger is assigned, player must be inside it (optional safety).
    /// 3. Steam supply must be active (placeholder — always true for now).
    /// When all conditions are met, the door locks and the puzzle activates.
    /// </summary>
    private void CheckActivation()
    {
        if (IsActivated || IsSolved) return;
        if (_entryDoor == null) return;

        // Log diagnostic info once per second.
        _activationLogTimer += Time.deltaTime;
        if (_activationLogTimer >= 1f)
        {
            _activationLogTimer = 0f;
            bool doorClosed = _entryDoor.IsFullyClosed;
            bool playerInside = _roomTrigger != null && _roomTrigger.IsPlayerInside;
            Debug.Log($"[PressurePuzzle] CheckActivation — " +
                      $"doorClosed: {doorClosed}, " +
                      $"wasDoorOpen: {_wasDoorOpen}, " +
                      $"playerInside: {playerInside}, " +
                      $"steamSupplied: {_steamSupplied}");
        }

        bool doorClosedNow = _entryDoor.IsFullyClosed;

        // Detect the moment the door transitions from open to fully closed.
        if (_wasDoorOpen && doorClosedNow)
        {
            // Room trigger is optional — if assigned, player must be inside.
            // If not assigned, closing the door from inside is sufficient.
            bool playerInside = _roomTrigger == null || _roomTrigger.IsPlayerInside;

            if (playerInside && _steamSupplied)
            {
                _entryDoor.Lock();
                IsActivated = true;
                Debug.Log("[PressurePuzzle] Activated — door locked, player inside, steam supplied.");
            }
            else
            {
                Debug.Log($"[PressurePuzzle] Door closed but conditions not met — " +
                          $"playerInside: {playerInside}, steamSupplied: {_steamSupplied}");
            }
        }

        _wasDoorOpen = !doorClosedNow;
    }

    // ── Danger zone ───────────────────────────────────────────────────────────

    /// <summary>
    /// Detects edge-crossing into the danger zone (≥ _dangerAngle).
    /// Reset fires only on the transition from safe to dangerous.
    /// </summary>
    private void CheckDanger()
    {
        bool inDanger = _currentArrowAngle >= _dangerAngle;

        if (inDanger && !_wasInDanger) TriggerReset();

        _wasInDanger = inDanger;
    }

    // ── Steam VFX ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ramps steam emission based on proximity to the danger angle.
    /// Steam starts at _warningFraction × _dangerAngle and reaches maximum
    /// at _dangerAngle. During a reset, steam bursts at full intensity then
    /// linearly fades to zero over the cooldown period.
    /// </summary>
    private void UpdateSteam()
    {
        float warningStart = _dangerAngle * _warningFraction;

        float dangerProgress = 0f;
        if (!_wasInDanger)
            dangerProgress = Mathf.Clamp01(
                Mathf.InverseLerp(warningStart, _dangerAngle, _currentArrowAngle));

        float rate = Mathf.Lerp(0f, _maxSteamEmission, dangerProgress);

        if (IsResetting)
        {
            float fadeProgress = Mathf.Clamp01(_resetTimer / _resetCooldown);
            rate = Mathf.Lerp(_resetSteamEmission, 0f, fadeProgress);
        }

        foreach (var ps in _steamEmitters)
        {
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }

        UpdateSteamAudio(rate);
    }

    /// <summary>
    /// Scales the looping steam AudioSource volume proportionally to the
    /// current emission rate. Uses hysteresis to prevent flicker.
    /// </summary>
    private void UpdateSteamAudio(float rate)
    {
        if (_steamAudioSource == null || _steamLoopClip == null) return;

        float volumeFraction = Mathf.Clamp01(rate / _resetSteamEmission);
        float targetVolume   = volumeFraction * _steamLoopMaxVolume;

        _steamAudioSource.volume = targetVolume;

        const float startThreshold = 0.05f;
        const float stopThreshold  = 0.005f;

        if (targetVolume > startThreshold && !_steamAudioSource.isPlaying)
            _steamAudioSource.Play();
        else if (targetVolume < stopThreshold && _steamAudioSource.isPlaying)
            _steamAudioSource.Stop();
    }

    // ── Solve detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the arrow has settled within tolerance of _solveAngle.
    /// Both current and target angles must be within tolerance — this prevents
    /// false solves when the arrow merely passes through the target.
    /// </summary>
    private void CheckSolve()
    {
        if (_solveLocked) return;

        if (Mathf.Abs(_currentArrowAngle - _solveAngle) <= _solveAngleTolerance
            && Mathf.Abs(_targetArrowAngle - _solveAngle) <= _solveAngleTolerance)
        {
            _currentArrowAngle = _solveAngle;
            _targetArrowAngle  = _solveAngle;
            _arrowVelocity     = 0f;
            ApplyArrow(_solveAngle);
            Solve();
        }
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snaps all levers to OFF and sets the arrow target to 0°. The arrow
    /// will SmoothDamp from its current position (≥ _dangerAngle) down to 0°,
    /// passing through _solveAngle (180°) on the way — the long descending
    /// path, not the short wrap-around.
    /// </summary>
    private void TriggerReset()
    {
        IsResetting = true;
        _resetTimer = 0f;
        _fadeClipPlayed = false;

        foreach (var lever in _levers)
            lever.SetStateQuiet(false);

        // All OFF → pressure 0 → angle 0°. The arrow descends through 180°.
        _targetArrowAngle = 0f;

        _wasInDanger = false;
        _solveLocked = true;
        _actionCount = 0;

        foreach (var ps in _steamEmitters)
            if (ps != null) ps.Emit(_resetBurstCount);

        if (_resetSound != null)
            _audioSource.PlayOneShot(_resetSound, _resetSoundVolume);

        Debug.Log("[PressurePuzzle] Pressure reset triggered!");
    }

    /// <summary>
    /// Drives the slow return of the arrow to 0° during a reset.
    /// Steam fades over the cooldown. When cooldown expires, levers unlock.
    /// </summary>
    private void UpdateReset()
    {
        _resetTimer += Time.deltaTime;

        _currentArrowAngle = Mathf.SmoothDamp(
            _currentArrowAngle, _targetArrowAngle,
            ref _arrowVelocity, 1f / _resetPressureSpeed);

        ApplyArrow(_currentArrowAngle);
        UpdateSteam();

        if (_resetTimer >= _resetCooldown)
        {
            IsResetting   = false;
            _arrowVelocity = 0f;
            _currentArrowAngle = _targetArrowAngle;
            ApplyArrow(_currentArrowAngle);

            _wasInDanger = _currentArrowAngle >= _dangerAngle;

            if (_steamAudioSource != null && _steamAudioSource.isPlaying)
                _steamAudioSource.Stop();

            foreach (var ps in _steamEmitters)
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.rateOverTime = 0f;
                }

            if (!_fadeClipPlayed && _steamFadeClip != null)
            {
                _audioSource.PlayOneShot(_steamFadeClip, _steamFadeVolume);
                _fadeClipPlayed = true;
            }

            Debug.Log("[PressurePuzzle] Reset complete — levers unlocked.");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PressureLever on every toggle.
    /// Each toggle (ON or OFF) counts as one action against _maxActions.
    /// When the action budget is exhausted, the system triggers a reset
    /// and the counter restarts.
    /// </summary>
    public void OnLeverChanged()
    {
        if (IsSolved || IsResetting) return;

        _actionCount++;

        // Action budget exhausted — trigger reset.
        if (_actionCount > _maxActions)
        {
            Debug.Log($"[PressurePuzzle] Action limit exceeded! {_actionCount - 1}/{_maxActions} actions used — triggering reset.");
            TriggerReset();
            return;
        }

        _solveLocked = false;
        _targetArrowAngle = PressureToAngle(GetCurrentTotal());

        int onCount = 0;
        foreach (var lever in _levers)
            if (lever.IsOn) onCount++;

        Debug.Log($"[PressurePuzzle] Action {_actionCount}/{_maxActions}. " +
                  $"{onCount} levers ON. Remaining: {RemainingActions}.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private float GetCurrentTotal()
    {
        float sum = 0f;
        foreach (var lever in _levers)
            sum += lever.CurrentValue;
        return sum;
    }

    /// <summary>
    /// Maps pressure to dial angle using a scale derived from the solution.
    /// pressure 0 → 0° (start), pressure = solutionTotal → _solveAngle (180°).
    /// </summary>
    private float PressureToAngle(float pressure)
    {
        if (_solutionTotal <= 0f) return 0f;
        return pressure * (_solveAngle / _solutionTotal);
    }

    private void ApplyArrow(float angle)
    {
        if (_arrow == null) return;
        float absoluteAngle = angle + _arrowBaseAngle;
        _arrow.localEulerAngles = new Vector3(absoluteAngle, _arrowBaseEulerY, _arrowBaseEulerZ);
    }

    /// <summary>Counts the number of set bits in an integer.</summary>
    private static int CountBits(int n)
    {
        int count = 0;
        while (n != 0) { count += n & 1; n >>= 1; }
        return count;
    }

    // ── Solve ─────────────────────────────────────────────────────────────────

    private void Solve()
    {
        IsSolved = true;

        _currentArrowAngle = _solveAngle;
        ApplyArrow(_solveAngle);

        foreach (var ps in _steamEmitters)
            if (ps != null)
            {
                var emission = ps.emission;
                emission.rateOverTime = 0f;
            }

        if (_steamAudioSource != null && _steamAudioSource.isPlaying)
            _steamAudioSource.Stop();

        foreach (var obj in _rewardObjects)
            if (obj != null) obj.SetActive(true);

        // Unlock the entry door so the player can leave.
        if (_entryDoor != null && _entryDoor.IsLocked)
        {
            _entryDoor.Unlock();
            Debug.Log("[PressurePuzzle] Entry door unlocked — player can leave.");
        }

        _onSolved.Invoke();
        SaveManager.Instance?.Save();
        Debug.Log("[PressurePuzzle] Solved!");
    }

    /// <summary>
    /// Applies the solved visual state instantly without invoking events.
    /// Called on load when the save data shows the puzzle was already solved.
    /// </summary>
    private void RestoreSolvedState()
    {
        if (_loadedLeverStates != null && _loadedLeverStates.Length == _levers.Count)
        {
            for (int i = 0; i < _levers.Count; i++)
                _levers[i].SetStateQuiet(_loadedLeverStates[i]);
        }

        IsSolved           = true;
        _currentArrowAngle = _solveAngle;
        ApplyArrow(_solveAngle);

        foreach (var ps in _steamEmitters)
            if (ps != null)
            {
                var emission = ps.emission;
                emission.rateOverTime = 0f;
            }

        if (_steamAudioSource != null && _steamAudioSource.isPlaying)
            _steamAudioSource.Stop();

        foreach (var obj in _rewardObjects)
            if (obj != null) obj.SetActive(true);

        // Door should already be unlocked if puzzle was solved.
        if (_entryDoor != null && _entryDoor.IsLocked)
            _entryDoor.Unlock();

        Debug.Log("[PressurePuzzle] Restored solved state from save.");
    }

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        var states = new bool[_levers.Count];
        for (int i = 0; i < _levers.Count; i++)
            states[i] = _levers[i].IsOn;

        return JsonUtility.ToJson(new SaveData { isSolved = IsSolved, leverStates = states });
    }

    public void LoadSaveData(string json)
    {
        var data           = JsonUtility.FromJson<SaveData>(json);
        _loadedIsSolved    = data.isSolved;
        _loadedLeverStates = data.leverStates;
    }

    [Serializable]
    private struct SaveData
    {
        public bool   isSolved;
        public bool[] leverStates;
    }

    // ── Editor validation ─────────────────────────────────────────────────────
#if UNITY_EDITOR
    public struct EditorValidation
    {
        public int    LeverCount;
        public int    MinLeversOn;
        public int    MaxActions;
        public bool   CanPickSolution;
        public int    ValidCombinationCount;
        public float[] Magnitudes;
        public float  MaxTotal;
        public float  MinSolutionTotal;
        public float  MaxSolutionTotal;
        public float  SolveAngle;
        public float  DangerAngle;
    }

    public EditorValidation GetEditorValidation()
    {
        var levers = GetComponentsInChildren<PressureLever>(includeInactive: false);
        int n      = levers.Length;
        int minOn  = _minLeversOnInSolution;
        int maxOn  = n - 1;

        float[] magnitudes = new float[n];
        float   maxTotal   = 0f;

        int smallCount = Mathf.Clamp(_smallLeverCount, 1, n - 1);
        int largeCount = n - smallCount;

        for (int i = 0; i < smallCount; i++)
            magnitudes[i] = _smallLeverBase + _smallLeverStep * i;
        for (int i = 0; i < largeCount; i++)
            magnitudes[smallCount + i] = _largeLeverBase + _largeLeverStep * i;

        for (int i = 0; i < n; i++)
            maxTotal += magnitudes[i];

        float minSol = maxTotal * _minSolutionFraction;
        float maxSol = maxTotal * _maxSolutionFraction;

        // Only count combinations within the maxActions feasibility:
        // solution needs at least minOn toggles, and minOn <= maxActions.
        int maxAllowed = maxOn;

        int validCount = 0;
        if (maxAllowed >= minOn && minOn <= _maxActions)
        {
            for (int mask = 0; mask < (1 << n); mask++)
            {
                int onCount = 0;
                float total = 0f;
                for (int i = 0; i < n; i++)
                {
                    bool on = (mask & (1 << i)) != 0;
                    if (on) onCount++;
                    total += on ? magnitudes[i] : 0f;
                }
                if (onCount >= minOn && onCount <= maxAllowed
                    && total >= minSol && total < maxSol)
                    validCount++;
            }
        }

        return new EditorValidation
        {
            LeverCount          = n,
            MinLeversOn         = minOn,
            MaxActions          = _maxActions,
            CanPickSolution     = validCount > 0,
            ValidCombinationCount = validCount,
            Magnitudes          = magnitudes,
            MaxTotal            = maxTotal,
            MinSolutionTotal    = minSol,
            MaxSolutionTotal    = maxSol,
            SolveAngle          = _solveAngle,
            DangerAngle         = _dangerAngle
        };
    }
#endif
}
