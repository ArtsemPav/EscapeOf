using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Sound-effect service: one-shot SFX, spatial 3D loops and background layers.
/// Owns no music — all music (menu, gameplay, zones, events) is handled by
/// MusicDirector through the Music bus of the SFX AudioMixer.
/// Volume settings are applied to the exposed mixer parameters (SFXVolume /
/// MusicVolume) by SystemSettings, so sliders affect playing audio instantly.
/// </summary>
public class AudioManager : MonoBehaviour {
    public static AudioManager Instance { get; private set; }

    [Header("SFX")]
    [Tooltip("2D source all one-shot sound effects play through.")]
    [SerializeField] private AudioSource _sfxSource;

    [Tooltip("SFX AudioMixer with the exposed SFXVolume and MusicVolume parameters.")]
    [SerializeField] private AudioMixer _sfxMixer;

    [Tooltip("Name of the exposed SFX volume parameter on the AudioMixer.")]
    [SerializeField] private string _sfxVolumeParameter = "SFXVolume";

    [Tooltip("Name of the exposed music volume parameter on the AudioMixer (Music bus).")]
    [SerializeField] private string _musicVolumeParameter = "MusicVolume";

    [Tooltip("Mixer group all dynamically created sound-effect sources are routed to.")]
    [SerializeField] private AudioMixerGroup _sfxGroup;

    [Header("Fades")]
    [Tooltip("Default fade duration in seconds for background layers and mute transitions.")]
    [SerializeField] private float _fadeDuration = 1.5f;

    // ── Background layer state ─────────────────────────────────────────────────

    private AudioSource _backgroundLayerSource;
    private Coroutine _layerCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BackgroundTracking.ResetForNewSession();
            ApplySavedVolumes();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        } else {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>After each scene load, routes every orphan AudioSource (no mixer group) to the SFX bus.</summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) {
        AudioSource[] sources = FindObjectsOfType<AudioSource>();
        foreach (AudioSource source in sources) {
            if (source != null && source.outputAudioMixerGroup == null)
                RouteToSfxGroup(source);
        }
    }

    /// <summary>
    /// Plays a one-shot 3D sound effect from a pooled source parented to the
    /// target (auto-destroys when the clip finishes). Routed to the SFX bus.
    /// </summary>
    public void Play3DOneShot(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 10f) {
        if (clip == null) return;
        GameObject obj = new GameObject("3D_OneShot_SFX");
        obj.transform.position = position;

        AudioSource source = obj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.playOnAwake = false;
        RouteToSfxGroup(source);
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

    /// <summary>Routes a dynamically created source to the SFX mixer group so the SFX volume setting applies to it.</summary>
    private void RouteToSfxGroup(AudioSource source) {
        if (source != null && _sfxGroup != null)
            source.outputAudioMixerGroup = _sfxGroup;
    }

    /// <summary>Public wrapper for component-owned sources (e.g. drag/scrape loops) outside the tracking system.</summary>
    public void RouteSourceToSfxGroup(AudioSource source) => RouteToSfxGroup(source);

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
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.loop = true;
        source.playOnAwake = false;
        RouteToSfxGroup(source);
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
        RouteToSfxGroup(source);
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
        RouteToSfxGroup(_backgroundLayerSource);
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
