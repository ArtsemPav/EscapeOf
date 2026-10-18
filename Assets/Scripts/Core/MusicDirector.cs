using System;
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
/// Implements ISaveable: persists which play-once tracks already fired and
/// which one is currently dominant, so a reloaded session restarts the
/// dominant track and the fired zones never re-trigger.
/// The director also owns the menu music: the persistent instance lives in
/// Start.unity, plays Menu_Main there, and picks up the gameplay default
/// track in Game.unity (its scene duplicate adopts it in Awake).
/// </summary>
public class MusicDirector : MonoBehaviour, ISaveable {
    [Tooltip("Track played when the game starts.")]
    [SerializeField] private MusicTrack _defaultGameTrack;

    [Tooltip("Track played while the game is paused (pause menu). Restored gameplay music resumes after.")]
    [SerializeField] private MusicTrack _pauseMenuTrack;

    [Tooltip("Mixer group all music sources are routed to (the Music bus).")]
    [SerializeField] private UnityEngine.Audio.AudioMixerGroup _musicGroup;

    [Tooltip("Minimum time in seconds between music switches to avoid flickering on door triggers.")]
    [SerializeField] private float _minSwitchInterval = 2f;

    public static MusicDirector Instance { get; private set; }

    private MusicTrack _activeTrack;
    private MusicTrack _previousTrack;
    private Coroutine _fadeCoroutine;
    // -999 so the very first track request is never rejected by _minSwitchInterval
    private float _lastSwitchTime = -999f;
    private AudioSource _activeSource;
    private int _muteOwnerCount; // nested horror events — restore fires only when the last owner releases
    private Coroutine _muteCoroutine;

    /// <summary>True while the director is silenced for a horror event.</summary>
    public static bool MutedForEvent => Instance != null && Instance._muteOwnerCount > 0;

    private readonly HashSet<string> _playedOnceTrackNames = new HashSet<string>();
    private readonly Dictionary<MusicTrack, AudioSource> _sourcePool = new Dictionary<MusicTrack, AudioSource>();
    // All MusicTrack assets seen this session (registered by zones and Start).
    // Static so zones can register in Awake() before the director's Instance exists.
    private static readonly Dictionary<string, MusicTrack> KnownTracks = new Dictionary<string, MusicTrack>();
    private string _loadedDominantTrackName; // set by LoadSaveData, applied in Start
    private float _pendingResumeTime; // set by LoadSaveData, moved to _resumeTime in Start
    private float _resumeTime; // playback position the next created source starts from
    private Coroutine _duckCoroutine;
    private float _preDuckVolume = -1f; // volume the active source played at before the last duck

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "music_director";

    /// <summary>Serializes the dominant track, its playback position and the list of already-fired play-once tracks.
    /// While the pause menu owns the music, the gameplay track that the menu interrupted is saved instead —
    /// the autosave timer runs on unscaled time and must never persist the menu track as dominant.</summary>
    public string GetSaveData() {
        MusicTrack dominant;
        float dominantTime;
        if (_menuMusicActive) {
            dominant = _preMenuTrack;
            AudioSource preMenuSource = dominant != null && _sourcePool.TryGetValue(dominant, out AudioSource pooled) ? pooled : null;
            dominantTime = preMenuSource != null ? preMenuSource.time : 0f;
        } else {
            dominant = _activeTrack != _pauseMenuTrack ? _activeTrack : null;
            dominantTime = dominant != null && _activeSource != null && _activeSource.clip == dominant.Clip ? _activeSource.time : 0f;
        }
        if (dominant == null) dominant = _defaultGameTrack;

        return JsonUtility.ToJson(new MusicDirectorSaveData
        {
            dominantTrack = dominant != null ? dominant.name : null,
            dominantTrackTime = dominantTime,
            playedOnceTracks = new List<string>(_playedOnceTrackNames),
        });
    }

    /// <summary>
    /// Restores which play-once tracks already fired and which track was
    /// dominant, including where it stopped. The dominant track is applied in
    /// Start() — it restarts from the saved position.
    /// </summary>
    public void LoadSaveData(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        MusicDirectorSaveData data = JsonUtility.FromJson<MusicDirectorSaveData>(json);
        _playedOnceTrackNames.Clear();
        if (data.playedOnceTracks != null) {
            foreach (string trackName in data.playedOnceTracks) _playedOnceTrackNames.Add(trackName);
        }
        _loadedDominantTrackName = data.dominantTrack;
        _pendingResumeTime = Mathf.Max(0f, data.dominantTrackTime);
    }

