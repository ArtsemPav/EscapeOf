using UnityEngine;

/// <summary>
/// Animates a sinusoidal pendulum swing around a configurable axis.
/// The initial local rotation is the rest position (bottom of the arc).
///
/// When tick data is provided via <see cref="SetTickData"/>, the pendulum phase
/// is derived from detected audio peak timestamps using N intervals per loop
/// (one interval = one half-swing between two consecutive peaks). The "wrap"
/// interval spans from the last peak of cycle k to the first peak of cycle k+1,
/// guaranteeing smooth continuity across loop boundaries regardless of clip length.
///
/// Phase at every peak boundary = integer * π, so cos(phase) = ±1 (extreme position).
///
/// Falls back to wall-clock time when no tick data is available.
/// Start/stop is controlled externally by ClockPuzzleController via
/// <see cref="StartSwing"/> and <see cref="StopSwing"/>.
/// </summary>
public class PendulumSwing : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Tooltip("Maximum angle of the swing in degrees on each side.")]
    [SerializeField] private float _amplitude = 15f;

    [Tooltip("Oscillations per second used when no audio sync data is assigned.")]
    [SerializeField] private float _fallbackFrequency = 1f;

    [Tooltip("Local axis around which the pendulum swings.")]
    [SerializeField] private Vector3 _swingAxis = Vector3.forward;

    [Tooltip("Speed in degrees/sec at which the pendulum settles to the bottom after being stopped.")]
    [SerializeField, Min(1f)] private float _settleSpeed = 90f;

    // ── Sync state ─────────────────────────────────────────────────────────────

    private AudioSource _audioSource;
    private float[]     _peakTimes;    // sorted peak timestamps within [0, clipLength]
    private float       _clipLength;
    private float       _elapsedTime;  // monotonically increasing; handles loop wraps
    private float       _lastRawTime;

    // ── Other state ────────────────────────────────────────────────────────────

    private Quaternion _initialRotation;
    private bool       _isStopped;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Supplies audio sync data. The pendulum phase is driven by N intervals per
    /// loop, where N = number of detected peaks. Each interval is a half-swing
    /// between two consecutive peaks; the last interval wraps to peaks[0] of the
    /// next loop.
    /// </summary>
    public void SetTickData(AudioSource audioSource, float[] peakTimes, float clipLength)
    {
        _audioSource = audioSource;
        _peakTimes   = peakTimes;
        _clipLength  = clipLength;
        _lastRawTime = audioSource != null ? audioSource.time : 0f;
        _elapsedTime = _lastRawTime;
    }

    /// <summary>Detaches audio sync. Pendulum falls back to wall-clock time.</summary>
    public void ClearTickData()
    {
        _audioSource = null;
        _peakTimes   = null;
    }

    /// <summary>Resumes pendulum swing. Called by ClockPuzzleController.</summary>
    public void StartSwing() => _isStopped = false;

    /// <summary>Halts pendulum swing and smoothly settles it to the rest position. Called by ClockPuzzleController.</summary>
    public void StopSwing() => _isStopped = true;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _initialRotation = transform.localRotation;
    }

    private void Update()
    {
        if (_isStopped)
        {
            transform.localRotation = Quaternion.RotateTowards(
                transform.localRotation,
                _initialRotation,
                _settleSpeed * Time.deltaTime
            );
            return;
        }

        float angle = ComputeAngle();
        transform.localRotation = _initialRotation * Quaternion.AngleAxis(angle, _swingAxis);
    }

    // ── Phase computation ──────────────────────────────────────────────────────

    private float ComputeAngle()
    {
        if (_audioSource == null || _peakTimes == null || _peakTimes.Length == 0 ||
            _clipLength <= 0f || !_audioSource.isPlaying)
        {
            return Mathf.Sin(Time.time * _fallbackFrequency * Mathf.PI * 2f) * _amplitude;
        }

        // ── Advance monotonic elapsed time ─────────────────────────────────
        float rawTime = _audioSource.time;
        float dt      = rawTime - _lastRawTime;

        if (dt < -(_clipLength * 0.5f))   // loop wrap detected
            dt += _clipLength;

        _lastRawTime  = rawTime;
        _elapsedTime += Mathf.Max(0f, dt);

        // ── Find surrounding peaks in absolute time ────────────────────────
        int   N          = _peakTimes.Length;
        float cycleTime  = _elapsedTime % _clipLength;
        int   fullCycles = Mathf.FloorToInt(_elapsedTime / _clipLength);

        float prevPeakAbs, nextPeakAbs;
        int   globalPeakIdx;

        if (cycleTime < _peakTimes[0])
        {
            prevPeakAbs   = (fullCycles - 1) * _clipLength + _peakTimes[N - 1];
            nextPeakAbs   = fullCycles        * _clipLength + _peakTimes[0];
            globalPeakIdx = fullCycles * N - 1;
        }
        else
        {
            int i = N - 1;
            for (int j = 0; j < N - 1; j++)
            {
                if (cycleTime < _peakTimes[j + 1]) { i = j; break; }
            }

            prevPeakAbs  = fullCycles * _clipLength + _peakTimes[i];
            nextPeakAbs  = (i < N - 1)
                ? fullCycles * _clipLength + _peakTimes[i + 1]
                : (fullCycles + 1) * _clipLength + _peakTimes[0];
            globalPeakIdx = fullCycles * N + i;
        }

        // ── Map to cosine angle ─────────────────────────────────────────────
        float dur   = nextPeakAbs - prevPeakAbs;
        float u     = dur > 0f ? Mathf.Clamp01((_elapsedTime - prevPeakAbs) / dur) : 0f;
        float phase = (globalPeakIdx + u) * Mathf.PI;
        return Mathf.Cos(phase) * _amplitude;
    }
}
