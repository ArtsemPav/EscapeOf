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
    // The outgoing source of the current crossfade (fading to 0, paused at the
    // end). Tracked separately so an INTERRUPTED fade never leaves it orphaned
    // mid-volume: whoever stops the fade coroutine finishes it via this slot.
    private AudioSource _fadingOutSource;
    // Observed-playing flag for the ACTIVE source: set the moment Update sees
    // it playing, cleared whenever the active track switches. The track-life
    // guard uses it to tell a FINISHED non-loop track (played, then stopped —
    // including a pooled source that resumed mid-clip and therefore stopped
    // before the clip length elapsed) from a NEVER-STARTED one (Play() raced
    // a streaming load) — source.time and clock math cannot tell them apart.
    private bool _activeSourcePlayed;
    private int _muteOwnerCount; // nested horror events — restore fires only when the last owner releases
    private Coroutine _muteCoroutine;

    /// <summary>True while the director is silenced for a horror event.</summary>
    public static bool MutedForEvent => Instance != null && Instance._muteOwnerCount > 0;

    private readonly HashSet<string> _playedOnceTrackNames = new HashSet<string>();
    private readonly Dictionary<MusicTrack, AudioSource> _sourcePool = new Dictionary<MusicTrack, AudioSource>();
    // Zone requests that could not fire immediately: rejected by the horror
    // mute or by the min-switch-interval anti-flicker window. Stacked — on
    // replay only the LAST pending zone fires, the others are stale.
    private readonly List<(MusicTrack track, bool playOnce)> _pendingZoneRequests = new List<(MusicTrack, bool)>();
    private Coroutine _pendingZoneCoroutine;
    // All MusicTrack assets seen this session (registered by zones and Start).
    // Static so zones can register in Awake() before the director's Instance exists.
    private static readonly Dictionary<string, MusicTrack> KnownTracks = new Dictionary<string, MusicTrack>();

    // Hard cap on how long a single mute session may own the soundscape.
    // Healthy HorrorEvents release via Deactivate/StopSoundObject or the 30s
    // per-event failsafe (nested events stack, hence the generous cap); a
    // leaked owner (a manual MuteBackgroundMusic without a restore, an event
    // disabled mid-play freezing its failsafe coroutine) must not silence the
    // game — including the finale — forever. Enforced by the Update watchdog.
    private const float MuteMaxHoldSeconds = 120f;
    private float _muteHoldStart = -1f;
    private string _loadedDominantTrackName; // set by LoadSaveData, applied in Start
    private float _pendingResumeTime; // set by LoadSaveData, moved to _resumeTime in Start
    private float _resumeTime; // playback position the next created source starts from
    private Coroutine _duckCoroutine;
    private float _preDuckVolume = -1f; // volume the active source played at before the last duck
    private MusicTrack _idleCueTrack; // last track requested by the idle (anti-stuck) cue — never saved as dominant

    // ── Focus-loss silence state ──────────────────────────────────────────────
    private bool _appFocused = true;             // current window focus state (OnApplicationFocus/OnApplicationPause)
    private bool _silencedWhileUnfocused;        // the focus loss muted the menu track — refocus must refade it
    private bool _frozeSourcesWhileUnfocused;    // the focus loss paused one-shot sources — refocus must unfreeze them
    private readonly List<AudioSource> _frozenWhileUnfocused = new List<AudioSource>();
    private float _lastReviveAttempt = -999f;    // rate-limits the never-started-track restart

    /// <summary>True while the ESC pause (or its auto focus-loss variant) owns the game.
    /// Frozen sources must not unfreeze while the player sits in the menu.</summary>
    private bool GameManagerPausedSafe =>
        GameManager.Instance != null && GameManager.Instance.IsPaused;

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
        // A session saved while the idle (anti-stuck) cue was dominant must not
        // reload into it — the cue is an in-the-moment hint, the loaded session
        // falls back to the scene default and its own idle timer.
        if (dominant == _idleCueTrack) dominant = null;
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

    /// <summary>
    /// True while the scene's default game track is the dominant one — no zone
    /// request, no menu music, no event mute owns the soundscape right now.
    /// Systems that only ever want to sit on top of the default ambience
    /// (e.g. the idle music cue) check this before requesting a track.
    /// </summary>
    public bool IsDefaultTrackActive => !_menuMusicActive
        && _muteOwnerCount == 0
        && _activeTrack != null
        && _activeTrack == _defaultGameTrack;

    // Menu (pause) music state — the director owns ALL music, including the menu.
    private bool _menuMusicActive;
    private MusicTrack _preMenuTrack; // gameplay track the menu interrupted (never the menu track itself)
    private float _menuResumeTime;
    private Coroutine _menuResumeCoroutine;

    /// <summary>
    /// Stops a running crossfade and finalizes its outgoing source (the one on
    /// its way down) so an interrupted fade never leaves a source orphaned
    /// mid-volume: the player's pause/horror event/load takes over the audio
    /// cleanly. Returns true when a fade was stopped.
    /// </summary>
    private bool StopActiveFade() {
        if (_fadeCoroutine == null) return false;
        StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = null;
        if (_fadingOutSource != null) {
            _fadingOutSource.volume = 0f;
            _fadingOutSource.Pause();
            _fadingOutSource = null;
        }
        return true;
    }

    /// <summary>
    /// Switches to the pause menu track, remembering the gameplay track's
    /// playback position. Replaces the legacy AudioManager.PlayMenuMusic.
    /// </summary>
    public static void PlayMenuMusic() {
        if (Instance == null || Instance._pauseMenuTrack == null) return;
        // Plays even during a horror event: while paused the listener is muted
        // (AudioListener.pause) and the menu track is the ONLY audible sound.

        Instance._menuMusicActive = true;

        // Remember where the gameplay track stopped so it resumes from the same
        // spot. Only a source that played through to its end is not remembered
        // — resuming it would restart a played-out zone track.
        if (!Instance.TryRememberPreMenuTrack() && Instance._preMenuTrack == null) {
            // Menu opened with no gameplay music (main menu) — nothing to restore.
            Instance._preMenuTrack = null;
        }

        Instance.StopActiveFade();
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

        // Restore chain: the track the menu remembered (the source that was
        // actually sounding — the incoming source of a mid-crossfade included,
        // because the crossfade switches the bookkeeping at its START), then
        // the last requested track, then the scene default.
        MusicTrack gameplayTrack = Instance._preMenuTrack != null
            ? Instance._preMenuTrack
            : (Instance._previousTrack != null && Instance._previousTrack != Instance._pauseMenuTrack
                ? Instance._previousTrack
                : Instance._defaultGameTrack);
        if (gameplayTrack == null) return;
        // A finished one-shot must not be resurrected by the restore chain: if
        // the remembered/previous track's source sat through to the clip end,
        // the zone music is over — the default track takes over instead.
        // NOTE: a source PAUSED BY THE MENU FADE is not stale — pausing is its
        // normal state after the menu took over; only the clip end means the
        // track is over.
        if (gameplayTrack != null && gameplayTrack != Instance._defaultGameTrack
            && Instance._sourcePool.TryGetValue(gameplayTrack, out AudioSource candSrc)
            && candSrc != null && !candSrc.isPlaying
            && gameplayTrack.Clip != null && candSrc.time >= gameplayTrack.Clip.length - 0.05f) {
            Debug.Log($"[Music] PlayGameMusic: '{gameplayTrack.name}' already played through — falling back to the default track.");
            gameplayTrack = Instance._defaultGameTrack;
        }

        if (Instance._menuResumeCoroutine != null) Instance.StopCoroutine(Instance._menuResumeCoroutine);
        // Finalizes the menu fade's outgoing source (the pre-menu track's
        // source keeps its position for the resume below).
        Instance.StopActiveFade();
        Instance._activeTrack = gameplayTrack;
        // ResumeGameplayRoutine consumes _menuResumeTime and _preMenuTrack itself.
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

    // ── Focus-loss silence (pause-menu track must not leak into other apps) ───

    /// <summary>
    /// Window focus lost or regained (and build-level application pause).
    /// While unfocused, Update() keeps the pause-menu track silent; on focus
    /// regain it refades the menu track in from its paused position.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus) {
        if (Instance != this) return; // scene duplicate awaiting destruction
        SetAppFocused(hasFocus);
    }

    private void OnApplicationPause(bool paused) {
        if (Instance != this) return; // builds fire this on minimize/device loss
        SetAppFocused(!paused);
    }

    private void SetAppFocused(bool focused) {
        if (_appFocused == focused) return;
        _appFocused = focused;

        if (!focused) {
            // Immediate cut — the callback may arrive before/after GameManager's
            // own focus handler, Update() enforces the state every frame anyway.
            if (_menuMusicActive && _activeSource != null) {
                _silencedWhileUnfocused = true;
                _activeSource.Pause();
                _activeSource.volume = 0f;
            }
            return;
        }

        // Focus regained: refade the menu track in if the focus loss had
        // silenced it. A still-running menu fade targets zero — replace it.
        if (!_silencedWhileUnfocused) return;
        _silencedWhileUnfocused = false;
        if (!_menuMusicActive || _pauseMenuTrack == null) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeRoutine(_pauseMenuTrack));
    }

    /// <summary>
    /// Remembers the currently active gameplay track for a later menu restore,
    /// but only when its source actually carries that clip AND is still alive.
    /// The incoming source of a mid-crossfade QUALIFIES: the crossfade switched
    /// _activeSource to it at fade start, and its position is meaningful.
    /// Only a source that played through to the clip end is stale — resuming
    /// it would restart a played-out zone track.
    /// </summary>
    private bool TryRememberPreMenuTrack() {
        if (_activeSource == null || _activeTrack == null || _activeTrack == _pauseMenuTrack) return false;
        if (_activeSource.clip != _activeTrack.Clip) return false;

        // SetPause(true) has already raised AudioListener.pause by the time
        // PlayMenuMusic runs — a listener-paused source reports isPlaying ==
        // false, so the pause alone must not disqualify it. Only a source that
        // finished naturally (sitting at the clip end) is stale and must not
        // be remembered: resuming it would restart a played-out zone track.
        float clipLength = _activeTrack.Clip != null ? _activeTrack.Clip.length : 0f;
        bool finished = clipLength > 0f && _activeSource.time >= clipLength - 0.05f;
        bool alive = !finished
            && (_activeSource.isPlaying || AudioListener.pause || _activeSource.time > 0f);
        if (!alive) return false;

        _menuResumeTime = _activeSource.time;
        _preMenuTrack = _activeTrack;
        return true;
    }

    private bool _registeredWithSaveManager;

    private void Start() {
        PrewarmKnownTracks();

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

    /// <summary>
    /// Kicks off background audio data loading for every registered track.
    /// With the clips' "Load Type: Streaming + Preload Audio Data" the OS
    /// streams the file without blocking, so the first play never hitches
    /// the main thread. Best-effort: clips register as zones Awake.
    /// </summary>
    private void PrewarmKnownTracks() {
        foreach (MusicTrack track in KnownTracks.Values) {
            if (track == null || track.Clip == null) continue;
            track.Clip.LoadAudioData();
        }
    }

    private void Update() {
        // ── Focus-loss silence ────────────────────────────────────────────────
        // While the game window is unfocused (Alt-Tab) the pause-menu track must
        // stay silent — the focus-loss pause (GameManager) muting the world via
        // AudioListener.pause does not cover this ignoreListenerPause source.
        // Enforced in Update (not in the focus callback) because the callback
        // order between GameManager and the director is undefined: SetPause may
        // start the menu fade AFTER this component saw the focus loss.
        if (!_appFocused && _menuMusicActive && _activeSource != null) {
            if (_activeSource.isPlaying) _activeSource.Pause();
            if (_activeSource.volume > 0f) _activeSource.volume = 0f;
            _silencedWhileUnfocused = true;
        }

        // ── Focus-loss freeze (one-shot tracks) ───────────────────────────────
        // The GameManager's Alt-Tab pause only mutes the world while the app
        // still runs - and AudioListener.pause does NOT hold a one-shot
        // source's playback position against the audio clock racing ahead of
        // the frozen game loop. A track playing through while unfocused then
        // "finished" behind the player's back: the director restored the
        // fallback mid-finale, and a mid-crossfade source restarted from zero
        // silently. While unfocused, freeze every non-menu source in place.
        if (!_appFocused && !_frozeSourcesWhileUnfocused && !_menuMusicActive) {
            _frozeSourcesWhileUnfocused = true;
            foreach (KeyValuePair<MusicTrack, AudioSource> pooled in _sourcePool) {
                if (pooled.Value == null || pooled.Key == _pauseMenuTrack) continue;
                if (pooled.Value.isPlaying) {
                    pooled.Value.Pause();
                    _frozenWhileUnfocused.Add(pooled.Value);
                }
            }
        }
        if (_appFocused && _frozeSourcesWhileUnfocused) {
            _frozeSourcesWhileUnfocused = false;
            foreach (AudioSource source in _frozenWhileUnfocused) {
                if (source != null && !source.isPlaying && !GameManagerPausedSafe) source.UnPause();
            }
            _frozenWhileUnfocused.Clear();
        }

        // ── Track-life guard: the source is the single source of truth ───────
        // If the ACTIVE source stopped without a request, find out WHY and
        // react. All source death paths funnel through this state machine:
        // - a pause took over (menu/autoplay pause)  -> the pause owns audio,
        //   its FadeRoutine will restart the source on resume;
        // - a mute is held (horror event)            -> RestoreAfterEvent will;
        // - a fade is running                        -> it manages both sources;
        // - a non-loop clip reached its end          -> RestoreFallbackTrack;
        // - anything else (streaming source died after alt-tab / device loss,
        //   play() failed while the clip was still loading) -> the source is
        //   restarted from the clip start — a stopped non-loop source mid-fade
        //   (e.g. killed and unpaused by a pause/restore cycle) is also revived
        //   here instead of being misreported as finished.
        // ── Mute-leak watchdog (runs BEFORE the MutedForEvent early-return):
        // A mute owner that never releases (a manual MuteBackgroundMusic left
        // without a restore, or an event disabled mid-play whose failsafe
        // coroutine is frozen) must not silence the game — and the finale —
        // forever. Past the hard cap the count is zeroed and the pre-mute
        // track (plus any queued zone requests) comes back.
        if (_muteOwnerCount > 0 && Time.unscaledTime - _muteHoldStart > MuteMaxHoldSeconds) {
            Debug.LogWarning($"[Music] A mute owner never released for over {MuteMaxHoldSeconds}s — forcing restore (leaked HorrorEvent mute or manual MuteBackgroundMusic without a restore).");
            ForceReleaseMute();
            return;
        }
        if (_activeSource == null || _activeTrack == null || _fadeCoroutine != null || MutedForEvent) return;
        if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;

        if (_activeSource.isPlaying) {
            _activeSourcePlayed = true; // observed live — the track did start
            return;
        }

        if (_activeTrack.Loop) {
            Debug.LogWarning($"[Music] Active loop track '{_activeTrack.name}' is not playing outside pause/mute — reviving its source.");
            _activeSource.UnPause();
            if (!_activeSource.isPlaying) {
                _activeSource.time = 0f;
                _activeSource.Play();
            }
            if (!_activeSource.isPlaying && AudioManager.Instance != null) {
                // Full device loss: the pooled source is unusable — rebuild it.
                _sourcePool.Remove(_activeTrack);
                AudioSource rebuilt = AudioManager.Instance.PlayNewMusicSource(_activeTrack.Clip, true, _activeTrack.name);
                if (_musicGroup != null) rebuilt.outputAudioMixerGroup = _musicGroup;
                rebuilt.ignoreListenerPause = _activeTrack == _pauseMenuTrack;
                rebuilt.volume = _activeTrack.Volume;
                _activeSource = rebuilt;
                _activeSourcePlayed = false;
                _sourcePool[_activeTrack] = rebuilt;
                Debug.LogWarning($"[Music] Source for '{_activeTrack.name}' was unusable — rebuilt a fresh one.");
            }
            return;
        }

        // Non-looping track, not playing, no fade/mute/pause owns the audio.
        // The observed-playing flag is the ONLY reliable finished/never-started
        // discriminator: source.time clamps to ~0 on a finished source on some
        // platforms, and clock math misses a pooled source that RESUMED
        // mid-clip (Room1/Room7 both restarted from zero that way). If Update
        // ever saw this source playing, it is a spent track — fall back; if
        // not, Play() never took (streaming load race, pause/restore cycle) —
        // revive it: the player walked into its zone and must hear it.
        float clipLength = _activeTrack.Clip != null ? _activeTrack.Clip.length : 0f;
        if (_activeSourcePlayed) {
            Debug.Log($"[Music] Track '{_activeTrack.name}' finished playing -> restoring fallback (isPlaying=false detected)");
            RestoreFallbackTrack();
            return;
        }

        // Rate-limited: if Play() keeps failing (e.g. AudioListener.pause
        // desync — source can never start while the listener is paused),
        // logging/Play() every frame would spam the console. Retry at most
        // twice a second; the source-life guard above still catches the
        // recovery the moment the cause is gone.
        if (Time.unscaledTime - _lastReviveAttempt < 0.5f) return;
        _lastReviveAttempt = Time.unscaledTime;
        // A source parked at the clip end is spent by definition (a resume
        // started there immediately stops again, so it is never "observed
        // playing") — restarting it would loop restarts forever.
        if (clipLength > 0f && _activeSource.time >= clipLength - 0.05f) {
            Debug.Log($"[Music] Track '{_activeTrack.name}' parked at the clip end -> restoring fallback.");
            RestoreFallbackTrack();
            return;
        }
        Debug.LogWarning($"[Music] Non-loop track '{_activeTrack.name}' never started (source stopped at 0:00) — restarting.");
        _activeSource.Play();
    }

    /// <summary>
    /// True when the given AudioSource belongs to the director (a pooled
    /// source or the current crossfade target). The ambience mute must never
    /// sweep the director's music away — only world ambience.
    /// </summary>
    public bool OwnsSource(AudioSource source) {
        if (source == null) return false;
        if (_activeSource == source) return true;
        foreach (KeyValuePair<MusicTrack, AudioSource> pooled in _sourcePool)
            if (pooled.Value == source) return true;
        return false;
    }

    /// <summary>
    /// Requests a track to play. If the same track is already active the request is ignored;
    /// otherwise the director IMMEDIATELY crossfades to it — the playing track fades out
    /// while the new one fades in. The newest request always wins.
    /// Tracks already played with playOnce are skipped.
    /// While a horror mute is held the request is not lost — it is queued and replayed
    /// when the last mute owner releases.
    /// Returns true when the request was accepted (the track is now the dominant one).
    /// </summary>
    public static bool RequestTrack(MusicTrack track, bool playOnce = false) {
        if (Instance == null || track == null) return false;
        RegisterTrack(track);
        if (playOnce && Instance._playedOnceTrackNames.Contains(track.name)) return false;
        if (MusicDirector.MutedForEvent) {
            // The horror event owns the soundscape — remember which zone the
            // player walked into and fire it once the mute lifts.
            if (!Instance._pendingZoneRequests.Exists(p => p.track == track))
                Instance._pendingZoneRequests.Add((track, playOnce));
            Debug.Log($"[Music] RequestTrack '{track.name}' QUEUED: muted for horror event, will replay on restore");
            if (Instance._pendingZoneCoroutine == null)
                Instance._pendingZoneCoroutine = Instance.StartCoroutine(Instance.PendingZoneRoutine());
            return false;
        }
        return Instance.RequestTrackInternal(track, playOnce);
    }

    /// <summary>
    /// Waits for the horror mute to lift, then fires the newest queued zone.
    /// </summary>
    private IEnumerator PendingZoneRoutine() {
        while (_pendingZoneRequests.Count > 0) {
            yield return null;

            if (MutedForEvent) continue;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused) continue;

            (MusicTrack pending, bool pendingPlayOnce) = _pendingZoneRequests[_pendingZoneRequests.Count - 1];
            _pendingZoneRequests.Clear();
            _pendingZoneCoroutine = null;
            Debug.Log($"[Music] Pending zone request '{pending.name}' -> replaying after mute");
            RequestTrackInternal(pending, pendingPlayOnce);
            yield break;
        }
        _pendingZoneCoroutine = null;
    }

    /// <summary>
    /// Requests the idle (anti-stuck) cue track. Behaves like RequestTrack but
    /// remembers the cue: a session saved while the cue plays reloads into the
    /// default game track instead, and ClearTrack restores the previous track.
    /// </summary>
    public static bool RequestIdleCueTrack(MusicTrack track) {
        if (Instance == null) return false;
        Instance._idleCueTrack = track;
        return RequestTrack(track, playOnce: false);
    }

    /// <summary>
    /// Restores the previously playing track after a temporary request ends.
    /// Safe to call with a track that is not currently active — the request is ignored.
    /// </summary>
    public static void ClearTrack(MusicTrack track) {
        if (Instance == null || track == null) return;
        Instance.ClearTrackInternal(track);
    }

    /// <summary>
    /// Restores the music after a non-looping zone track plays through to
    /// its end. Always falls back to the default game track — a previously
    /// played zone track must never restart, only the default loops on.
    /// </summary>
    private void RestoreFallbackTrack() {
        MusicTrack fallback = _defaultGameTrack != null && _defaultGameTrack != _activeTrack
            ? _defaultGameTrack
            : null;
        Debug.Log($"[Music] RestoreFallbackTrack: finished '{_activeTrack.name}' -> fallback '{(fallback != null ? fallback.name : "NONE (fade to silence)")}'");
        _previousTrack = null;
        _activeTrack = fallback;
        _lastSwitchTime = Time.unscaledTime;

        StopActiveFade();
        if (_duckCoroutine != null) StopCoroutine(_duckCoroutine); // its volume writes would fight FadeRoutine
        _duckCoroutine = null; // track switch cancels any active duck
        if (fallback != null) {
            _fadeCoroutine = StartCoroutine(FadeRoutine(fallback));
        } else {
            // No default track (or the finished track IS the default) — fade
            // the active source out and pause it. _activeSource keeps pointing
            // at it so a later track request can crossfade away from it.
            _fadeCoroutine = StartCoroutine(FadeOutAndPauseRoutine(_activeSource));
        }
    }

    private bool RequestTrackInternal(MusicTrack track, bool playOnce) {
        if (MutedForEvent) { Debug.Log($"[Music] RequestTrack '{track.name}' REJECTED: muted for horror event"); return false; } // horror event owns the music — requests are queued out
        if (_activeTrack == track) return true;

        // No anti-flicker interval here anymore: the newest request always
        // wins immediately — the old track fades out under the new one.

        if (playOnce) _playedOnceTrackNames.Add(track.name);

        Debug.Log($"[Music] RequestTrack '{track.name}' ACCEPTED (playOnce: {playOnce}), previous: '{(_activeTrack != null ? _activeTrack.name : "null")}'");
        _previousTrack = _activeTrack;
        _activeTrack = track;
        _lastSwitchTime = Time.unscaledTime;

        StopActiveFade();
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
    public static void MuteForEvent(float fadeDuration, string caller = null) {
        if (Instance == null) return;
        Instance._muteOwnerCount++; // nested events stack — each owner must release individually
        if (Instance._muteOwnerCount == 1) Instance._muteHoldStart = Time.unscaledTime; // watchdog clock
        Debug.Log($"[Music] MuteForEvent (fade {fadeDuration}s) from '{(caller ?? "unknown")}' — owners now: {Instance._muteOwnerCount}");

        Instance.StopActiveFade();
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
        Debug.Log($"[Music] RestoreAfterEvent — owners remaining: {Instance._muteOwnerCount}");
        if (Instance._muteOwnerCount > 0) return; // another horror event still owns the soundscape
        Instance.RestoreAfterLastOwner(fadeDurationOverride);
    }

    /// <summary>
    /// Watchdog helper: zeroes a leaked mute count and refades the pre-mute
    /// track. Never called while a healthy event still owns the mute — the
    /// hold time already exceeded MuteMaxHoldSeconds.
    /// </summary>
    private static void ForceReleaseMute() {
        Instance._muteOwnerCount = 0;
        Instance.RestoreAfterLastOwner(-1f);
    }

    /// <summary>
    /// The shared tail of every restore path (owner released normally, or the
    /// watchdog force-released a leak): stop the mute fade, drop the duck and
    /// refade the pre-mute track. Queued zone requests are replayed by
    /// PendingZoneRoutine once MutedForEvent turns false.
    /// </summary>
    private void RestoreAfterLastOwner(float fadeDurationOverride) {
        // An unfinished mute fade must stop — its volume writes would fight the restore fade
        if (_muteCoroutine != null) {
            StopCoroutine(_muteCoroutine);
            _muteCoroutine = null;
        }
        _preDuckVolume = -1f; // a duck interrupted by the event must not restore a stale level

        // The menu owns the music — nothing to restore until the menu closes.
        // A zone queued during the mute is replayed by PendingZoneRoutine —
        // it waits out the menu too, and until then the pre-event track
        // refades below as usual.
        if (_menuMusicActive) return;

        // Refade the track that was active before the event; FadeRoutine
        // un-pauses the pooled source and brings its volume back up.
        // Exception: a track that had ALREADY played through to the end when
        // the event muted it (idle cue, zone one-shot). Resuming a finished
        // source restarts it from zero — the idle cue would replay from the
        // start, so hand the restore to the fallback logic instead.
        MusicTrack track = _activeTrack;
        if (track != null) {
            float clipLength = track.Clip != null ? track.Clip.length : 0f;
            bool finished = clipLength > 0f
                && _activeSource != null
                && _activeSource.clip == track.Clip
                && _activeSource.time >= clipLength - 0.05f;
            if (finished) {
                Debug.Log($"[Music] RestoreAfterLastOwner: pre-mute track '{track.name}' already finished -> fallback instead of restart.");
                RestoreFallbackTrack();
                return;
            }
            _fadeCoroutine = StartCoroutine(FadeRoutine(track, fadeDurationOverride));
        }
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

    /// <summary>
    /// Fades every pooled director source to silence over fadeDuration seconds
    /// and pauses them, clearing the active track. Called when a loading screen
    /// takes over (menu → game, game → menu): the new scene's own music logic
    /// starts the next track after the load, nothing here resumes the old one.
    /// Unlike MuteForEvent this blocks nothing and needs no restore — the
    /// transition ends the director's current ownership entirely.
    /// </summary>
    public static void FadeOutForLoadingScreen(float fadeDuration = 1.5f, string caller = null) {
        if (Instance == null) return;
        Debug.Log($"[Music] FadeOutForLoadingScreen ({fadeDuration}s) from '{(caller ?? "unknown")}'");

        // A fade still in progress: stop its bookkeeping, sources re-snapshot below.
        Instance.StopActiveFade();
        if (Instance._duckCoroutine != null) { Instance.StopCoroutine(Instance._duckCoroutine); Instance._duckCoroutine = null; }
        Instance._preDuckVolume = -1f;

        Instance._menuMusicActive = false;
        Instance._activeTrack = null;
        Instance._previousTrack = null;
        Instance._preMenuTrack = null;
        Instance._menuResumeTime = 0f;
        Instance._activeSource = null;

        // The fade runs on a dedicated DontDestroyOnLoad host, not on the
        // director: MainMenuItemBtn destroys the persistent AudioManager
        // mid-transition, which would kill a coroutine started on it.
        LoadingFadeHost.BeginFadeOut(Instance._sourcePool.Values, fadeDuration);
    }

    /// <summary>
    /// Survives singleton destruction during scene transitions so music fades
    /// out cleanly even when the AudioManager itself is being destroyed.
    /// </summary>
    private class LoadingFadeHost : MonoBehaviour {
        private static LoadingFadeHost _instance;

        public static void BeginFadeOut(Dictionary<MusicTrack, AudioSource>.ValueCollection sources, float duration) {
            if (_instance == null) {
                GameObject hostObject = new GameObject("MusicFadeHost");
                DontDestroyOnLoad(hostObject);
                _instance = hostObject.AddComponent<LoadingFadeHost>();
            }
            foreach (AudioSource source in sources) {
                if (source == null) continue;
                _instance.StartCoroutine(FadeOutAndPause(source, duration));
            }
        }

        private static IEnumerator FadeOutAndPause(AudioSource source, float duration) {
            yield return AudioFades.Volume(source, 0f, duration);
            if (source != null) source.Pause(); // keeps its position for later resume
        }

        private void OnDestroy() {
            if (_instance == this) _instance = null;
        }
    }

    private void ClearTrackInternal(MusicTrack track) {
        if (_activeTrack != track) return;

        MusicTrack restoreTo = _previousTrack;
        _previousTrack = null;
        _activeTrack = restoreTo;
        _lastSwitchTime = Time.unscaledTime;

        StopActiveFade();
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

        // Switch the bookkeeping NOW, not at fade end: any interruption during
        // the crossfade (pause menu, horror mute, focus loss) must see the
        // INCOMING source as active, and a fade interrupted before it killed
        // the previous outgoing source must not leave that source orphaned
        // mid-volume. Without this, a menu opened mid-crossfade remembered the
        // OLD track as the restore target and the player's newest zone request
        // was lost (its source kept playing half-faded, unowned).
        if (oldSource != newSource) {
            if (_fadingOutSource != null && _fadingOutSource != oldSource) {
                // The previous fade was interrupted before it finished — finish
                // its outgoing source here (it is already on its way down).
                _fadingOutSource.volume = 0f;
                _fadingOutSource.Pause();
            }
            _fadingOutSource = oldSource;
        }
        _activeSource = newSource;
        _activeSourcePlayed = false; // fresh start — the guard must re-observe playback

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
            if (_fadingOutSource == oldSource) _fadingOutSource = null;
        }
        newSource.volume = targetVolume;

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
            // Only a source that played through to the end restarts from zero
            // (or from the saved session position if just restored from a save).
            if (!pooled.loop && !pooled.isPlaying) {
                float clipLength = track.Clip != null ? track.Clip.length : 0f;
                bool finished = clipLength > 0f && pooled.time >= clipLength - 0.05f;
                if (finished) pooled.time = 0f;
            }
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
