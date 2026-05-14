using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main controller for the clock puzzle.
/// Validates hand positions against the target time and triggers puzzle completion.
/// Controls ambient audio (looping tick, chime) and pendulum synchronization
/// based on the chosen <see cref="ClockSolveBehavior"/>.
/// Optionally moves the clock along a local-space axis when the puzzle is solved.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ClockPuzzleController : MonoBehaviour
{
    // ── Inspector: Correct Time ────────────────────────────────────────────────

    [Header("Correct Time")]
    [Tooltip("Target hour (0–11 in 12-hour format).")]
    [SerializeField, Range(0, 11)] private int _targetHour = 6;

    [Tooltip("Target minute (0–59).")]
    [SerializeField, Range(0, 59)] private int _targetMinute = 0;

    [Tooltip("Allowed deviation in minutes. 0 = exact match required.")]
    [SerializeField] private int _toleranceMinutes = 0;

    // ── Inspector: Randomization ───────────────────────────────────────────────

    [Header("Randomization")]
    [Tooltip("Minimum number of minute steps the minute hand must start away from the solution (out of 60).")]
    [SerializeField, Range(1, 30)] private int _minuteHandMinDistance = 15;

    [Tooltip("Minimum number of hour steps the hour hand must start away from the solution (out of 12).")]
    [SerializeField, Range(1, 6)]  private int _hourHandMinDistance = 3;

    // ── Inspector: Solve Behavior ──────────────────────────────────────────────

    [Header("Solve Behavior")]
    [Tooltip("StopOnSolve: clock ticks from the start and stops when solved.\n" +
             "StartOnSolve: clock is silent and still initially, starts ticking when solved.")]
    [SerializeField] private ClockSolveBehavior _solveBehavior = ClockSolveBehavior.StopOnSolve;

    // ── Inspector: Tick (looping) ──────────────────────────────────────────────

    [Header("Tick (looping)")]
    [Tooltip("Looping tick clip whose waveform peaks drive the pendulum timing.")]
    [SerializeField] private AudioClip _tickClip;

    [SerializeField, Range(0f, 1f)] private float _tickVolume = 0.8f;

    [SerializeField, Min(0f)] private float _tickMinDistance = 1f;
    [SerializeField, Min(0f)] private float _tickMaxDistance = 15f;

    // ── Inspector: Peak Detection ──────────────────────────────────────────────

    [Header("Peak Detection")]
    [Tooltip("Normalized amplitude threshold (0–1). Raise if false peaks appear; lower if real peaks are missed.")]
    [SerializeField, Range(0.05f, 0.95f)] private float _peakThreshold = 0.35f;

    [Tooltip("Minimum time between two detected peaks in seconds.")]
    [SerializeField, Min(0.05f)] private float _minPeakInterval = 0.15f;

    // ── Inspector: Solved Chime ────────────────────────────────────────────────

    [Header("Solved Chime")]
    [Tooltip("One-shot chime played via AudioManager when the puzzle is solved.")]
    [SerializeField] private AudioClip _chimeClip;

    [SerializeField, Range(0f, 1f)] private float _chimeVolume = 1f;

    // ── Inspector: On Solve: Movement ─────────────────────────────────────────

    [Header("On Solve: Movement")]
    [Tooltip("If enabled, the clock moves along the chosen axis when the puzzle is solved.")]
    [SerializeField] private bool _moveOnSolve = false;

    [Tooltip("Local-space axis along which the clock moves.")]
    [SerializeField] private ClockMoveAxis _moveAxis = ClockMoveAxis.Y;

    [Tooltip("Used when Move Axis is set to Custom.")]
    [SerializeField] private Vector3 _moveAxisCustom = Vector3.up;

    [Tooltip("Total distance (in local units) to travel.")]
    [SerializeField] private float _moveDistance = 0.5f;

    [Tooltip("Duration of the movement animation in seconds.")]
    [SerializeField, Min(0.01f)] private float _moveDuration = 1f;

    [Tooltip("Easing curve for the movement (X = normalized time, Y = normalized distance).")]
    [SerializeField] private AnimationCurve _moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── State ──────────────────────────────────────────────────────────────────

    private PuzzleModeController _puzzleMode;
    private ClockHand            _minuteHand;
    private ClockHand            _hourHand;
    private PendulumSwing        _pendulum;
    private AudioSource          _tickSource;
    private bool                 _tickingActive;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        if (_puzzleMode == null)
        {
            Debug.LogError($"[{nameof(ClockPuzzleController)}] PuzzleModeController not found on {gameObject.name}.", this);
            return;
        }

        _puzzleMode.OnSolved += HandleSolved;

        foreach (var hand in GetComponentsInChildren<ClockHand>())
        {
            if (hand.HandType == ClockHandType.Minute)
                _minuteHand = hand;
            else if (hand.HandType == ClockHandType.Hour)
                _hourHand = hand;
        }

        if (_minuteHand != null) _minuteHand.OnReleased += CheckSolution;
        if (_hourHand   != null) _hourHand.OnReleased   += CheckSolution;

        _pendulum = GetComponentInChildren<PendulumSwing>();
    }

    private void Start()
    {
        if (_puzzleMode.IsSolved)
        {
            // Restore post-solve visual state silently (no audio, no animation).
            if (_solveBehavior == ClockSolveBehavior.StartOnSolve)
            {
                // Clock is supposed to be ticking after solve — start tick without chime.
                StartTick();
                _pendulum?.StartSwing();
            }
            else // StopOnSolve — clock should be still and silent.
            {
                _pendulum?.StopSwing();
            }

            if (_moveOnSolve)
            {
                // Apply the final moved position immediately without animating.
                transform.localPosition += GetMoveAxisVector() * _moveDistance;
            }

            return;
        }

        bool handsRestored = (_minuteHand != null && _minuteHand.WasRestoredFromSave) ||
                             (_hourHand   != null && _hourHand.WasRestoredFromSave);

        if (!handsRestored)
        {
            _minuteHand?.RandomizePosition(_targetMinute, _minuteHandMinDistance);
            _hourHand?.RandomizePosition(_targetHour,     _hourHandMinDistance);
        }

        if (_solveBehavior == ClockSolveBehavior.StartOnSolve)
        {
            // Clock starts silent and still; activates on solve.
            _pendulum?.StopSwing();
            return;
        }

        // StopOnSolve: clock ticks from the start.
        StartTick();
    }

    private void OnDestroy()
    {
        if (_minuteHand != null) _minuteHand.OnReleased -= CheckSolution;
        if (_hourHand   != null) _hourHand.OnReleased   -= CheckSolution;
        if (_puzzleMode != null) _puzzleMode.OnSolved   -= HandleSolved;

        StopTick();
    }

    // ── Solution Check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether both hands match the target time within the allowed tolerance.
    /// Called every time any hand is released after dragging.
    /// </summary>
    private void CheckSolution()
    {
        if (_puzzleMode == null || _puzzleMode.IsSolved) return;
        if (_minuteHand == null || _hourHand == null)    return;

        int currentMinute     = _minuteHand.CurrentMinute;
        int currentHour       = _hourHand.CurrentMinute;   // Returns hour * 5 (0–55)
        int targetHourMinutes = _targetHour * 5;           // Convert hour to minute-scale (0–55)

        bool minuteMatch = IsWithinTolerance(currentMinute, _targetMinute,     60);
        bool hourMatch   = IsWithinTolerance(currentHour,   targetHourMinutes, 60);

        if (!minuteMatch || !hourMatch) return;

        _puzzleMode.SetSolved();
    }

    // ── Solve Handling ─────────────────────────────────────────────────────────

    private void HandleSolved()
    {
        PlayChime();

        if (_solveBehavior == ClockSolveBehavior.StartOnSolve)
        {
            StartTick();
            _pendulum?.StartSwing();
        }
        else // StopOnSolve
        {
            StopTick();
            _pendulum?.StopSwing();
        }

        if (_moveOnSolve)
            StartCoroutine(MoveCoroutine());
    }

    // ── On Solve: Movement ─────────────────────────────────────────────────────

    private IEnumerator MoveCoroutine()
    {
        Vector3 axis     = GetMoveAxisVector();
        Vector3 startPos = transform.localPosition;
        Vector3 endPos   = startPos + axis * _moveDistance;
        float   elapsed  = 0f;

        while (elapsed < _moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = _moveCurve.Evaluate(Mathf.Clamp01(elapsed / _moveDuration));
            transform.localPosition = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        transform.localPosition = endPos;
    }

    /// <summary>Returns the normalized local-space direction to move in.</summary>
    private Vector3 GetMoveAxisVector()
    {
        return _moveAxis switch
        {
            ClockMoveAxis.X      => Vector3.right,
            ClockMoveAxis.Y      => Vector3.up,
            ClockMoveAxis.Z      => Vector3.forward,
            ClockMoveAxis.Custom => _moveAxisCustom.sqrMagnitude > 0f
                                        ? _moveAxisCustom.normalized
                                        : Vector3.up,
            _                    => Vector3.up
        };
    }

    // ── Ambient Audio ──────────────────────────────────────────────────────────

    private void StartTick()
    {
        if (_tickClip == null)
        {
            Debug.LogWarning($"[{nameof(ClockPuzzleController)}] Tick clip is not assigned on {gameObject.name}.", this);
            return;
        }

        if (AudioManager.Instance == null)
        {
            Debug.LogWarning($"[{nameof(ClockPuzzleController)}] AudioManager not found.", this);
            return;
        }

        Transform parent = _pendulum != null ? _pendulum.transform : transform;
        _tickSource = AudioManager.Instance.Play3DLoop(
            _tickClip, parent, _tickVolume, _tickMinDistance, _tickMaxDistance);

        float[] peaks = AnalyzePeaks(_tickClip, _peakThreshold, _minPeakInterval);
        Debug.Log($"[{nameof(ClockPuzzleController)}] Detected {peaks.Length} peak(s) in '{_tickClip.name}'.");

        _pendulum?.SetTickData(_tickSource, peaks, _tickClip.length);
        _tickingActive = true;
    }

    private void StopTick()
    {
        _tickingActive = false;
        _pendulum?.ClearTickData();

        if (_tickSource == null) return;
        _tickSource.Stop();
        Destroy(_tickSource.gameObject);
        _tickSource = null;
    }

    private void PlayChime()
    {
        if (_chimeClip == null)
        {
            Debug.LogWarning($"[{nameof(ClockPuzzleController)}] Chime clip is not assigned on {gameObject.name}.", this);
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(_chimeClip, _chimeVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(_chimeClip, transform.position, _chimeVolume);
        }
    }

    // ── Waveform Analysis ──────────────────────────────────────────────────────

    /// <summary>
    /// Scans the clip sample data and returns timestamps (seconds) of amplitude
    /// spikes above the threshold, spaced at least <paramref name="minIntervalSec"/> apart.
    /// </summary>
    private static float[] AnalyzePeaks(AudioClip clip, float threshold, float minIntervalSec)
    {
        int channels    = clip.channels;
        int sampleCount = clip.samples;
        int sampleRate  = clip.frequency;

        var raw = new float[sampleCount * channels];
        if (!clip.GetData(raw, 0))
        {
            Debug.LogWarning($"[{nameof(ClockPuzzleController)}] Could not read samples from '{clip.name}'. " +
                             "Ensure Load Type is not Streaming.");
            return new float[0];
        }

        // Build a mono amplitude envelope using 5 ms windows.
        int windowSamples = Mathf.Max(1, sampleRate / 200);
        int frameCount    = sampleCount / windowSamples;
        var envelope      = new float[frameCount];

        for (int f = 0; f < frameCount; f++)
        {
            float maxAbs = 0f;
            int   start  = f * windowSamples * channels;
            int   end    = Mathf.Min(start + windowSamples * channels, raw.Length);
            for (int i = start; i < end; i++)
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(raw[i]));
            envelope[f] = maxAbs;
        }

        // Normalize to [0, 1].
        float globalMax = 0f;
        foreach (float v in envelope) globalMax = Mathf.Max(globalMax, v);
        if (globalMax > 0f)
            for (int i = 0; i < envelope.Length; i++)
                envelope[i] /= globalMax;

        // Find local maxima above threshold, enforcing a minimum frame gap.
        int minFrameGap = Mathf.Max(1, Mathf.RoundToInt(minIntervalSec * sampleRate / windowSamples));
        var peaks       = new List<float>();
        int lastFrame   = -minFrameGap;

        for (int f = 1; f < frameCount - 1; f++)
        {
            bool aboveThreshold = envelope[f] >= threshold;
            bool isLocalMax     = envelope[f] >= envelope[f - 1] && envelope[f] >= envelope[f + 1];
            bool farEnough      = f - lastFrame >= minFrameGap;

            if (aboveThreshold && isLocalMax && farEnough)
            {
                peaks.Add((float)(f * windowSamples) / sampleRate);
                lastFrame = f;
            }
        }

        return peaks.ToArray();
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
