using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides which music track should currently play and crossfades between
/// tracks through the existing AudioManager. Systems request tracks by
/// referencing MusicTrack assets — the director compares requests and fades
/// to the newest one, restoring the previous track when a request is cleared.
/// Sources are pooled and paused instead of destroyed, so a track that already
/// played resumes from the position where it faded out.
/// Tracks requested with playOnce are remembered and never restarted.
/// </summary>
public class MusicDirector : MonoBehaviour {
    [Tooltip("Track played when the game starts. Leave empty to keep using AudioManager.PlayGameMusic.")]
    [SerializeField] private MusicTrack _defaultGameTrack;

    [Tooltip("Minimum time in seconds between music switches to avoid flickering on door triggers.")]
    [SerializeField] private float _minSwitchInterval = 2f;

    public static MusicDirector Instance { get; private set; }

    private MusicTrack _activeTrack;
    private MusicTrack _previousTrack;
    private Coroutine _fadeCoroutine;
    private float _lastSwitchTime;
    private AudioSource _activeSource;
    private Coroutine _eventPauseCoroutine;
    private bool _mutedForEvent;

    /// <summary>True while the director is silenced for a horror event.</summary>
    public static bool MutedForEvent => Instance != null && Instance._mutedForEvent;

    private readonly HashSet<MusicTrack> _playedOnceTracks = new HashSet<MusicTrack>();
    private readonly Dictionary<MusicTrack, AudioSource> _sourcePool = new Dictionary<MusicTrack, AudioSource>();

    /// <summary>
    /// The track that is currently playing (or pending fade-in).
    /// </summary>
    public MusicTrack ActiveTrack => _activeTrack;

    private void Awake() {
        if (Instance != null) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start() {
        if (_defaultGameTrack != null) {
            RequestTrackInternal(_defaultGameTrack, playOnce: false);
        }
    }

    /// <summary>
    /// Requests a track to play. If the same track is already active the request is ignored;
    /// otherwise the director crossfades to it and remembers the previous track for ClearTrack.
    /// Tracks already played with playOnce are skipped.
    /// </summary>
    public static void RequestTrack(MusicTrack track, bool playOnce = false) {
        if (Instance == null || track == null) return;
        if (playOnce && Instance._playedOnceTracks.Contains(track)) return;
        Instance.RequestTrackInternal(track, playOnce);
    }

    /// <summary>
    /// Restores the previously playing track after a temporary request ends.
    /// Safe to call with a track that is not currently active — the request is ignored.
    /// </summary>
    public static void ClearTrack(MusicTrack track) {
        if (Instance == null || track == null) return;
        Instance.ClearTrackInternal(track);
    }

    private void RequestTrackInternal(MusicTrack track, bool playOnce) {
        if (_mutedForEvent) return; // horror event owns the music — requests are queued out
        if (_activeTrack == track) return;
        if (Time.unscaledTime - _lastSwitchTime < _minSwitchInterval) return;

        if (playOnce) _playedOnceTracks.Add(track);

        _previousTrack = _activeTrack;
        _activeTrack = track;
        _lastSwitchTime = Time.unscaledTime;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(track));
    }

    /// <summary>
    /// Instantly silences EVERY director source (the whole pooled set, not just
    /// the active one — covers mid-crossfade states) and blocks new track
    /// requests until RestoreAfterEvent. Used by the horror event to own the
    /// soundscape for the duration of a track.
    /// </summary>
    public static void MuteForEvent() {
        if (Instance == null || Instance._mutedForEvent) return;
        Instance._mutedForEvent = true;

        if (Instance._fadeCoroutine != null) Instance.StopCoroutine(Instance._fadeCoroutine);
        if (Instance._eventPauseCoroutine != null) Instance.StopCoroutine(Instance._eventPauseCoroutine);

        // Mute and pause every pooled source — whatever is playing, it dies now
        foreach (KeyValuePair<MusicTrack, AudioSource> kvp in Instance._sourcePool) {
            if (kvp.Value == null) continue;
            kvp.Value.volume = 0f;
            kvp.Value.Pause();
        }
        if (Instance._activeSource != null) {
            Instance._activeSource.volume = 0f;
            Instance._activeSource.Pause();
        }
    }

    /// <summary>
    /// Returns the director to normal operation: resumes the track it was
    /// playing before MuteForEvent (from the same position).
    /// </summary>
    public static void RestoreAfterEvent() {
        if (Instance == null || !Instance._mutedForEvent) return;
        Instance._mutedForEvent = false;

        // Refade the track that was active before the event; FadeRoutine
        // un-pauses the pooled source and brings its volume back up.
        MusicTrack track = Instance._activeTrack;
        if (track != null)
            Instance._fadeCoroutine = Instance.StartCoroutine(Instance.FadeRoutine(track));
    }

    private void ClearTrackInternal(MusicTrack track) {
        if (_activeTrack != track) return;

        MusicTrack restoreTo = _previousTrack;
        _previousTrack = null;
        _activeTrack = restoreTo;
        _lastSwitchTime = Time.unscaledTime;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = restoreTo != null ? StartCoroutine(FadeRoutine(restoreTo)) : null;
    }

    private IEnumerator FadeRoutine(MusicTrack track) {
        float duration = track.FadeDuration < 0f ? _minSwitchInterval : track.FadeDuration;
        float targetVolume = track.Volume;
        AudioClip clip = track.Clip;

        AudioSource oldSource = _activeSource;
        AudioSource newSource;

        // Same clip is already playing — only refade its volume.
        if (oldSource != null && oldSource.clip == clip) {
            newSource = oldSource;
        } else {
            newSource = GetOrCreateSource(track);
            newSource.volume = 0f;
        }

        // Fade old source out and pause it so it can resume from the same spot later.
        float startOldVolume = oldSource != null && oldSource != newSource ? oldSource.volume : 0f;
        float startNewVolume = newSource.volume;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (oldSource != null && oldSource != newSource) oldSource.volume = Mathf.Lerp(startOldVolume, 0f, t);
            newSource.volume = Mathf.Lerp(startNewVolume, targetVolume, t);

            yield return null;
        }

        if (oldSource != null && oldSource != newSource) {
            oldSource.Pause();
            oldSource.volume = 0f;
        }
        newSource.volume = targetVolume;

        _activeSource = newSource;
        _fadeCoroutine = null;
    }

    /// <summary>
    /// Returns a pooled paused AudioSource for the track's clip, or creates a
    /// new one through the AudioManager. A paused source resumes where it
    /// faded out instead of restarting the clip.
    /// </summary>
    private AudioSource GetOrCreateSource(MusicTrack track) {
        if (_sourcePool.TryGetValue(track, out AudioSource pooled) && pooled != null) {
            pooled.UnPause();
            return pooled;
        }

        AudioSource created = AudioManager.Instance.PlayNewMusicSource(track.Clip, track.Loop);
        _sourcePool[track] = created;
        return created;
    }
}
