using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Sound-effect service: one-shot SFX, spatial 3D loops and background layers.
/// Owns no music — all music (menu, gameplay, zones, events) is handled by
/// MusicDirector through the Music bus of the SFX AudioMixer.
/// SFX is split into two mixer-bus categories: SFX2D (non-positional sounds)
/// and SFX3D (spatial sources); sources are routed automatically by their
/// spatialBlend, and unassigned categories fall back to the general SFX group.
/// Volume settings are applied to the exposed mixer parameters (SFXVolume /
/// MusicVolume) by SystemSettings, so sliders affect playing audio instantly.
/// </summary>
public class AudioManager : MonoBehaviour {
    public static AudioManager Instance { get; private set; }

    [Header("SFX")]
    [Tooltip("2D source all one-shot sound effects play through.")]
    [SerializeField] private AudioSource _sfxSource;

    [Tooltip("2D source for menu/UI one-shots; ignores AudioListener pause so menu clicks stay audible while the game is paused.")]
    [SerializeField] private AudioSource _uiSfxSource;

    [Tooltip("SFX AudioMixer with the exposed SFXVolume and MusicVolume parameters.")]
    [SerializeField] private AudioMixer _sfxMixer;

    [Tooltip("Name of the exposed SFX volume parameter on the AudioMixer.")]
    [SerializeField] private string _sfxVolumeParameter = "SFXVolume";

    [Tooltip("Name of the exposed music volume parameter on the AudioMixer (Music bus).")]
    [SerializeField] private string _musicVolumeParameter = "MusicVolume";

    [Tooltip("Mixer group all dynamically created sound-effect sources are routed to (fallback when no category group is assigned).")]
    [SerializeField] private AudioMixerGroup _sfxGroup;

    [Tooltip("Mixer group for 2D sound effects (non-positional one-shots and background layers). Fallback: _sfxGroup.")]
    [SerializeField] private AudioMixerGroup _sfx2DGroup;

    [Tooltip("Mixer group for 3D (spatial) sound effects. Fallback: _sfxGroup.")]
    [SerializeField] private AudioMixerGroup _sfx3DGroup;

    [Tooltip("Mixer group for safe-dial sounds, routed under Master OUTSIDE the Music/SFX buses, " +
             "so stethoscope mode can silence the world while safe sounds stay audible.")]
    [SerializeField] private AudioMixerGroup _safeGroup;

    [Header("Fades")]
    [Tooltip("Default fade duration in seconds for background layers and mute transitions.")]
    [SerializeField] private float _fadeDuration = 1.5f;

    // ── Stethoscope state ──────────────────────────────────────────────────────

    private AudioSource _safeSfxSource;
    private bool _stethoscopeActive;

    // ── Background layer state ─────────────────────────────────────────────────

    private AudioSource _backgroundLayerSource;
    private Coroutine _layerCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // The audio stack boots fresh here — clear any AudioListener.pause
            // inherited from a scene transition (e.g. the pause menu's exit to
            // the main menu, where the dying gameplay scene must stay silent
            // under the loading screen until this Awake unpauses the listener).
            AudioListener.pause = false;
            BackgroundTracking.ResetForNewSession();
            ApplySavedVolumes();
            // The shared 2D SFX source belongs to the 2D category (its group may
            // already be set in the inspector — only route when it isn't).
            if (_sfxSource != null && !BelongsToCategory(_sfxSource, _sfx2DGroup))
                RouteToCategory(_sfxSource);
            if (_uiSfxSource != null) {
                _uiSfxSource.ignoreListenerPause = true;
                if (!BelongsToCategory(_uiSfxSource, _sfx2DGroup))
                    RouteToCategory(_uiSfxSource);
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        } else {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>After each scene load, routes every orphan AudioSource (no mixer group) to its 2D/3D SFX category by spatialBlend.</summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) {
        AudioSource[] sources = FindObjectsOfType<AudioSource>();
        foreach (AudioSource source in sources) {
            if (source != null && source.outputAudioMixerGroup == null)
                RouteToCategory(source);
        }
    }

