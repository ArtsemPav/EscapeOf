using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
/// <summary>
/// Validates the clock hand positions against the target time and triggers puzzle completion.
/// Self-wires all references in Awake via GetComponent / GetComponentsInChildren.
/// WASD exits puzzle mode because UIManager.PushModalState() disables the Player action map.
/// </summary>
public class ClockPuzzleController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Correct Time")]
    [Tooltip("Target hour (0–11 in 12-hour format).")]
    [SerializeField, Range(0, 11)] private int _targetHour = 6;

    [Tooltip("Target minute (0–59).")]
    [SerializeField, Range(0, 59)] private int _targetMinute = 0;

    [Tooltip("Allowed deviation in minutes. 0 = exact match required.")]
    [SerializeField] private int _toleranceMinutes = 0;

    [Header("Randomization")]
    [Tooltip("Minimum number of minute steps the minute hand must start away from the solution (out of 60).")]
    [SerializeField, Range(1, 30)] private int _minuteHandMinDistance = 15;

    [Tooltip("Minimum number of hour steps the hour hand must start away from the solution (out of 12).")]
    [SerializeField, Range(1, 6)]  private int _hourHandMinDistance = 3;

    [Header("Events")]
    [Tooltip("Fired when the correct time is set.")]
    [SerializeField] private UnityEvent _onSolved;

    [Tooltip("Optional ScriptableObject event channel. Prefab works without it.")]
    [SerializeField] private GameEvent _onSolvedEvent;

    // ── State ──────────────────────────────────────────────────────────────────
    private PuzzleModeController _puzzleMode;
    private ClockHand            _minuteHand;
    private ClockHand            _hourHand;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        if (_puzzleMode == null)
        {
            Debug.LogError($"[{nameof(ClockPuzzleController)}] PuzzleModeController not found on {gameObject.name}.", this);
            return;
        }

        foreach (var hand in GetComponentsInChildren<ClockHand>())
        {
            if (hand.HandType == ClockHandType.Minute)
                _minuteHand = hand;
            else if (hand.HandType == ClockHandType.Hour)
                _hourHand = hand;
        }

        if (_minuteHand != null) _minuteHand.OnReleased += CheckSolution;
        if (_hourHand   != null) _hourHand.OnReleased   += CheckSolution;
    }

    private void Start()
    {
        // Skip randomization if the puzzle is already solved or hands were restored from a save.
        if (_puzzleMode.IsSolved) return;
        if (_minuteHand != null && _minuteHand.WasRestoredFromSave) return;
        if (_hourHand   != null && _hourHand.WasRestoredFromSave)   return;

        _minuteHand?.RandomizePosition(_targetMinute,  _minuteHandMinDistance);
        _hourHand?.RandomizePosition(_targetHour,      _hourHandMinDistance);
    }

    private void OnDestroy()
    {
        if (_minuteHand != null) _minuteHand.OnReleased -= CheckSolution;
        if (_hourHand   != null) _hourHand.OnReleased   -= CheckSolution;
    }

    private void Update()
    {
        if (_puzzleMode == null || !_puzzleMode.IsActive) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.wKey.isPressed || kb.sKey.isPressed || kb.aKey.isPressed || kb.dKey.isPressed)
            _puzzleMode.ExitPuzzleMode();
    }

    // ── Solution Check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether both hands match the target time within the allowed tolerance.
    /// Called every time any hand moves by one step.
    /// </summary>
    private void CheckSolution()
    {
        if (_puzzleMode == null || _puzzleMode.IsSolved) return;
        if (_minuteHand == null || _hourHand == null)    return;

        int currentMinute = _minuteHand.CurrentMinute;
        int currentHour   = _hourHand.CurrentMinute; // Returns hour * 5 (0–55)

        int targetHourMinutes = _targetHour * 5; // Convert hour to minute-scale (0–55)

        bool minuteMatch = IsWithinTolerance(currentMinute, _targetMinute, 60);
        bool hourMatch   = IsWithinTolerance(currentHour,   targetHourMinutes, 60);

        if (!minuteMatch || !hourMatch) return;

        Solve();
    }

    private void Solve()
    {
        _onSolved?.Invoke();
        _onSolvedEvent?.Raise();
        _puzzleMode.SetSolved();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="value"/> is within <see cref="_toleranceMinutes"/>
    /// of <paramref name="target"/> on a circular scale of <paramref name="modulo"/>.
    /// </summary>
    private bool IsWithinTolerance(int value, int target, int modulo)
    {
        int diff = Mathf.Abs(value - target) % modulo;
        if (diff > modulo / 2) diff = modulo - diff;
        return diff <= _toleranceMinutes;
    }
}
