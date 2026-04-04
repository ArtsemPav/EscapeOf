using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Pressure puzzle controller.
///
/// Each child PressureLever has an OFF value and an ON value.
/// Total pressure = sum of every lever's CurrentValue (off or on).
/// The arrow always reflects the current total on the dial.
/// When the total reaches _targetValue (within tolerance), the puzzle is solved.
///
/// Arrow maps the full possible range [all-OFF sum … all-ON sum]
/// to [_arrowAngleAtMin … _arrowAngleAtMax] on the local X axis.
///
/// Expected hierarchy:
///   PreasurePuzzel          ← this component
///     stick1                ← PressureLever on Interactable Layer
///     ...
///     screen
///       arrow               ← assign to _arrow field
/// </summary>
public class PressurePuzzle : MonoBehaviour, ISaveable
{
    // ── References ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Transform of the arrow inside the dial (child of 'screen').")]
    [SerializeField] private Transform _arrow;

    [Header("Save")]
    [Tooltip("Unique identifier for the save system. Must be unique across the entire game.")]
    [SerializeField] private string _saveId = "pressure_puzzle";

    [Header("Dial Settings")]
    [Tooltip("Arrow local X angle when total pressure is at its minimum (all levers at their minimum value).")]
    [SerializeField] private float _arrowAngleAtMin = 135f;
    [Tooltip("Arrow local X angle when total pressure is at its maximum (all levers at their maximum value).")]
    [SerializeField] private float _arrowAngleAtMax = -135f;
    [Tooltip("Smooth speed at which the arrow visually follows the current pressure.")]
    [SerializeField] private float _arrowSmoothSpeed = 8f;

    [Header("Puzzle")]
    [Tooltip("Allowed deviation from 0° in degrees at which the puzzle counts as solved. " +
             "The arrow must point to the neutral (0°) position within this margin.")]
    [SerializeField] private float _solveAngleTolerance = 10f;

    [Header("Randomization")]
    [Tooltip("Minimum distance from the target as a fraction of the full range (0–1). " +
             "Prevents the puzzle from starting already solved or trivially close.")]
    [SerializeField] [Range(0f, 1f)] private float _minStartDistanceFraction = 0.35f;

    [Tooltip("Minimum number of levers the player must flip to reach the solution. " +
             "Prevents trivially easy starts where only 1–2 levers need changing. " +
             "Must be ≤ total lever count.")]
    [SerializeField] [Range(1, 10)] private int _minFlipsFromSolution = 3;

    [Header("Solution")]
    [Tooltip("Minimum number of levers that must be ON in the randomly chosen solution. " +
             "Also enforces the same minimum for OFF levers, preventing trivial single-lever solutions. " +
             "Requires at least 2 × this value levers in total.")]
    [SerializeField] [Range(1, 5)] private int _minLeversOnInSolution = 2;

    [Header("Lever Value Generation")]
    [Tooltip("Contribution magnitude for the lever with the smallest impact. " +
             "Every lever gets offValue = –magnitude and onValue = +magnitude. " +
             "Magnitudes are evenly spaced starting from this value.")]
    [SerializeField] [Min(1f)] private float _leverValueBase = 5f;

    [Tooltip("Spacing between consecutive lever magnitudes. " +
             "With 6 levers, base = 5 and step = 5 → magnitudes: 5, 10, 15, 20, 25, 30 (shuffled per session).")]
    [SerializeField] [Min(1f)] private float _leverValueStep = 5f;

    [Header("Difficulty")]
    [Tooltip("When enabled, the arrow only updates when the player interacts with the gauge — " +
             "not after each lever toggle. Removes real-time feedback and forces mental calculation.")]
    [SerializeField] private bool _confirmOnInteract = true;

    [Header("Events")]
    [Tooltip("Fired exactly once when the player reaches the target pressure.")]
    [SerializeField] private UnityEvent _onSolved;

    [Header("Reward")]
    [Tooltip("GameObjects to activate when the puzzle is solved (e.g. lights, doors).")]
    [SerializeField] private GameObject[] _rewardObjects;

    // ── Runtime state ─────────────────────────────────────────────────────────

    /// <summary>True once the puzzle has been solved.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>Whether the gauge must be interacted with to commit lever state and check for a solution.</summary>
    public bool ConfirmOnInteract => _confirmOnInteract;

