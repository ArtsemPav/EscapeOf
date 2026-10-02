using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// UV pentagram horror sequence.
///
/// Phases:
///   1. Spawn   — CatHorror prefab is instantiated at this object's position.
///                Cat, particles and lights start hidden; only the pentagram
///                (HiddenWallSign, UV mode) is visible through the UV flashlight.
///   2. Watch   — accumulates look time while the player stares at the pentagram.
///   3. Shrink  — pentagram quickly scales down and hides.
///   4. Play    — cat, particles and lights activate, music starts.
///                Beat events (OnBeat) fire at BPM; timeline UnityEvents fire
///                at their configured track time for escalating weirdness.
///   5. Finish  — when the track ends everything is restored and the instance
///                is removed (pentagram and cat are gone for good).
///
/// Debug: press the debug key in Play Mode to run the whole sequence instantly
/// (skips the look-wait). Useful while the cat animation is being tuned.
/// </summary>
public class UVPentagramSequence : MonoBehaviour
{
    /// <summary>Fired on every music beat while the sequence is playing.</summary>
    public static event Action<int> OnBeat;

    /// <summary>True while the music (and the whole effect escalation) is running.</summary>
    public static bool MusicPlaying { get; private set; }

    /// <summary>The currently playing music source (or last one). Exposed for effect components.</summary>
    public static AudioSource InstanceMusicSource { get; private set; }

    private enum Phase { Idle, Watching, Shrinking, Playing, Finished }

    [Header("References")]
    [Tooltip("CatHorror instance placed in the scene at the pentagram location. " +
             "Must contain children: Pentagram, CatAnimation, Particles, Lights, Audio.")]
    [SerializeField] private GameObject _catHorrorInstance;

    [Tooltip("Player camera for look detection. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Flashlight controller. Auto-found in the scene if left empty. Look counting only " +
             "runs while the flashlight is ON and in UV mode — same visibility as the pentagram.")]
    [SerializeField] private FlashlightController _flashlight;

    [Header("Look Detection")]
    [Tooltip("Dot product above which the player counts as looking at the pentagram. 0.7 ≈ 45°.")]
    [SerializeField] private float _lookAtThreshold = 0.7f;

    [Tooltip("Seconds of continuous looking required to start the sequence.")]
    [SerializeField] private float _lookSecondsRequired = 3f;

    [Header("Pentagram Shrink")]
    [SerializeField] private float _shrinkDuration = 0.35f;

    [Tooltip("Scale over time. X: 0 → shrink start, 1 → shrink end. Y: scale multiplier.")]
    [SerializeField] private AnimationCurve _shrinkCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Pentagram Spin (watch phase)")]
    [Tooltip("Rotation speed in degrees per second while waiting for the player to look.")]
    [SerializeField] private float _spinSpeed = 60f;

    [Tooltip("Set true to spin clockwise around Y, false for counter-clockwise.")]
    [SerializeField] private bool _spinClockwise = true;

    [Header("Music")]
    [Tooltip("BPM used to fire OnBeat. Match the track tempo.")]
    [SerializeField, Range(30f, 300f)] private float _bpm = 128f;

    [Tooltip("Track time in seconds when the particle systems activate (0 = together with the cat).")]
    [SerializeField] private float _particlesStartTime = 20f;

    [Tooltip("Seconds before the track ends when all visuals (cat, lights, particles, pillars) " +
             "shut off, leaving only the music. 0 = off visuals only at the very end.")]
    [SerializeField, Min(0f)] private float _effectsCutoffBeforeEnd = 1.5f;

    [Header("Timeline — escalating weirdness")]
    [Tooltip("Fires each UnityEvent once when the track reaches its time (seconds).")]
    [SerializeField] private TimelineEntry[] _timeline;

    [Header("Events")]
    [Tooltip("Fired when the music starts (cat visible, first beat).")]
    [SerializeField] private UnityEvent _onMusicStarted;

    [Tooltip("Fired when the track finishes. Restore room colors/lighting here if the timeline changed them.")]
    [SerializeField] private UnityEvent _onMusicFinished;