    /// <summary>
    /// Plays a one-shot 3D sound effect from a pooled source parented to the
    /// target (auto-destroys when the clip finishes). Routed to the SFX3D bus.
    /// </summary>
    public void Play3DOneShot(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 10f) {
        if (clip == null) return;
        GameObject obj = new GameObject("3D_OneShot_SFX");
        obj.transform.position = position;

        AudioSource source = obj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        // Linear rolloff makes maxDistance a hard cutoff: the source is truly
        // silent beyond it. The default Logarithmic rolloff ignores maxDistance
        // and fades only as 1/distance, so clicks stay faintly audible floors
        // away (no audio occlusion in Unity — geometry does not block sound).
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.playOnAwake = false;
        RouteToCategory(source);
        source.Play();
        Destroy(obj, clip.length + 0.1f);
    }

    /// <summary>Applies the saved music and SFX volumes (PlayerPrefs) to the AudioMixer buses.</summary>
    public void ApplySavedVolumes() {
        AudioMixer mixer = ResolveMixer();
        if (mixer == null) return;
        mixer.SetFloat(_sfxVolumeParameter, SystemSettings.ToDecibels(SystemSettings.SfxVolume));
        mixer.SetFloat(_musicVolumeParameter, SystemSettings.ToDecibels(SystemSettings.MusicVolume));
    }

    /// <summary>Mixer access for SystemSettings (instant volume application).</summary>
    public AudioMixer ResolveMixerExternal() => ResolveMixer();

    /// <summary>The mixer from the serialized field or the SFX source's group as fallback.</summary>
    private AudioMixer ResolveMixer() {
        if (_sfxMixer != null) return _sfxMixer;
        return _sfxSource != null && _sfxSource.outputAudioMixerGroup != null
            ? _sfxSource.outputAudioMixerGroup.audioMixer
            : null;
    }

    /// <summary>
    /// Routes a dynamically created source to its SFX category group: 2D
    /// (spatialBlend &lt; 0.5) goes to the SFX2D bus, spatial sources to SFX3D.
    /// Falls back to the general SFX group when a category group is unassigned.
    /// </summary>
    private void RouteToCategory(AudioSource source) {
        if (source == null) return;
        AudioMixerGroup group = source.spatialBlend >= 0.5f ? _sfx3DGroup : _sfx2DGroup;
        if (group == null) group = _sfxGroup;
        if (group != null)
            source.outputAudioMixerGroup = group;
    }

    /// <summary>True when the source is already routed to the given category group (or the group is unassigned).</summary>
    private static bool BelongsToCategory(AudioSource source, AudioMixerGroup categoryGroup) {
        return categoryGroup == null || source.outputAudioMixerGroup == categoryGroup;
    }

    /// <summary>
    /// Public wrapper for component-owned sources (e.g. drag/scrape loops)
    /// outside the tracking system. Routes by spatialBlend: 2D sources to the
    /// SFX2D bus, spatial sources to SFX3D.
    /// </summary>
    public void RouteSourceToSfxGroup(AudioSource source) => RouteToCategory(source);

    // ── Dynamic music source factory (used by MusicDirector) ───────────────────

    /// <summary>
    /// Creates a dedicated looping AudioSource under the manager for a
    /// dynamically requested music track and starts playback at volume 0.
    /// The director routes it to the Music bus and owns its lifetime.
    /// </summary>
    public AudioSource PlayNewMusicSource(AudioClip clip, bool loop, string trackName = null) {
        GameObject musicObject = new GameObject("DynamicMusic_" + (string.IsNullOrEmpty(trackName) ? "Track" : trackName));
        musicObject.transform.SetParent(transform);

        AudioSource source = musicObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.playOnAwake = false;
        source.Play();

        return source;
    }

    // ── One-shot SFX ───────────────────────────────────────────────────────────

    public void PlaySFX(AudioClip clip) => PlaySFX(clip, 1f);