    [Serializable]
    private class MusicDirectorSaveData
    {
        public string dominantTrack;
        public float dominantTrackTime;
        public List<string> playedOnceTracks = new List<string>();
    }

    /// <summary>
    /// The track that is currently playing (or pending fade-in).
    /// </summary>
    public MusicTrack ActiveTrack => _activeTrack;

    // Menu (pause) music state — the director owns ALL music, including the menu.
    private bool _menuMusicActive;
    private MusicTrack _preMenuTrack; // gameplay track the menu interrupted (never the menu track itself)
    private float _menuResumeTime;
    private Coroutine _menuResumeCoroutine;

    /// <summary>
    /// Switches to the pause menu track, remembering the gameplay track's
    /// playback position. Replaces the legacy AudioManager.PlayMenuMusic.
    /// </summary>
    public static void PlayMenuMusic() {
        if (Instance == null || Instance._pauseMenuTrack == null) return;
        // Plays even during a horror event: while paused the listener is muted
        // (AudioListener.pause) and the menu track is the ONLY audible sound.

        Instance._menuMusicActive = true;

        // Remember where the gameplay track stopped so it resumes from the same spot.
        if (Instance._activeSource != null && Instance._activeTrack != null
            && Instance._activeTrack != Instance._pauseMenuTrack
            && Instance._activeSource.clip == Instance._activeTrack.Clip) {
            Instance._menuResumeTime = Instance._activeSource.time;
            Instance._preMenuTrack = Instance._activeTrack;
        } else if (Instance._preMenuTrack == null) {
            // Menu opened with no gameplay music (main menu) — nothing to restore.
            Instance._preMenuTrack = null;
        }

        if (Instance._fadeCoroutine != null) Instance.StopCoroutine(Instance._fadeCoroutine);
        if (Instance._duckCoroutine != null) { Instance.StopCoroutine(Instance._duckCoroutine); Instance._duckCoroutine = null; }
        Instance._preDuckVolume = -1f; // the duck is abandoned — the restored track plays at its own level
        Instance._activeTrack = Instance._pauseMenuTrack;
        Instance._fadeCoroutine = Instance.StartCoroutine(Instance.FadeRoutine(Instance._pauseMenuTrack));
    }

    /// <summary>
    /// Restores the gameplay music after the pause menu closes, resuming the
    /// gameplay track from the position where the menu music took over.
    /// When no gameplay track is active (main menu → gameplay transition),
    /// starts the scene's default track.
    /// </summary>
    public static void PlayGameMusic() {
        if (Instance == null) return;

        Instance._menuMusicActive = false;
        // Closing the menu during an active horror event: the event still owns
        // the soundscape — stay silent until RestoreAfterEvent refades the track.
        if (MutedForEvent) return;

        MusicTrack gameplayTrack = Instance._preMenuTrack != null
            ? Instance._preMenuTrack
            : (Instance._previousTrack != null && Instance._previousTrack != Instance._pauseMenuTrack
                ? Instance._previousTrack
                : Instance._defaultGameTrack);
        if (gameplayTrack == null) return;

        if (Instance._menuResumeCoroutine != null) Instance.StopCoroutine(Instance._menuResumeCoroutine);
        if (Instance._fadeCoroutine != null) Instance.StopCoroutine(Instance._fadeCoroutine);
        Instance._activeTrack = gameplayTrack;
        Instance._menuResumeCoroutine = Instance.StartCoroutine(Instance.ResumeGameplayRoutine(gameplayTrack));
    }