    [Header("Debug")]
    [Tooltip("Play Mode hotkey that instantly runs the full sequence (skips look-wait).")]
    [SerializeField] private Key _debugKey = Key.O;

    [Serializable]
    public class TimelineEntry
    {
        [Tooltip("Track time in seconds when this event fires.")]
        public float time;

        [Tooltip("Actions to invoke at this track time (lights, colors, vfx, sounds...).")]
        public UnityEvent onTime;
    }

    // Child names inside the CatHorror scene instance
    private const string PentagramNode = "Pentagram";
    private const string AudioNode = "Audio";
    private const string CatAnimationNode = "CatAnimation";
    private const string ParticlesNode = "Particles";
    private const string LightPilarsNode = "LightPilars";

    // Hidden at spawn, activated when the music starts
    private static readonly string[] HiddenUntilMusic = { CatAnimationNode, "Lights", ParticlesNode, LightPilarsNode };

    // Hidden at spawn, activated at the particles timeline moment (climax chaos)
    private static readonly string[] HiddenUntilParticles = { ParticlesNode, LightPilarsNode };

    private Phase _phase = Phase.Idle;
    private Transform _pentagram;
    private readonly List<GameObject> _particleRoots = new();
    private AudioSource _audio;
    private Vector3 _pentagramBaseScale;
    private float _lookTimer;
    private int _nextTimelineIndex;
    private double _nextBeatTime;
    private int _beatIndex;
    private bool _particlesStarted;
    private bool _effectsCutoffDone;
    private float _debugLogTimer;

    private void Start()
    {
        if (_playerCamera == null)
            _playerCamera = Camera.main;

        Debug.Log($"[UVPentagramSequence] Start. Camera={(_playerCamera != null ? _playerCamera.name : "NULL")}", this);
        SpawnInstance();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[_debugKey].wasPressedThisFrame)
        {
            DebugPlay();
            return;
        }

        if (_phase == Phase.Watching)
        {
            SpinPentagram();
            AccumulateLookTime();
        }
        else if (_phase == Phase.Playing)
            UpdateMusicTimeline();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Runs the full sequence skipping the look-wait. Debug hotkey or manual call.</summary>
    public void DebugPlay()
    {
        if (_phase != Phase.Watching && _phase != Phase.Idle) return;
        StartCoroutine(ShrinkAndPlay());
    }

    // ── Spawn / look detection ────────────────────────────────────────────────

    private void SpawnInstance()
    {
        if (_catHorrorInstance == null)
        {
            Debug.LogWarning($"[{name}] CatHorror scene instance is not assigned.", this);
            return;
        }

        _pentagram = FindChildRecursive(_catHorrorInstance.transform, PentagramNode);
        if (_pentagram == null)
            Debug.LogWarning($"[{name}] Child '{PentagramNode}' not found on the instance.", this);
        else
            _pentagramBaseScale = _pentagram.localScale;

        Transform audioNode = FindChildRecursive(_catHorrorInstance.transform, AudioNode);
        _audio = audioNode != null ? audioNode.GetComponent<AudioSource>() : _catHorrorInstance.GetComponent<AudioSource>();
        if (_audio == null)
            Debug.LogWarning($"[{name}] No AudioSource found under '{AudioNode}'.", this);

        // Everything except the pentagram stays hidden until the music starts.
        foreach (string childName in HiddenUntilMusic)
        {
            Transform child = FindChildRecursive(_catHorrorInstance.transform, childName);
            if (child != null) child.gameObject.SetActive(false);
            else Debug.LogWarning($"[{name}] Child '{childName}' not found on the instance.", this);
        }

        // Root Animator always animates regardless of child state — the cat
        // would be visible/animated before the music starts. Disable until then.
        Animator rootAnimator = _catHorrorInstance.GetComponent<Animator>();
        if (rootAnimator != null) rootAnimator.enabled = false;

        _phase = Phase.Watching;
    }

    private void SpinPentagram()
    {
        if (_pentagram == null) return;
        float direction = _spinClockwise ? -1f : 1f;
        _pentagram.Rotate(0f, 0f, direction * _spinSpeed * Time.deltaTime, Space.Self);
    }