    /// <summary>Plays a one-shot sound effect on the shared 2D SFX source.</summary>
    public void PlaySFX(AudioClip clip, float volume) {
        if (clip != null)
            _sfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Plays a one-shot menu/UI sound on the dedicated UI source that ignores
    /// AudioListener.pause, so hover/click stay audible while the game is
    /// paused (AudioListener.pause = true does not hold them back).
    /// </summary>
    public void PlayUISFX(AudioClip clip, float volume) {
        if (clip != null && _uiSfxSource != null)
            _uiSfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Plays an SFX clip, first stopping all sounds on the shared SFX source.
    /// Use for sounds that must not overlap themselves (e.g. a repeating
    /// success chime in a minigame).
    /// </summary>
    public void PlaySFXExclusive(AudioClip clip, float volume = 1f) {
        if (clip == null) return;
        _sfxSource.Stop();
        _sfxSource.PlayOneShot(clip, volume);
    }

    // ── Tracked 3D loops (faded with the background) ───────────────────────────

    /// <summary>
    /// Creates a looping spatial source parented to the target and registers it
    /// in the background tracking system (faded by MuteBackground/UnmuteBackground).
    /// Returns the source so the caller can stop it later via UnregisterLoopSource.
    /// </summary>
    public AudioSource Play3DLoop(AudioClip clip, Transform target, float volume, float minDistance, float maxDistance) {
        GameObject loopObject = new GameObject("3D_Loop_SFX");
        loopObject.transform.position = target.position;
        loopObject.transform.SetParent(target);

        AudioSource source = loopObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f; // Полное 3D
        // Linear rolloff makes maxDistance a hard cutoff: the source is truly
        // silent beyond it (same as Play3DOneShot). The default Logarithmic
        // rolloff ignores maxDistance and fades only as 1/distance.
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.loop = true;
        source.playOnAwake = false;
        RouteToCategory(source);
        source.Play();

        BackgroundTracking.Register(source, volume);
        return source;
    }

    /// <summary>
    /// Registers an existing AudioSource (configured prefab source) in the
    /// background tracking system so MuteBackground/UnmuteBackground fade it.
    /// </summary>
    public void RegisterLoopSource(AudioSource source, float volume) {
        if (source == null) return;
        RouteToCategory(source);
        BackgroundTracking.Register(source, volume);
    }

    /// <summary>Unregisters a tracked loop source and stops its playback. Safe to call twice.</summary>
    public void UnregisterLoopSource(AudioSource source) {
        if (source == null) return;
        BackgroundTracking.Unregister(source);
        source.Stop();
    }

    // ── Background layer ───────────────────────────────────────────────────────

    /// <summary>
    /// Plays a looping 2D background layer (heartbeat, ambient hum) that fades in
    /// independently of the tracked loops. Returns the AudioSource for the caller.
    /// </summary>
    public AudioSource PlayBackgroundLayer(AudioClip clip, float volume = 1f, float fadeDuration = -1f) {
        // Stop AND fully finish the previous layer's fade-out before starting a new one —
        // otherwise RestartLayerCoroutine would kill the old fade and leak the source.
        if (_backgroundLayerSource != null || _layerCoroutine != null) {
            StopBackgroundLayer(0f);
        }

        GameObject layerObject = new GameObject("BackgroundLayer_SFX");
        layerObject.transform.SetParent(transform);

        _backgroundLayerSource = layerObject.AddComponent<AudioSource>();
        _backgroundLayerSource.clip         = clip;
        _backgroundLayerSource.loop         = true;
        _backgroundLayerSource.spatialBlend = 0f; // 2D
        _backgroundLayerSource.volume       = 0f;
        _backgroundLayerSource.playOnAwake  = false;
        RouteToCategory(_backgroundLayerSource);
        _backgroundLayerSource.Play();

        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        RestartLayerCoroutine(AudioFades.Volume(_backgroundLayerSource, volume, duration));
        return _backgroundLayerSource;
    }

    /// <summary>Fades out and destroys the currently playing background layer.</summary>
    public void StopBackgroundLayer(float fadeDuration = -1f) {
        if (_layerCoroutine != null) {
            StopCoroutine(_layerCoroutine);
            _layerCoroutine = null;
        }
        if (_backgroundLayerSource == null) return;

        AudioSource source = _backgroundLayerSource;
        _backgroundLayerSource = null;

        if (fadeDuration <= 0f) {
            Destroy(source.gameObject);
            return;
        }

        float duration = fadeDuration;
        RestartLayerCoroutine(AudioFades.Volume(source, 0f, duration, () => {
            if (source != null) Destroy(source.gameObject);
        }));
    }

    private void RestartLayerCoroutine(IEnumerator routine) {
        if (_layerCoroutine != null) StopCoroutine(_layerCoroutine);
        _layerCoroutine = StartCoroutine(routine);
    }

    // ── Stethoscope fade (exposed mixer volume params, Safe group excluded) ────

    /// <summary>Supersedes any running stethoscope fade and starts a new one.</summary>
    private void StartStethoscopeFade(AudioMixer mixer, bool mute, float duration) {
        if (_stethoscopeMuteRoutine != null) StopCoroutine(_stethoscopeMuteRoutine);
        _stethoscopeMuteRoutine = StartCoroutine(StethoscopeFadeRoutine(mixer, mute, duration));
    }

    /// <summary>
    /// Fades the Music and SFX (parent, so both 2D/3D children follow) bus volumes:
    /// muting fades to silence, unmuting restores the user's saved volumes
    /// (SystemSettings). The Safe group is a direct child of Master, outside both
    /// buses, so it is not affected.
    /// </summary>
    private IEnumerator StethoscopeFadeRoutine(AudioMixer mixer, bool mute, float duration) {
        float sfxStart   = GetBusLinearVolume(mixer, _sfxVolumeParameter);
        float musicStart = GetBusLinearVolume(mixer, _musicVolumeParameter);
        float sfxEnd     = mute ? 0f : SystemSettings.SfxVolume;
        float musicEnd   = mute ? 0f : SystemSettings.MusicVolume;

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            mixer.SetFloat(_sfxVolumeParameter, SystemSettings.ToDecibels(Mathf.Lerp(sfxStart, sfxEnd, t)));
            mixer.SetFloat(_musicVolumeParameter, SystemSettings.ToDecibels(Mathf.Lerp(musicStart, musicEnd, t)));
            yield return null;
        }
        mixer.SetFloat(_sfxVolumeParameter, SystemSettings.ToDecibels(sfxEnd));
        mixer.SetFloat(_musicVolumeParameter, SystemSettings.ToDecibels(musicEnd));
    }

    /// <summary>Reads a bus volume from the mixer, converting decibels to the 0..1 scale.</summary>
    private float GetBusLinearVolume(AudioMixer mixer, string parameter) {
        return mixer.GetFloat(parameter, out float db) ? SystemSettings.FromDecibels(db) : 1f;
    }

    // ── Ambience mute (ending cutscene: the world goes quiet, the music speaks) ──

    [Tooltip("How often the ambience mute re-scans for world sources that appeared after the mute started (e.g. fires the ending ignites).")]
    [SerializeField, Range(0.2f, 5f)] private float _ambienceRescanInterval = 0.5f;

    // ── Stethoscope fade state ─────────────────────────────────────────────────

    private Coroutine _stethoscopeMuteRoutine;

    private readonly List<AudioSource> _mutedAmbience = new List<AudioSource>();
    private readonly List<float> _mutedAmbienceVolumes = new List<float>();
    private Coroutine _ambienceMuteCoroutine;
    private bool _ambienceMuted;

    /// <summary>
    /// Silences the world's ambient soundscape for a cinematic: fades out and
    /// pauses every scene AudioSource EXCEPT the MusicDirector's sources and
    /// the shared SFX source. Unlike MuteBackground this does not require
    /// sources to be registered — it sweeps the whole scene, and keeps
    /// re-scanning while held, so ambience that APPEARS during the cinematic
    /// (e.g. fires the ending ignites near the portal) is silenced too.
    /// Intentionally one-way for the ending: the credits flow destroys this
    /// singleton on the way back to the main menu, so nothing needs restoring.
    /// </summary>
    public void MuteAmbience(float fadeDuration = 1.5f) {
        if (_ambienceMuted) return;
        _ambienceMuted = true;
        if (_ambienceMuteCoroutine != null) StopCoroutine(_ambienceMuteCoroutine);
        _ambienceMuteCoroutine = StartCoroutine(AmbienceMuteRoutine(fadeDuration));
    }

    /// <summary>Releases the ambience mute (unpauses and restores the swept sources).</summary>
    public void UnmuteAmbience() {
        if (!_ambienceMuted) return;
        _ambienceMuted = false;
        if (_ambienceMuteCoroutine != null) { StopCoroutine(_ambienceMuteCoroutine); _ambienceMuteCoroutine = null; }

        for (int i = 0; i < _mutedAmbience.Count; i++) {
            AudioSource source = _mutedAmbience[i];
            if (source == null) continue;
            source.volume = _mutedAmbienceVolumes[i];
            source.UnPause();
        }
        _mutedAmbience.Clear();
        _mutedAmbienceVolumes.Clear();
    }

    private IEnumerator AmbienceMuteRoutine(float fadeDuration) {
        // First sweep: fade out whatever is playing right now.
        SweepAmbience(fadeDuration);

        // Hold: the ending keeps igniting new fires (zones near the portal) —
        // re-scan periodically and cut any newcomer instantly.
        float rescan = 0f;
        while (_ambienceMuted) {
            rescan += Time.unscaledDeltaTime;
            if (rescan >= _ambienceRescanInterval) {
                rescan = 0f;
                SweepAmbience(0f);
            }
            yield return null;
        }
    }

    private void SweepAmbience(float fadeDuration) {
        foreach (AudioSource source in FindObjectsOfType<AudioSource>()) {
            if (source == null || source == _sfxSource || _mutedAmbience.Contains(source)) continue;
            if (MusicDirector.Instance != null && MusicDirector.Instance.OwnsSource(source)) continue;
            if (source.clip == null) continue;

            _mutedAmbience.Add(source);
            _mutedAmbienceVolumes.Add(source.volume);
            if (fadeDuration <= 0f) {
                source.volume = 0f;
                source.Pause();
            } else {
                StartCoroutine(AudioFades.Volume(source, 0f, fadeDuration, () => {
                    if (source != null && _ambienceMuted) source.Pause();
                }));
            }
        }
    }

    // ── Background mute (music + tracked loops) ────────────────────────────────

    /// <summary>Fades the music and all tracked 3D loops to silence, leaving one-shot SFX intact.</summary>
    public void MuteBackground(float fadeDuration = -1f) {
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        MusicDirector.MuteForEvent(duration);
        BackgroundTracking.SetMuted(true, duration, this);
    }

    /// <summary>Restores the music and all tracked 3D loops to their original volumes.</summary>
    public void UnmuteBackground(float fadeDuration = -1f) {
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        MusicDirector.RestoreAfterEvent(duration);
        BackgroundTracking.SetMuted(false, duration, this);
    }

    // ── Stethoscope mode (silence the world, keep the safe audible) ────────────

    /// <summary>
    /// Silences the whole soundscape (Music and SFX buses) while leaving the Safe
    /// mixer group untouched, so safe-dial sounds stay audible. Implemented with
    /// exposed mixer volume parameters — every source routed through the mixer
    /// is silenced, including ones spawned later; no scene sweeping required.
    /// Restores the pre-mode saved volumes on exit.
    /// </summary>
    public void EnterStethoscopeMode(float fadeDuration = -1f) {
        AudioMixer mixer = ResolveMixer();
        if (mixer == null || _stethoscopeActive) return;
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;

        _stethoscopeActive = true;
        MusicDirector.MuteForEvent(duration);
        BackgroundTracking.SetMuted(true, duration, this);

        StartStethoscopeFade(mixer, true, duration);
    }

    /// <summary>Fades the world's volume back and ends stethoscope mode.</summary>
    public void ExitStethoscopeMode(float fadeDuration = -1f) {
        AudioMixer mixer = ResolveMixer();
        if (mixer == null || !_stethoscopeActive) return;
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;

        _stethoscopeActive = false;
        MusicDirector.RestoreAfterEvent(duration);
        BackgroundTracking.SetMuted(false, duration, this);

        StartStethoscopeFade(mixer, false, duration);
    }

    /// <summary>True while stethoscope mode is active (safe sounds bypass the world's mute).</summary>
    public bool StethoscopeActive => _stethoscopeActive;

    /// <summary>
    /// Plays a one-shot on a dedicated source routed to the Safe mixer group —
    /// the only soundscape that stays audible in stethoscope mode.
    /// </summary>
    public void PlaySafeSFX(AudioClip clip, float volume = 1f) {
        if (clip == null) return;
        EnsureSafeSfxSource();
        _safeSfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>True when the source belongs to the Safe mixer group.</summary>
    public bool BelongsToSafeGroup(AudioSource source) =>
        source != null && _safeGroup != null && source.outputAudioMixerGroup == _safeGroup;

    /// <summary>
    /// Routes a source (e.g. the safe's background layer) to the Safe mixer group,
    /// the only soundscape that stays audible in stethoscope mode. No-op when the
    /// Safe group is unassigned in the inspector.
    /// </summary>
    public void RouteSourceToSafeGroup(AudioSource source) {
        if (source != null && _safeGroup != null)
            source.outputAudioMixerGroup = _safeGroup;
    }

    /// <summary>Creates the dedicated Safe-group source lazily (DontDestroyOnLoad manager).</summary>
    private void EnsureSafeSfxSource() {
        if (_safeSfxSource != null) return;
        GameObject obj = new GameObject("Safe_SFX");
        obj.transform.SetParent(transform);
        _safeSfxSource = obj.AddComponent<AudioSource>();
        _safeSfxSource.playOnAwake = false;
        _safeSfxSource.spatialBlend = 0f;
        _safeSfxSource.outputAudioMixerGroup = _safeGroup;
    }

    /// <summary>
    /// Tracks loop sources that follow the background mute state, independent of
    /// the manager lifecycle so destroyed scene sources clean up safely.
    /// </summary>
    private static class BackgroundTracking {
        private class TrackedLoop {
            public AudioSource source;
            public float originalVolume;
        }

        private static readonly List<TrackedLoop> trackedLoops = new List<TrackedLoop>();
        private static bool muted;

        /// <summary>Clears static mute state on singleton creation — survives Play Mode restarts in the editor.</summary>
        public static void ResetForNewSession() {
            trackedLoops.Clear();
            muted = false;
        }

        public static void Register(AudioSource source, float volume) {
            if (source == null) return;
            trackedLoops.Add(new TrackedLoop { source = source, originalVolume = volume });
            if (muted) source.volume = 0f;
        }

        public static void Unregister(AudioSource source) {
            trackedLoops.RemoveAll(loop => loop.source == source);
        }

        /// <summary>Fades all tracked loops to silence or back to their original volumes.</summary>
        public static void SetMuted(bool value, float duration, AudioManager owner) {
            muted = value;
            trackedLoops.RemoveAll(loop => loop.source == null);

            float[] startVolumes = new float[trackedLoops.Count];
            for (int i = 0; i < trackedLoops.Count; i++)
                startVolumes[i] = trackedLoops[i].source.volume;

            owner.StartCoroutine(FadeLoops(startVolumes, duration));
        }

        private static IEnumerator FadeLoops(float[] startVolumes, float duration) {
            float[] endVolumes = new float[trackedLoops.Count];
            for (int i = 0; i < trackedLoops.Count; i++)
                endVolumes[i] = muted ? 0f : trackedLoops[i].originalVolume;

            float elapsed = 0f;
            while (elapsed < duration) {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < trackedLoops.Count; i++)
                    if (trackedLoops[i].source != null)
                        trackedLoops[i].source.volume = Mathf.Lerp(startVolumes[i], endVolumes[i], t);
                yield return null;
            }

            for (int i = 0; i < trackedLoops.Count; i++)
                if (trackedLoops[i].source != null)
                    trackedLoops[i].source.volume = endVolumes[i];
        }
    }
}
