using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages ambient clock sounds and pendulum synchronization.
///
/// On start, analyzes the tick clip's waveform to find the actual amplitude
/// peak timestamps. Those timestamps are handed to <see cref="PendulumSwing"/>
/// so it interpolates its angle between real audio transients rather than
/// assuming a uniform loop duration.
///
/// Tick: single looping 3D AudioSource on the pendulum transform.
/// Chime: one-shot SFX via AudioManager on puzzle solve.
///
/// Subscribes directly to PuzzleModeController.OnSolved.
/// Attach on the same root GameObject as PuzzleModeController.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ClockAmbience : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Tick (looping)")]
    [Tooltip("Looping tick clip whose waveform peaks drive the pendulum timing.")]
    [SerializeField] private AudioClip _tickClip;

    [SerializeField, Range(0f, 1f)] private float _tickVolume = 0.8f;

    [SerializeField, Min(0f)] private float _tickMinDistance = 1f;
    [SerializeField, Min(0f)] private float _tickMaxDistance = 15f;

    [Header("Peak Detection")]
    [Tooltip("Normalized amplitude threshold (0–1). Raise if false peaks appear; lower if real peaks are missed.")]
    [SerializeField, Range(0.05f, 0.95f)] private float _peakThreshold = 0.35f;

    [Tooltip("Minimum time between two detected peaks in seconds. Should be slightly less than the shortest tick interval.")]
    [SerializeField, Min(0.05f)] private float _minPeakInterval = 0.15f;

    [Header("Solved Chime")]
    [Tooltip("One-shot chime played via AudioManager.PlaySFX when the puzzle is solved.")]
    [SerializeField] private AudioClip _chimeClip;

    [SerializeField, Range(0f, 1f)] private float _chimeVolume = 1f;

    // ── State ──────────────────────────────────────────────────────────────────

    private PuzzleModeController _puzzleMode;
    private PendulumSwing        _pendulum;
    private AudioSource          _tickSource;
    private bool                 _tickingActive;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        _puzzleMode.OnSolved += HandleSolved;

        _pendulum = GetComponentInChildren<PendulumSwing>();
    }

    private void Start()
    {
        if (_puzzleMode.IsSolved) return;
        StartTick();
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
            _puzzleMode.OnSolved -= HandleSolved;

        StopTick();
    }

    // ── Handlers ───────────────────────────────────────────────────────────────

    private void HandleSolved()
    {
        bool justSolvedNow = _tickingActive;
        StopTick();
        if (justSolvedNow) PlayChime();
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private void StartTick()
    {
        if (_tickClip == null)
        {
            Debug.LogWarning($"[{nameof(ClockAmbience)}] Tick clip is not assigned on {gameObject.name}.", this);
            return;
        }

        Transform parent = _pendulum != null ? _pendulum.transform : transform;
        var go           = new GameObject("ClockTickSource");
        go.transform.SetParent(parent, worldPositionStays: false);

        _tickSource              = go.AddComponent<AudioSource>();
        _tickSource.clip         = _tickClip;
        _tickSource.loop         = true;
        _tickSource.spatialBlend = 1f;
        _tickSource.minDistance  = _tickMinDistance;
        _tickSource.maxDistance  = _tickMaxDistance;
        _tickSource.rolloffMode  = AudioRolloffMode.Logarithmic;
        _tickSource.playOnAwake  = false;
        _tickSource.volume       = _tickVolume;
        _tickSource.Play();

        float[] peaks = AnalyzePeaks(_tickClip, _peakThreshold, _minPeakInterval);
        Debug.Log($"[{nameof(ClockAmbience)}] Detected {peaks.Length} peak(s) in '{_tickClip.name}'.");

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
            Debug.LogWarning($"[{nameof(ClockAmbience)}] Chime clip is not assigned on {gameObject.name}.", this);
            return;
        }

        AudioManager.Instance?.PlaySFX(_chimeClip, _chimeVolume);
    }

    // ── Waveform analysis ──────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="clip"/> sample data and returns timestamps (seconds)
    /// of amplitude spikes above <paramref name="threshold"/> (normalized 0–1),
    /// spaced at least <paramref name="minIntervalSec"/> apart.
    /// </summary>
    private static float[] AnalyzePeaks(AudioClip clip, float threshold, float minIntervalSec)
    {
        int channels    = clip.channels;
        int sampleCount = clip.samples;
        int sampleRate  = clip.frequency;

        var raw = new float[sampleCount * channels];
        if (!clip.GetData(raw, 0))
        {
            Debug.LogWarning($"[{nameof(ClockAmbience)}] Could not read samples from '{clip.name}'. " +
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
}