    private void AccumulateLookTime()
    {
        if (_pentagram == null || _playerCamera == null) return;

        // Only count looking when the pentagram can actually be seen:
        // the UV flashlight must be ON and in UV mode (HiddenWallSign logic).
        if (_flashlight == null)
            _flashlight = FindFirstObjectByType<FlashlightController>();
        if (_flashlight == null || !_flashlight.IsOn || _flashlight.CurrentMode != FlashlightMode.UV)
        {
            _lookTimer = 0f;
            return;
        }

        Vector3 toTarget = (_pentagram.position - _playerCamera.transform.position).normalized;
        float dot = Vector3.Dot(_playerCamera.transform.forward, toTarget);

        // Debug telemetry — once per second, to verify look detection in Play Mode
        _debugLogTimer += Time.deltaTime;
        if (_debugLogTimer >= 1f)
        {
            _debugLogTimer = 0f;
            Debug.Log($"[UVPentagramSequence] dot={dot:F2} (need >= {_lookAtThreshold:F2}), lookTimer={_lookTimer:F1}s (need {_lookSecondsRequired:F1}s), pentagramPos={_pentagram.position}, camPos={_playerCamera.transform.position}", this);
        }

        if (dot >= _lookAtThreshold)
        {
            _lookTimer += Time.deltaTime;
            if (_lookTimer >= _lookSecondsRequired)
                StartCoroutine(ShrinkAndPlay());
        }
        else
        {
            // Optional: decay instead of instant reset feels more forgiving
            _lookTimer = Mathf.Max(0f, _lookTimer - Time.deltaTime * 2f);
        }
    }

    // ── Shrink → play ─────────────────────────────────────────────────────────

    private IEnumerator ShrinkAndPlay()
    {
        if (_phase == Phase.Shrinking || _phase == Phase.Playing) yield break;
        _phase = Phase.Shrinking;

        // Pentagram shrinks rapidly and disappears
        if (_pentagram != null)
        {
            float t = 0f;
            while (t < _shrinkDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _shrinkDuration);
                _pentagram.localScale = _pentagramBaseScale * _shrinkCurve.Evaluate(k);
                yield return null;
            }
            _pentagram.gameObject.SetActive(false);
        }

        // Cat comes alive; particles and pillars are excluded — they start on the timeline
        foreach (string childName in HiddenUntilMusic)
        {
            if (Array.IndexOf(HiddenUntilParticles, childName) >= 0) continue;
            Transform child = FindChildRecursive(_catHorrorInstance.transform, childName);
            if (child != null) child.gameObject.SetActive(true);
        }

        // Remember particle/pillar roots to activate at _particlesStartTime
        _particleRoots.Clear();
        foreach (string childName in HiddenUntilParticles)
        {
            Transform child = FindChildRecursive(_catHorrorInstance.transform, childName);
            if (child != null) _particleRoots.Add(child.gameObject);
            else Debug.LogWarning($"[{name}] Child '{childName}' not found — won't start at climax.", this);
        }

        Animator rootAnimator = _catHorrorInstance != null ? _catHorrorInstance.GetComponent<Animator>() : null;
        if (rootAnimator != null)
        {
            rootAnimator.enabled = true;
            // Debug: report what the Animator is driving and whether renderers exist under it
            Transform catAnim = FindChildRecursive(_catHorrorInstance.transform, CatAnimationNode);
            if (catAnim != null)
            {
                var renderers = catAnim.GetComponentsInChildren<Renderer>(false);
                Debug.Log($"[UVPentagramSequence] Cat activated. Animator '{rootAnimator.runtimeAnimatorController?.name ?? "NULL"}' enabled, " +
                          $"CatAnimation renderers: {renderers.Length}" +
                          (renderers.Length == 0 ? " — NOTHING TO RENDER, check the cat model" : ""), this);
            }
            else
            {
                Debug.Log($"[UVPentagramSequence] Cat activated, but no '{CatAnimationNode}' child found.", this);
            }
        }