    private IEnumerator ResumeGameplayRoutine(MusicTrack track) {
        // Pause every other pooled source (menu track included) while gameplay resumes.
        foreach (KeyValuePair<MusicTrack, AudioSource> pooled in _sourcePool) {
            if (pooled.Value == null || pooled.Key == track) continue;
            pooled.Value.volume = 0f;
            pooled.Value.Pause();
        }

        // Resume the gameplay track from where the menu interrupted it.
        if (_sourcePool.TryGetValue(track, out AudioSource source) && source != null && _menuResumeTime > 0f) {
            source.time = Mathf.Min(_menuResumeTime, Mathf.Max(0f, track.Clip != null ? track.Clip.length : 0f));
        }
        _menuResumeTime = 0f;
        _preMenuTrack = null;

        yield return FadeRoutine(track);
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            // A newly loaded scene brings its own director as a config carrier —
            // adopt its default game track, then drop the duplicate component.
            if (_defaultGameTrack != null) Instance._defaultGameTrack = _defaultGameTrack;
            Destroy(this);
            return;
        }
        Instance = this;
        RegisterWithSaveManager();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) {
        // The persistent director must join the SaveManager of the gameplay scene.
        RegisterWithSaveManager();
    }

    private void RegisterWithSaveManager() {
        if (_registeredWithSaveManager || SaveManager.Instance == null) return;
        SaveManager.Instance.Register(this);
        _registeredWithSaveManager = true;
    }

    private void OnDestroy() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this && _registeredWithSaveManager) SaveManager.Instance?.Unregister(this);
    }

    private bool _registeredWithSaveManager;

    private void Start() {
        // A restored dominant track wins over the scene default: the zone the
        // player last entered stays the "main" melody for the loaded session,
        // resuming from the position saved at the last checkpoint.
        MusicTrack dominant = ResolveTrack(_loadedDominantTrackName);
        if (dominant == null) dominant = _defaultGameTrack;
        if (dominant != null) {
            RegisterTrack(dominant);
            _resumeTime = _pendingResumeTime;
            _pendingResumeTime = 0f;
            RequestTrackInternal(dominant, playOnce: false);
            // FadeRoutine consumed the position on its first frame — don't
            // re-apply it to later source requests in this session.
            _resumeTime = 0f;
        }
        // Menu scenes have no default game track — the pause menu track serves as menu music.
        else if (_pauseMenuTrack != null) {
            _menuMusicActive = true;
            _activeTrack = _pauseMenuTrack;
            _fadeCoroutine = StartCoroutine(FadeRoutine(_pauseMenuTrack));
        }
    }

    /// <summary>
    /// Finds a track asset by name among the ones registered this session.
    /// Returns null when no zone has registered it (unknown save entry).
    /// </summary>
    private static MusicTrack ResolveTrack(string trackName) {
        if (string.IsNullOrEmpty(trackName)) return null;
        return KnownTracks.TryGetValue(trackName, out MusicTrack track) ? track : null;
    }

    /// <summary>Remembers a track asset so save data can resolve it by name later. Called by zones in Awake.</summary>
    public static void RegisterTrack(MusicTrack track) {
        if (track != null) KnownTracks[track.name] = track;
    }

    private void Update() {
        // A non-looping track that stopped playing has finished — restore the
        // previous track. Explicitly skip the paused-for-menu and muted-for-event
        // states: a paused source also reports isPlaying == false.
        if (_activeSource == null || _activeTrack == null || _activeTrack.Loop) return;
        if (_fadeCoroutine != null || MutedForEvent) return;
        if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;

        if (!_activeSource.isPlaying)
            ClearTrackInternal(_activeTrack);
    }

    /// <summary>
    /// Requests a track to play. If the same track is already active the request is ignored;
    /// otherwise the director crossfades to it and remembers the previous track for ClearTrack.
    /// Tracks already played with playOnce are skipped.
    /// Returns true when the request was accepted (the track is now the dominant one).
    /// </summary>
    public static bool RequestTrack(MusicTrack track, bool playOnce = false) {
        if (Instance == null || track == null) return false;
        RegisterTrack(track);
        if (playOnce && Instance._playedOnceTrackNames.Contains(track.name)) return false;
        return Instance.RequestTrackInternal(track, playOnce);
    }

    /// <summary>
    /// Restores the previously playing track after a temporary request ends.
    /// Safe to call with a track that is not currently active — the request is ignored.
    /// </summary>
    public static void ClearTrack(MusicTrack track) {
        if (Instance == null || track == null) return;
        Instance.ClearTrackInternal(track);
    }

    private bool RequestTrackInternal(MusicTrack track, bool playOnce) {
        if (MutedForEvent) return false; // horror event owns the music — requests are queued out
        if (_activeTrack == track) return true;
        if (Time.unscaledTime - _lastSwitchTime < _minSwitchInterval) return false;

        if (playOnce) _playedOnceTrackNames.Add(track.name);

        _previousTrack = _activeTrack;
        _activeTrack = track;
        _lastSwitchTime = Time.unscaledTime;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        if (_duckCoroutine != null) StopCoroutine(_duckCoroutine); // its volume writes would fight FadeRoutine
        _duckCoroutine = null; // track switch cancels any active duck
        _fadeCoroutine = StartCoroutine(FadeRoutine(track));
        return true;
    }

    /// <summary>
    /// Instantly silences EVERY director source (the whole pooled set, not just
    /// the active one — covers mid-crossfade states) and blocks new track
    /// requests until RestoreAfterEvent. Used by the horror event to own the
    /// soundscape for the duration of a track.
    /// </summary>
    public static void MuteForEvent() => MuteForEvent(0f);

    /// <summary>
    /// Silences every director source over fadeDuration seconds and blocks new
    /// track requests until RestoreAfterEvent. Pass 0 for an instant cut.
    /// Requests are blocked immediately (before the fade finishes).
    /// </summary>
    public static void MuteForEvent(float fadeDuration) {
        if (Instance == null) return;
        Instance._muteOwnerCount++; // nested events stack — each owner must release individually

        if (Instance._fadeCoroutine != null) Instance.StopCoroutine(Instance._fadeCoroutine);
        if (Instance._duckCoroutine != null) { Instance.StopCoroutine(Instance._duckCoroutine); Instance._duckCoroutine = null; }
        Instance._preDuckVolume = -1f; // the duck is abandoned — restore plays at full level

        if (fadeDuration <= 0f) {
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
            return;
        }

        Instance._muteCoroutine = Instance.StartCoroutine(Instance.MuteRoutine(fadeDuration));
    }

    private IEnumerator MuteRoutine(float duration) {
        // Snapshot the sources and their starting volumes — the pool can contain
        // leftovers from past crossfades, all of them must die together.
        List<AudioSource> sources = new List<AudioSource>();
        List<float> startVolumes = new List<float>();
        foreach (KeyValuePair<MusicTrack, AudioSource> kvp in _sourcePool) {
            if (kvp.Value == null) continue;
            sources.Add(kvp.Value);
            startVolumes.Add(kvp.Value.volume);
        }

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < sources.Count; i++)
                if (sources[i] != null)
                    sources[i].volume = Mathf.Lerp(startVolumes[i], 0f, t);
            yield return null;
        }

        for (int i = 0; i < sources.Count; i++) {
            if (sources[i] == null) continue;
            sources[i].volume = 0f;
            sources[i].Pause(); // paused sources resume from the same position later
        }
        _muteCoroutine = null;
    }

    /// <summary>
    /// Returns the director to normal operation: resumes the track it was
    /// playing before MuteForEvent (from the same position).
    /// </summary>
    public static void RestoreAfterEvent() => RestoreAfterEvent(-1f);

    /// <summary>
    /// Same as RestoreAfterEvent() but with an explicit fade-in duration in seconds.
    /// Pass -1 to use the track's own fade duration.
    /// </summary>
    public static void RestoreAfterEvent(float fadeDurationOverride) {
        if (Instance == null || Instance._muteOwnerCount <= 0) return;
        Instance._muteOwnerCount--;
        if (Instance._muteOwnerCount > 0) return; // another horror event still owns the soundscape

        // An unfinished mute fade must stop — its volume writes would fight the restore fade
        if (Instance._muteCoroutine != null) {
            Instance.StopCoroutine(Instance._muteCoroutine);
            Instance._muteCoroutine = null;
        }
        Instance._preDuckVolume = -1f; // a duck interrupted by the event must not restore a stale level

        // The menu owns the music — nothing to restore until the menu closes.
        if (Instance._menuMusicActive) return;

        // Refade the track that was active before the event; FadeRoutine
        // un-pauses the pooled source and brings its volume back up.
        MusicTrack track = Instance._activeTrack;
        if (track != null)
            Instance._fadeCoroutine = Instance.StartCoroutine(Instance.FadeRoutine(track, fadeDurationOverride));
    }

    /// <summary>
    /// Fades the active director track to a target volume multiplier and back.
    /// Used to duck the music while the player is inside the elevator.
    /// If no track is active this is a no-op. A track switch while ducked
    /// cancels the duck — the new track plays at full volume.
    /// </summary>
    public static void DuckActiveTrack(float targetMultiplier, float fadeDuration) {
        if (Instance == null) return;
        Instance.DuckActiveTrackInternal(targetMultiplier, fadeDuration);
    }

    private void DuckActiveTrackInternal(float targetMultiplier, float fadeDuration) {
        // During a horror event (or while the menu owns the music) the elevator
        // must not fight the mute: duck and restore are both ignored.
        if (_muteOwnerCount > 0 || _menuMusicActive) return;

        if (_duckCoroutine != null) StopCoroutine(_duckCoroutine);

        AudioSource source = _activeSource;
        if (source == null) return; // nothing playing — nothing to duck

        // Restore to the exact level the music played at before the duck,
        // not to _activeTrack.Volume — _activeTrack can be null after
        // ClearTrack (e.g. MusicZone exit) while the source keeps playing.
        if (targetMultiplier < 1f) {
            // Ducking down — remember the level the music was playing at
            _preDuckVolume = source.volume;
        } else if (_preDuckVolume < 0f) {
            _preDuckVolume = source.volume; // restore without a prior duck — keep current level
        }

        float toVolume = targetMultiplier < 1f
            ? _preDuckVolume * Mathf.Clamp01(targetMultiplier)
            : _preDuckVolume;

        _duckCoroutine = StartCoroutine(DuckRoutine(source, source.volume, toVolume, fadeDuration));

        if (targetMultiplier >= 1f)
            _preDuckVolume = -1f; // restored — forget the remembered level
    }

    private IEnumerator DuckRoutine(AudioSource source, float fromVolume, float toVolume, float duration) {
        float elapsed = 0f;
        while (elapsed < duration && source != null) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(fromVolume, toVolume, t);
            yield return null;
        }

        if (source != null) source.volume = toVolume;
        _duckCoroutine = null;
    }

    private void ClearTrackInternal(MusicTrack track) {
        if (_activeTrack != track) return;

        MusicTrack restoreTo = _previousTrack;
        _previousTrack = null;
        _activeTrack = restoreTo;
        _lastSwitchTime = Time.unscaledTime;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        if (_duckCoroutine != null) StopCoroutine(_duckCoroutine); // its volume writes would fight FadeRoutine
        _duckCoroutine = null; // track switch cancels any active duck
        if (restoreTo != null) {
            _fadeCoroutine = StartCoroutine(FadeRoutine(restoreTo));
        } else {
            // Nothing to restore (the cleared zone track was the first one the
            // director played) — fade the active source out and pause it so it
            // doesn't keep playing ownerless. _activeSource keeps pointing at
            // it so a later track request can crossfade away from it properly.
            _fadeCoroutine = StartCoroutine(FadeOutAndPauseRoutine(_activeSource));
        }
    }

    private IEnumerator FadeOutAndPauseRoutine(AudioSource source) {
        if (source == null) yield break;
        float from = source.volume;
        float elapsed = 0f;
        while (elapsed < _minSwitchInterval && source != null) {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(from, 0f, Mathf.Clamp01(elapsed / _minSwitchInterval));
            yield return null;
        }
        if (source != null) {
            source.volume = 0f;
            source.Pause();
        }
    }

    private IEnumerator FadeRoutine(MusicTrack track) => FadeRoutine(track, -1f);

    private IEnumerator FadeRoutine(MusicTrack track, float fadeDurationOverride) {
        float duration = fadeDurationOverride >= 0f
            ? fadeDurationOverride
            : (track.FadeDuration < 0f ? _minSwitchInterval : track.FadeDuration);
        float targetVolume = track.Volume;
        AudioClip clip = track.Clip;

        AudioSource oldSource = _activeSource;
        AudioSource newSource;

        // Same clip is already assigned — refade its volume (and resume it if
        // it was paused, e.g. after PauseForMenu or a cleared ownerless track).
        if (oldSource != null && oldSource.clip == clip) {
            newSource = oldSource;
            if (!newSource.isPlaying) newSource.UnPause();
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
            if (pooled.isPlaying) return pooled;

            // A paused source resumes where it faded out — its position must not
            // be touched here (the menu/resume logic manages positions itself).
            // A stopped source (one-shot that played through) restarts from zero,
            // or from the saved session position if it was just restored from a save.
            if (!pooled.loop && !pooled.isPlaying) pooled.time = 0f;
            if (_resumeTime > 0f && track.name == _loadedDominantTrackName) {
                pooled.time = _resumeTime;
            }
            pooled.UnPause();
            if (!pooled.isPlaying) pooled.Play();
            return pooled;
        }

        AudioSource created = AudioManager.Instance.PlayNewMusicSource(track.Clip, track.Loop, track.name);
        if (_musicGroup != null) created.outputAudioMixerGroup = _musicGroup;
        // The pause menu track is the ONLY sound audible while the game is
        // paused (AudioListener.pause silences everything else).
        created.ignoreListenerPause = track == _pauseMenuTrack;
        if (_resumeTime > 0f && track.name == _loadedDominantTrackName) {
            created.time = Mathf.Min(_resumeTime, Mathf.Max(0f, track.Clip != null ? track.Clip.length : 0f));
        }
        _sourcePool[track] = created;
        return created;
    }
}
