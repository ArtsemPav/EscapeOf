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

    private readonly List<PressureLever> _levers = new();
    private float _minTotal;
    private float _maxTotal;
    private float _currentArrowAngle;
    private float _targetArrowAngle;
    private float _arrowVelocity;     // required by SmoothDamp
    private float _arrowBaseEulerY;   // cached once — never read from the transform again
    private float _arrowBaseEulerZ;
    private bool  _loadedIsSolved;    // set by LoadSaveData before Start() runs

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

        // Cache Y/Z euler of the arrow once to avoid gimbal lock jitter.
        if (_arrow != null)
        {
            Vector3 baseEuler = _arrow.localEulerAngles;
            _arrowBaseEulerY  = baseEuler.y;
            _arrowBaseEulerZ  = baseEuler.z;
        }

        // If the save system already marked this puzzle as solved, restore state
        // directly without randomizing or animating — the player has already won.
        if (_loadedIsSolved)
        {
            RestoreSolvedState();
            return;
        }

        RandomizeLevers();

        float initial      = GetCurrentTotal();
        _currentArrowAngle = PressureToAngle(initial);
        _targetArrowAngle  = _currentArrowAngle;
        ApplyArrow(_currentArrowAngle);

        Debug.Log($"[PressurePuzzle] {_levers.Count} levers. " +
                  $"Range [{_minTotal}…{_maxTotal}]. Solve at 0° ±{_solveAngleTolerance}°. " +
                  $"Start angle: {_currentArrowAngle:F1}°");
    }

    /// <summary>
    /// Randomises lever states, re-rolling until the starting total is at least
    /// _minStartDistanceFraction of the full range away from the target.
    /// This prevents the puzzle from beginning in (or near) the solved state.
    /// </summary>
    private void RandomizeLevers()
    {
        float angleRange  = Mathf.Abs(_arrowAngleAtMax - _arrowAngleAtMin);
        float minDistance = angleRange * _minStartDistanceFraction;

        const int maxAttempts = 500;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            foreach (var lever in _levers)
                lever.SetStateQuiet(UnityEngine.Random.value > 0.5f);

            float angle = PressureToAngle(GetCurrentTotal());
            if (Mathf.Abs(angle) >= minDistance)
                return;
        }

        Debug.LogWarning("[PressurePuzzle] Could not randomize far enough from 0°. " +
                         "Check lever values and angle range.");
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
    /// Moves the arrow to the current total and checks for a solution.
    /// In real-time mode this is a no-op (arrow already tracks live).
    /// </summary>
    public void Confirm()
    {
        if (IsSolved) return;

        UpdateArrowTarget();

        if (Mathf.Abs(_targetArrowAngle) <= _solveAngleTolerance)
            Solve();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void UpdateArrowTarget()
    {
        _targetArrowAngle = PressureToAngle(GetCurrentTotal());

        if (!_confirmOnInteract && Mathf.Abs(_targetArrowAngle) <= _solveAngleTolerance)
            Solve();
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
        float t = Mathf.InverseLerp(_minTotal, _maxTotal, pressure);
        return Mathf.Lerp(_arrowAngleAtMin, _arrowAngleAtMax, t);
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
        SaveManager.Instance?.Save();
        Debug.Log("[PressurePuzzle] Solved!");
    }

    /// <summary>
    /// Applies the solved visual state instantly without invoking events.
    /// Called on load when the save data shows the puzzle was already solved.
    /// </summary>
    private void RestoreSolvedState()
    {
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

    /// <summary>Serializes the solved state to JSON.</summary>
    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isSolved = IsSolved });
    }

    /// <summary>
    /// Restores state from JSON. Called by SaveManager before Start(),
    /// so only the flag is set here — Start() applies the actual state.
    /// </summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _loadedIsSolved = data.isSolved;
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
    }
}