    private readonly List<PressureLever> _levers = new();
    private readonly List<int> _validSolutionMasks = new(); // all combinations within _solveAngleTolerance
    private float _minTotal;
    private float _maxTotal;
    private int   _solutionMask;     // bitmask: bit i = lever i is ON in the solution
    private float _solutionTotal;    // total that maps to 0° — chosen randomly each session
    private float _currentArrowAngle;
    private float _targetArrowAngle;
    private float _arrowVelocity;    // required by SmoothDamp
    private float _arrowBaseEulerY;  // cached once — never read from the transform again
    private float _arrowBaseEulerZ;
    private bool  _loadedIsSolved;   // set by LoadSaveData before Start() runs
    private bool[] _loadedLeverStates; // lever IsOn per index, restored from save

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Start()
    {
        _levers.Clear();
        GetComponentsInChildren(includeInactive: false, _levers);

        // Cache Y/Z euler of the arrow once to avoid gimbal lock jitter.
        if (_arrow != null)
        {
            Vector3 baseEuler = _arrow.localEulerAngles;
            _arrowBaseEulerY  = baseEuler.y;
            _arrowBaseEulerZ  = baseEuler.z;
        }

        // If the save system already marked this puzzle as solved, restore lever
        // visual states and arrow position — skip randomization and value generation.
        if (_loadedIsSolved)
        {
            RestoreSolvedState();
            return;
        }

        // Assign lever values before computing the total range.
        GenerateAndAssignLeverValues();

        _minTotal = 0f;
        _maxTotal = 0f;
        foreach (var lever in _levers)
        {
            _minTotal += Mathf.Min(lever.OffValue, lever.OnValue);
            _maxTotal += Mathf.Max(lever.OffValue, lever.OnValue);
        }

        if (Mathf.Approximately(_minTotal, _maxTotal))
        {
            _minTotal -= 1f;
            _maxTotal += 1f;
        }

        PickRandomSolution();
        FindAllValidSolutions();
        RandomizeLevers();

        float initial      = GetCurrentTotal();
        _currentArrowAngle = PressureToAngle(initial);
        _targetArrowAngle  = _currentArrowAngle;
        ApplyArrow(_currentArrowAngle);

        // Final snap — ensures lever visuals match their randomized IsOn state
        // regardless of script execution order during scene initialization.
        foreach (var lever in _levers)
            lever.SnapVisual();

        Debug.Log($"[PressurePuzzle] {_levers.Count} levers. " +
                  $"Range [{_minTotal}…{_maxTotal}]. Solution total: {_solutionTotal}. " +
                  $"Solve at 0° ±{_solveAngleTolerance}°. Start angle: {_currentArrowAngle:F1}°");
    }

    /// <summary>
    /// Generates a unique magnitude per lever using a linear series starting at
    /// _leverValueBase with _leverValueStep spacing, then Fisher-Yates shuffles the
    /// assignment order so no lever is predictably the "strongest" or "weakest".
    ///
    /// Each lever receives: offValue = –magnitude, onValue = +magnitude.
    ///
    /// Called every session before PickRandomSolution() and RandomizeLevers(),
    /// so values differ each run even for the same scene setup.
    /// </summary>
    private void GenerateAndAssignLeverValues()
    {
        int n = _levers.Count;
        if (n == 0) return;

        // Build sorted magnitudes: base, base+step, base+2·step, …
        float[] magnitudes = new float[n];
        for (int i = 0; i < n; i++)
            magnitudes[i] = _leverValueBase + _leverValueStep * i;

        // Fisher-Yates shuffle — assign magnitudes in random order to levers.
        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (magnitudes[i], magnitudes[j]) = (magnitudes[j], magnitudes[i]);
        }

        for (int i = 0; i < n; i++)
            _levers[i].AssignValues(-magnitudes[i], magnitudes[i]);