        StartMusic();
    }

    // ── Music / beats / timeline ──────────────────────────────────────────────

    private void StartMusic()
    {
        _phase = Phase.Playing;
        MusicPlaying = true;
        _beatIndex = 0;
        _nextTimelineIndex = 0;
        _nextBeatTime = 0.0;
        _particlesStarted = _particlesStartTime <= 0f;
        _effectsCutoffDone = false;

        // Silence the game's background music and any MusicDirector tracks
        // (room 7 melody etc.) for the duration of the track
        if (AudioManager.Instance != null)
            AudioManager.Instance.MuteBackground();
        MusicDirector.MuteForEvent();

        if (_audio != null)
        {
            _audio.spatialBlend = 0f; // 2D — sound is heard everywhere at full volume
            _audio.Play();
            Debug.Log($"[UVPentagramSequence] Music started. Clip length: {(_audio.clip != null ? _audio.clip.length.ToString("F1") + "s" : "NULL")}", this);
        }

        InstanceMusicSource = _audio;

        _onMusicStarted?.Invoke();
    }

    private void UpdateMusicTimeline()
    {
        if (_audio == null || !_audio.isPlaying)
        {
            FinishSequence();
            return;
        }

        double trackTime = _audio.time;
        double trackLength = _audio.clip != null ? _audio.clip.length : 0.0;

        // Fade-out window: shortly before the track ends, kill all visuals
        // (cat, particles, pillars, event lights) — only the music keeps playing.
        if (_effectsCutoffBeforeEnd > 0f && !_effectsCutoffDone &&
            trackLength > 0.0 && trackTime >= trackLength - _effectsCutoffBeforeEnd)
        {
            _effectsCutoffDone = true;
            CutoffEffects();
            Debug.Log($"[UVPentagramSequence] Effects cutoff at {trackTime:F1}s ({_effectsCutoffBeforeEnd:F1}s before the end).", this);
        }

        double beatInterval = 60.0 / _bpm;

        while (trackTime >= _nextBeatTime)
        {
            OnBeat?.Invoke(_beatIndex++);
            _nextBeatTime += beatInterval;
        }

        while (_nextTimelineIndex < _timeline.Length && _timeline[_nextTimelineIndex].time <= trackTime)
        {
            _timeline[_nextTimelineIndex].onTime?.Invoke();
            _nextTimelineIndex++;
        }

        // Built-in timeline step: particles + light pillars activate at the configured track time
        if (!_particlesStarted && trackTime >= _particlesStartTime)
        {
            _particlesStarted = true;
            foreach (GameObject root in _particleRoots)
                root.SetActive(true);
            Debug.Log($"[UVPentagramSequence] Particles + light pillars activated at {trackTime:F1}s.", this);
        }
    }

    /// <summary>Hides all event visuals while the last seconds of the track play out.</summary>
    private void CutoffEffects()
    {
        if (_catHorrorInstance == null) return;

        // Hide the whole instance: cat, pentagram remnants, particles, pillars
        foreach (string childName in HiddenUntilMusic)
        {
            Transform child = FindChildRecursive(_catHorrorInstance.transform, childName);
            if (child != null) child.gameObject.SetActive(false);
        }
        foreach (GameObject root in _particleRoots)
            if (root != null) root.SetActive(false);

        if (_catHorrorInstance.GetComponent<Animator>() is { } animator)
            animator.enabled = false;

        MusicPlaying = false; // beat drivers (lights, pillars) go idle too
    }

    private void FinishSequence()
    {
        _phase = Phase.Finished;
        MusicPlaying = false;

        // Bring the game's background music back
        if (AudioManager.Instance != null)
            AudioManager.Instance.UnmuteBackground();
        MusicDirector.RestoreAfterEvent();

        _onMusicFinished?.Invoke();

        // Pentagram and cat are gone for good — hide the whole instance.
        if (_catHorrorInstance != null)
            _catHorrorInstance.SetActive(false);

        _pentagram = null;
        _audio = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent.name == childName) return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildRecursive(child, childName);
            if (result != null) return result;
        }
        return null;
    }
}