        Debug.Log($"[PressurePuzzle] Lever magnitudes assigned: [{string.Join(", ", magnitudes)}]");
    }

    /// <summary>
    /// Brute-forces all 2^N lever combinations and records every mask whose arrow angle
    /// falls within _solveAngleTolerance. Called once after PickRandomSolution() so that
    /// RandomizeLevers() can measure the true minimum distance to ANY winning state,
    /// not only the primary solution mask.
    /// </summary>
    private void FindAllValidSolutions()
    {
        _validSolutionMasks.Clear();
        int n = _levers.Count;

        for (int mask = 0; mask < (1 << n); mask++)
        {
            float total = 0f;
            for (int i = 0; i < n; i++)
                total += ((mask & (1 << i)) != 0) ? _levers[i].OnValue : _levers[i].OffValue;

            if (Mathf.Abs(PressureToAngle(total)) <= _solveAngleTolerance)
                _validSolutionMasks.Add(mask);
        }

        Debug.Log($"[PressurePuzzle] {_validSolutionMasks.Count} valid solution combination(s) found " +
                  $"within ±{_solveAngleTolerance}°.");
    }

    /// <summary>
    /// Returns the minimum number of lever flips needed to reach ANY valid solution
    /// from the given starting mask. This is the true lower bound the player must
    /// overcome, regardless of which winning combination they aim for.
    /// </summary>
    private int MinFlipsToAnySolution(int startMask)
    {
        int min = int.MaxValue;
        foreach (int sol in _validSolutionMasks)
            min = Mathf.Min(min, CountBits(startMask ^ sol));
        return min;
    }

    /// <summary>
    /// Picks a random lever combination as the session's goal state and stores its total
    /// in _solutionTotal. PressureToAngle() then shifts the dial so that this total maps
    /// to exactly 0° — making the puzzle solvable by construction every run.
    ///
    /// The combination must have at least _minLeversOnInSolution levers ON and the same
    /// minimum OFF, preventing trivially easy single-lever solutions.
    /// </summary>
    private void PickRandomSolution()
    {
        int n      = _levers.Count;
        int minOn  = _minLeversOnInSolution;
        int maxOn  = n - minOn;

        if (maxOn < minOn)
        {
            // Not enough levers — fall back to midpoint so play can still proceed.
            _solutionTotal = (_minTotal + _maxTotal) * 0.5f;
            Debug.LogWarning($"[PressurePuzzle] Not enough levers ({n}) for " +
                             $"_minLeversOnInSolution = {minOn}. Using midpoint as solution.");
            return;
        }

        const int maxAttempts = 300;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int mask    = UnityEngine.Random.Range(0, 1 << n);
            int onCount = 0;
            for (int i = 0; i < n; i++)
                if ((mask & (1 << i)) != 0) onCount++;

            if (onCount < minOn || onCount > maxOn) continue;

            float total = 0f;
            for (int i = 0; i < n; i++)
                total += ((mask & (1 << i)) != 0) ? _levers[i].OnValue : _levers[i].OffValue;

            _solutionMask  = mask;
            _solutionTotal = total;
            Debug.Log($"[PressurePuzzle] Solution chosen: {onCount}/{n} levers ON, total = {total}, mask = {Convert.ToString(mask, 2).PadLeft(n, '0')}");
            return;
        }

        _solutionTotal = (_minTotal + _maxTotal) * 0.5f;
        Debug.LogWarning("[PressurePuzzle] Could not find valid solution combination. Using midpoint.");
    }

    /// <summary>
    /// Randomises lever states, re-rolling until both conditions are met:
    ///   1. The arrow angle is at least _minStartDistanceFraction of the full range from 0°.
    ///   2. The minimum flips to reach ANY valid solution is at least _minFlipsFromSolution.
    ///      This uses MinFlipsToAnySolution() which checks against all winning combinations,
    ///      not only the primary solution mask — preventing trivial 1-flip paths.
    /// Falls back to satisfying only condition 2 if both cannot be met simultaneously.
    /// </summary>
    private void RandomizeLevers()
    {
        float angleRange  = Mathf.Abs(_arrowAngleAtMax - _arrowAngleAtMin);
        float minDistance = angleRange * _minStartDistanceFraction;
        int   minFlips    = Mathf.Clamp(_minFlipsFromSolution, 1, _levers.Count);

        const int maxAttempts = 500;

        // Pass 1: satisfy both the angle distance AND the minimum flips to any solution.
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int startMask = BuildRandomMask();
            if (Mathf.Abs(PressureToAngle(GetCurrentTotal())) < minDistance) continue;
            if (MinFlipsToAnySolution(startMask) >= minFlips) return;
        }

        // Pass 2: angle condition is relaxed — guarantee only the minimum flip count.
        Debug.LogWarning("[PressurePuzzle] Could not satisfy angle + flip constraints together. " +
                         $"Relaxing angle condition — start guaranteed ≥{minFlips} flips from any solution.");

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int startMask = BuildRandomMask();
            if (MinFlipsToAnySolution(startMask) >= minFlips) return;
        }

        Debug.LogError("[PressurePuzzle] Could not satisfy even the minimum flip constraint. " +
                       "Reduce _minFlipsFromSolution or add more levers.");
    }

    /// <summary>
    /// Randomises all levers to independent coin-flip states and returns the resulting bitmask.
    /// </summary>
    private int BuildRandomMask()
    {
        int mask = 0;
        for (int i = 0; i < _levers.Count; i++)
        {
            bool on = UnityEngine.Random.value > 0.5f;
            _levers[i].SetStateQuiet(on);
            if (on) mask |= (1 << i);
        }
        return mask;
    }

    /// <summary>Counts the number of set bits (population count) in an integer.</summary>
    private static int CountBits(int n)
    {
        int count = 0;
        while (n != 0) { count += n & 1; n >>= 1; }
        return count;
    }

    private void Update()
    {
        if (Mathf.Abs(_currentArrowAngle - _targetArrowAngle) < 0.01f)
        {
            if (!Mathf.Approximately(_currentArrowAngle, _targetArrowAngle))
            {
                _currentArrowAngle = _targetArrowAngle;
                _arrowVelocity     = 0f;
                ApplyArrow(_currentArrowAngle);

                if (!IsSolved && Mathf.Abs(_currentArrowAngle) <= _solveAngleTolerance)
                    Solve();
            }
            return;
        }

        // SmoothDamp is frame-rate independent and gracefully handles mid-flight
        // target changes without oscillation — unlike Lerp with Time.deltaTime.
        _currentArrowAngle = Mathf.SmoothDamp(
            _currentArrowAngle,
            _targetArrowAngle,
            ref _arrowVelocity,
            1f / _arrowSmoothSpeed
        );
        ApplyArrow(_currentArrowAngle);

        // Fire solve the moment the arrow visually enters the tolerance zone while
        // heading to a valid target — avoids the perceived pause of waiting for the
        // full SmoothDamp asymptotic settle before checking.
        if (!IsSolved
            && Mathf.Abs(_targetArrowAngle) <= _solveAngleTolerance
            && Mathf.Abs(_currentArrowAngle) <= _solveAngleTolerance)
        {
            _currentArrowAngle = _targetArrowAngle;
            _arrowVelocity     = 0f;
            ApplyArrow(_currentArrowAngle);
            Solve();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PressureLever on every toggle.
    /// In real-time mode the arrow tracks instantly.
    /// In confirm mode the arrow stays put — the player must interact with the gauge to commit.
    /// </summary>
    public void OnLeverChanged()
    {
        if (IsSolved) return;

        if (!_confirmOnInteract)
            UpdateArrowTarget();
    }

    /// <summary>
    /// Called when the player interacts with the gauge itself.
    /// Moves the arrow to the current total — solve is deferred until the arrow settles.
    /// In real-time mode this is a no-op (arrow already tracks live).
    /// </summary>
    public void Confirm()
    {
        if (IsSolved) return;

        UpdateArrowTarget();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void UpdateArrowTarget()
    {
        _targetArrowAngle = PressureToAngle(GetCurrentTotal());
        // Solve check is deferred to Update() — triggers only when the arrow finishes animating.
    }

    private float GetCurrentTotal()
    {
        float sum = 0f;
        foreach (var lever in _levers)
            sum += lever.CurrentValue;
        return sum;
    }

    private float PressureToAngle(float pressure)
    {
        // Raw angle from the configured min→max mapping.
        float t        = Mathf.InverseLerp(_minTotal, _maxTotal, pressure);
        float raw      = Mathf.Lerp(_arrowAngleAtMin, _arrowAngleAtMax, t);

        // Shift so that _solutionTotal maps to exactly 0°.
        // Every other combination is shown relative to the solution.
        float tSol     = Mathf.InverseLerp(_minTotal, _maxTotal, _solutionTotal);
        float solAngle = Mathf.Lerp(_arrowAngleAtMin, _arrowAngleAtMax, tSol);

        return raw - solAngle;
    }

    private void ApplyArrow(float angle)
    {
        if (_arrow == null) return;
        // Write all three components from cached/constant values — never read localEulerAngles.
        // This avoids Quaternion→Euler instability when X crosses through ±90° (gimbal lock).
        _arrow.localEulerAngles = new Vector3(angle, _arrowBaseEulerY, _arrowBaseEulerZ);
    }

    private void Solve()
    {
        IsSolved = true;

        // Snap arrow exactly to 0° — the neutral solved position.
        _targetArrowAngle  = 0f;
        _currentArrowAngle = 0f;
        ApplyArrow(0f);

        foreach (var obj in _rewardObjects)
            if (obj != null) obj.SetActive(true);

        _onSolved.Invoke();
        SaveManager.Instance?.Save(); // GetSaveData() captures lever states at this point.
        Debug.Log("[PressurePuzzle] Solved!");
    }

    /// <summary>
    /// Applies the solved visual state instantly without invoking events.
    /// Called on load when the save data shows the puzzle was already solved.
    /// Lever states from the winning combination are restored from save so the
    /// player sees the exact configuration they used to solve the puzzle.
    /// </summary>
    private void RestoreSolvedState()
    {
        // Restore lever visual states from save so they show the winning combination.
        if (_loadedLeverStates != null && _loadedLeverStates.Length == _levers.Count)
        {
            for (int i = 0; i < _levers.Count; i++)
                _levers[i].SetStateQuiet(_loadedLeverStates[i]);
        }

        IsSolved           = true;
        _targetArrowAngle  = 0f;
        _currentArrowAngle = 0f;
        ApplyArrow(0f);

        foreach (var obj in _rewardObjects)
            if (obj != null) obj.SetActive(true);

        Debug.Log("[PressurePuzzle] Restored solved state from save.");
    }

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>
    /// Serializes the solved state and the current lever positions to JSON.
    /// Lever states are captured here (called from SaveManager right after Solve()),
    /// so they always reflect the winning combination.
    /// </summary>
    public string GetSaveData()
    {
        var states = new bool[_levers.Count];
        for (int i = 0; i < _levers.Count; i++)
            states[i] = _levers[i].IsOn;

        return JsonUtility.ToJson(new SaveData { isSolved = IsSolved, leverStates = states });
    }

    /// <summary>
    /// Restores state from JSON. Called by SaveManager before Start(),
    /// so only flags are set here — Start() applies the actual visual state.
    /// </summary>
    public void LoadSaveData(string json)
    {
        var data            = JsonUtility.FromJson<SaveData>(json);
        _loadedIsSolved     = data.isSolved;
        _loadedLeverStates  = data.leverStates;
    }

    [Serializable]
    private struct SaveData
    {
        public bool   isSolved;
        /// <summary>IsOn state per lever at the moment the puzzle was solved.</summary>
        public bool[] leverStates;
    }

    // ── Editor validation ─────────────────────────────────────────────────────
#if UNITY_EDITOR
    /// <summary>
    /// Result of the editor-time solvability check.
    /// Populated by <see cref="GetEditorValidation"/> and consumed by PressurePuzzleEditor.
    /// </summary>
    public struct EditorValidation
    {
        /// <summary>Number of levers found as children.</summary>
        public int LeverCount;
        /// <summary>Minimum levers ON required in the solution.</summary>
        public int MinLeversOn;
        /// <summary>True when enough levers exist to satisfy the MinLeversOn constraint.</summary>
        public bool CanPickSolution;
        /// <summary>Number of valid solution combinations that satisfy the constraint.</summary>
        public int ValidCombinationCount;
        /// <summary>Magnitudes that will be generated (sorted, before shuffle).</summary>
        public float[] Magnitudes;
        /// <summary>Sum of all magnitudes — equals both |minTotal| and maxTotal.</summary>
        public float TotalRange;
    }

    /// <summary>
    /// Computes validation data using current inspector values.
    /// Lever values are not yet assigned at edit time, so magnitudes are derived
    /// directly from _leverValueBase and _leverValueStep.
    /// Called from PressurePuzzleEditor every OnInspectorGUI frame.
    /// </summary>
    public EditorValidation GetEditorValidation()
    {
        var levers = GetComponentsInChildren<PressureLever>(includeInactive: false);
        int n      = levers.Length;
        int minOn  = _minLeversOnInSolution;
        int maxOn  = n - minOn;

        // Count how many combinations satisfy the ON-count constraint.
        int validCount = 0;
        if (maxOn >= minOn)
        {
            for (int mask = 0; mask < (1 << n); mask++)
            {
                int onCount = 0;
                for (int i = 0; i < n; i++)
                    if ((mask & (1 << i)) != 0) onCount++;
                if (onCount >= minOn && onCount <= maxOn)
                    validCount++;
            }
        }

        // Compute the magnitudes that would be generated (sorted order, before shuffle).
        float[] magnitudes  = new float[n];
        float   totalRange  = 0f;
        for (int i = 0; i < n; i++)
        {
            magnitudes[i] = _leverValueBase + _leverValueStep * i;
            totalRange    += magnitudes[i];
        }

        return new EditorValidation
        {
            LeverCount            = n,
            MinLeversOn           = minOn,
            CanPickSolution       = validCount > 0,
            ValidCombinationCount = validCount,
            Magnitudes            = magnitudes,
            TotalRange            = totalRange
        };
    }
#endif
}
