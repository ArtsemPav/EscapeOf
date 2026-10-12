using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour {
    public static AudioManager Instance { get; private set; }

    [Header("Источники звука")]
    [SerializeField] private AudioSource _menuMusicSource;
    [SerializeField] private AudioSource _gameMusicSource;
    [SerializeField] private AudioSource _sfxSource;

    [Header("Аудио клипы")]
    public AudioClip _menuMusic;
    public AudioClip _gameMusic;

    [Header("Настройки")]
    [SerializeField] private float _backMusicVolume = 0.4f;
    [SerializeField] private float _fadeDuration = 1.5f; // Время затухания

    [Tooltip("SFX AudioMixer with the exposed SFXVolume parameter. Optional — falls back to the SFX source's mixer group.")]
    [SerializeField] private AudioMixer _sfxMixer;
    [Tooltip("Name of the exposed volume parameter on the SFX AudioMixer.")]
    [SerializeField] private string _sfxVolumeParameter = "SFXVolume";
    [Tooltip("Mixer group all dynamically created sound-effect sources are routed to.")]
    [SerializeField] private AudioMixerGroup _sfxGroup;

    private string SfxVolumeParameter => _sfxVolumeParameter;

    /// <summary>Routes a dynamically created source to the SFX mixer group so the SFX volume setting applies to it.</summary>
    private void RouteToSfxGroup(AudioSource source) {
        if (source != null && _sfxGroup != null)
            source.outputAudioMixerGroup = _sfxGroup;
    }

    public const string MusicVolumePrefKey = "Settings_MusicVolume";

    /// <summary>Пользовательская громкость музыки (0–1), сохраняется в PlayerPrefs.</summary>
    public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumePrefKey, 1f);

    /// <summary>Громкость музыки с учётом пользовательской настройки.</summary>
    private float BackMusicVolume => _backMusicVolume * MusicVolume;

    // ── Background mute state ──────────────────────────────────────────────────

    private bool _backgroundMuted;
    private class TrackedLoop {
        public AudioSource source;
        public float originalVolume;
    }
    private readonly List<TrackedLoop> _trackedLoops = new List<TrackedLoop>();
    private Coroutine _muteCoroutine;
    private Coroutine _musicFadeCoroutine;
    private AudioSource _activeMusicSource;

    // ── Background layer state ─────────────────────────────────────────────────

    private AudioSource _backgroundLayerSource;
    private float _backgroundLayerTargetVolume;
    private Coroutine _layerCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ApplySavedSfxVolume();
        } else {
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>Applies the saved SFX volume (PlayerPrefs) to the SFX AudioMixer.</summary>
    public void ApplySavedSfxVolume() {
        AudioMixer mixer = _sfxMixer != null ? _sfxMixer : (AudioMixer)(_sfxSource != null && _sfxSource.outputAudioMixerGroup != null ? _sfxSource.outputAudioMixerGroup.audioMixer : null);
        if (mixer != null)
            mixer.SetFloat(SfxVolumeParameter, SystemSettings.ToDecibels(SystemSettings.SfxVolume));
    }

    private void Start() {
        // Запускаем оба трека сразу на громкости 0
        if (_menuMusic != null) {
            _menuMusicSource.clip = _menuMusic;
            _menuMusicSource.loop = true;
            _menuMusicSource.volume = 0;
            _menuMusicSource.Play();
        }
        if (_gameMusic != null) {
            _gameMusicSource.clip = _gameMusic;
            _gameMusicSource.loop = true;
            _gameMusicSource.volume = 0;
            _gameMusicSource.Play();
        }
    }

    // Удобные методы-обертки
    public void PlayMenuMusic() {
        _activeMusicSource = _menuMusicSource;
        if (_backgroundMuted) return; // Stay silent if muted, but update target

        // When the MusicDirector owns the in-game music (e.g. a music zone
        // trigger switched the track), pause its track — otherwise both keep
        // playing during pause. The legacy game music source is faded out too,
        // so any of the two in-game layers can be active without conflict.
        if (MusicDirector.Instance != null) {
            MusicDirector.PauseForMenu();
            FadeInMenuMusic();
            StartCoroutine(FadeGameSourceOut());
            return;
        }

        StopMuteCoroutine();
        FadeInMenuMusic();

        // Запускаем плавный переход (игровая музыка просто затихнет, но продолжит играть)
        StartCoroutine(FadeBetweenSources(_menuMusicSource, _gameMusicSource));
    }
    public void PlayGameMusic() {
        _activeMusicSource = _gameMusicSource;
        if (_backgroundMuted) return; // Stay silent if muted, but update target

        // The MusicDirector track owns the in-game music — restore it instead
        // of fading the legacy game music source back up. Checked by Instance
        // rather than the active track: the director's Start may not have run
        // yet when this is called from GameManager.SetPause on scene load.
        if (MusicDirector.Instance != null) {
            MusicDirector.ResumeForGameplay();
            FadeOutMenuMusic();
            return;
        }

        StopMuteCoroutine();

        // При возврате в игру просто плавно выводим громкость игрового источника
        StartCoroutine(FadeBetweenSources(_gameMusicSource, _menuMusicSource));
    }

    /// <summary>Restarts the menu music from the beginning and fades it in to full volume.</summary>
    private void FadeInMenuMusic() {
        if (_menuMusicSource == null) return;
        _menuMusicSource.Stop();
        _menuMusicSource.Play();
        StartCoroutine(FadeMenuSource(_menuMusicSource, 0f, BackMusicVolume));
    }

    /// <summary>Fades the menu music out and stops it when it is fully silent.</summary>
    private void FadeOutMenuMusic() {
        if (_menuMusicSource == null) return;
        StartCoroutine(FadeMenuSource(_menuMusicSource, _menuMusicSource.volume, 0f, stopWhenSilent: true));
    }

    /// <summary>Fades the legacy in-game music source to silence without stopping it.</summary>
    private IEnumerator FadeGameSourceOut() {
        if (_gameMusicSource == null) yield break;
        float from = _gameMusicSource.volume;
        float elapsed = 0f;
        while (elapsed < _fadeDuration && _gameMusicSource != null) {
            elapsed += Time.unscaledDeltaTime;
            _gameMusicSource.volume = Mathf.Lerp(from, 0f, Mathf.Clamp01(elapsed / _fadeDuration));
            yield return null;
        }
        if (_gameMusicSource != null) _gameMusicSource.volume = 0f;
    }

    private IEnumerator FadeMenuSource(AudioSource source, float from, float to, bool stopWhenSilent = false) {
        float elapsed = 0f;
        while (elapsed < _fadeDuration && source != null) {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / _fadeDuration));
            yield return null;
        }
        if (source != null) {
            source.volume = to;
            if (stopWhenSilent) source.Stop();
        }
    }

    private void StopMuteCoroutine() {
        if (_muteCoroutine != null) {
            StopCoroutine(_muteCoroutine);
            _muteCoroutine = null;
        }
    }

    public void PlaySFX(AudioClip clip) => PlaySFX(clip, 1f);
    public void PlaySFX(AudioClip clip, float volume) {
        if (clip != null)
            _sfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Проигрывает SFX-клип, предварительно остановив все звуки на _sfxSource.
    /// Используй для звуков, которые не должны накладываться сами на себя
    /// (например, повторяющийся звук успеха в мини-игре).
    /// </summary>
    public void PlaySFXExclusive(AudioClip clip, float volume = 1f) {
        if (clip == null) return;
        _sfxSource.Stop();
        _sfxSource.PlayOneShot(clip, volume);
    }

    public AudioSource Play3DLoop(AudioClip clip, Transform target, float volume, float minDistance, float maxDistance) {
        GameObject sfxObj = new GameObject("3D_Loop_SFX");
        sfxObj.transform.position = target.position;
        sfxObj.transform.SetParent(target);

        AudioSource source = sfxObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = _backgroundMuted ? 0f : volume;
        source.spatialBlend = 1f; // Полное 3D
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.loop = true;
        source.playOnAwake = false;
        RouteToSfxGroup(source);
        source.Play();

        _trackedLoops.Add(new TrackedLoop { source = source, originalVolume = volume });
        return source;
    }

    /// <summary>
    /// Registers an existing AudioSource in the mute-tracking system so it is
    /// automatically faded with MuteBackground / UnmuteBackground.
    /// Use this when the AudioSource is placed directly in a prefab (not created by Play3DLoop).
    /// The source must already be configured (clip, loop, 3D settings) and will start playing immediately.
    /// </summary>
    public void RegisterLoopSource(AudioSource source, float volume) {
        if (source == null) return;
        source.volume = _backgroundMuted ? 0f : volume;
        _trackedLoops.Add(new TrackedLoop { source = source, originalVolume = volume });
    }

    /// <summary>
    /// Unregisters a previously registered AudioSource from the mute-tracking system
    /// and stops playback. Safe to call if the source was never registered.
    /// </summary>
    public void UnregisterLoopSource(AudioSource source) {
        if (source == null) return;
        _trackedLoops.RemoveAll(l => l.source == source);
        source.Stop();
    }

    // ── Dynamic music source API (used by MusicDirector) ──────────────────────

    /// <summary>
    /// Creates a dedicated looping AudioSource under the manager for a
    /// dynamically requested music track and starts playback at volume 0.
    /// Used by MusicDirector for crossfading between arbitrary tracks.
    /// </summary>
    public AudioSource PlayNewMusicSource(AudioClip clip, bool loop) {
        GameObject obj = new GameObject("DynamicMusic_SFX");
        obj.transform.SetParent(transform);

        AudioSource source = obj.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.playOnAwake = false;
        source.Play();

        return source;
    }

    /// <summary>
    /// Stops a dynamic music source created by PlayNewMusicSource without
    /// destroying it — MusicDirector keeps sources pooled and paused so the
    /// track can resume from the position where it faded out.
    /// </summary>
    public void ReleaseMusicSource(AudioSource source) {
        if (source == null) return;
        source.Pause();
    }


    // ── Background layer API ───────────────────────────────────────────────────

    /// <summary>
    /// Plays a looping background layer (e.g. heartbeat, ambient hum) that fades in
    /// independently of the main music. The layer is not affected by MuteBackground.
    /// Returns the AudioSource so the caller can hold a reference if needed.
    /// </summary>
    public AudioSource PlayBackgroundLayer(AudioClip clip, float volume = 1f, float fadeDuration = -1f)
    {
        StopBackgroundLayer(fadeDuration);

        GameObject obj = new GameObject("BackgroundLayer_SFX");
        obj.transform.SetParent(transform);

        _backgroundLayerSource = obj.AddComponent<AudioSource>();
        _backgroundLayerSource.clip        = clip;
        _backgroundLayerSource.loop        = true;
        _backgroundLayerSource.spatialBlend = 0f; // 2D
        _backgroundLayerSource.volume      = 0f;
        _backgroundLayerSource.playOnAwake = false;
        RouteToSfxGroup(_backgroundLayerSource);
        _backgroundLayerSource.Play();

        _backgroundLayerTargetVolume = volume;
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        RestartLayerCoroutine(FadeLayerVolume(_backgroundLayerSource, 0f, volume, duration));

        return _backgroundLayerSource;
    }

    /// <summary>Fades out and destroys the currently playing background layer.</summary>
    public void StopBackgroundLayer(float fadeDuration = -1f)
    {
        if (_backgroundLayerSource == null) return;

        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        AudioSource source = _backgroundLayerSource;
        _backgroundLayerSource = null;
        RestartLayerCoroutine(FadeLayerVolumeAndDestroy(source, source.volume, 0f, duration));
    }

    private void RestartLayerCoroutine(IEnumerator routine)
    {
        if (_layerCoroutine != null) StopCoroutine(_layerCoroutine);
        _layerCoroutine = StartCoroutine(routine);
    }

    private IEnumerator FadeLayerVolume(AudioSource source, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && source != null)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        if (source != null) source.volume = to;
        _layerCoroutine = null;
    }

    private IEnumerator FadeLayerVolumeAndDestroy(AudioSource source, float from, float to, float duration)
    {
        yield return FadeLayerVolume(source, from, to, duration);
        if (source != null) Destroy(source.gameObject);
        _layerCoroutine = null;
    }

    // ── Background mute API ────────────────────────────────────────────────────

    /// <summary>
    /// Fades only the game/menu music sources to a target multiplier (0 = silent, 1 = full).
    /// Does NOT affect tracked 3D loops or SFX — used by ElevatorController to duck
    /// the music while the player is inside the elevator.
    /// Also ducks the MusicDirector track, which owns the in-game music.
    /// </summary>
    public void FadeMusicVolume(float targetMultiplier, float fadeDuration = -1f)
    {
        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        if (_musicFadeCoroutine != null) StopCoroutine(_musicFadeCoroutine);
        _musicFadeCoroutine = StartCoroutine(FadeMusicOnly(targetMultiplier, duration));
        MusicDirector.DuckActiveTrack(targetMultiplier, duration);
    }

    private IEnumerator FadeMusicOnly(float targetMultiplier, float duration)
    {
        // When the MusicDirector owns the music, the legacy sources stay at
        // zero — raising them here would stack a second copy of the track
        // (e.g. on every elevator exit).
        bool directorOwnsMusic = MusicDirector.Instance != null;

        float startMenuVol = _menuMusicSource != null ? _menuMusicSource.volume : 0f;
        float startGameVol = _gameMusicSource != null ? _gameMusicSource.volume : 0f;

        float targetMenuVol = !directorOwnsMusic && targetMultiplier > 0f && _activeMusicSource == _menuMusicSource ? BackMusicVolume : 0f;
        float targetGameVol = !directorOwnsMusic && targetMultiplier > 0f && _activeMusicSource == _gameMusicSource ? BackMusicVolume : 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (_menuMusicSource != null)
                _menuMusicSource.volume = Mathf.Lerp(startMenuVol, targetMenuVol, t);
            if (_gameMusicSource != null)
                _gameMusicSource.volume = Mathf.Lerp(startGameVol, targetGameVol, t);

            yield return null;
        }

        if (_menuMusicSource != null) _menuMusicSource.volume = targetMenuVol;
        if (_gameMusicSource != null) _gameMusicSource.volume = targetGameVol;
        _musicFadeCoroutine = null;
    }

    /// <summary>Fades out background music and all tracked 3D loops, leaving SFX intact.</summary>
    public void MuteBackground(float fadeDuration = -1f)
    {
        if (_backgroundMuted) return;
        _backgroundMuted = true;

        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        RestartMuteCoroutine(FadeBackgroundVolume(0f, duration));
    }

    /// <summary>Restores background music and all tracked 3D loops to their original volumes.</summary>
    public void UnmuteBackground(float fadeDuration = -1f)
    {
        if (!_backgroundMuted) return;
        _backgroundMuted = false;

        float duration = fadeDuration < 0f ? _fadeDuration : fadeDuration;
        RestartMuteCoroutine(FadeBackgroundVolume(1f, duration));
    }

    private void RestartMuteCoroutine(IEnumerator routine)
    {
        StopMuteCoroutine();
        _muteCoroutine = StartCoroutine(routine);
    }

    private IEnumerator FadeBackgroundVolume(float targetMultiplier, float duration)
    {
        float startMenuVol = _menuMusicSource != null ? _menuMusicSource.volume : 0f;
        float startGameVol = _gameMusicSource != null ? _gameMusicSource.volume : 0f;

        // When the MusicDirector owns the music, the legacy sources stay at
        // zero — UnmuteBackground must not raise a second copy of the track.
        bool directorOwnsMusic = MusicDirector.Instance != null;

        // Determine targets based on active source and multiplier
        float targetMenuVol = !directorOwnsMusic && targetMultiplier > 0f && _activeMusicSource == _menuMusicSource ? BackMusicVolume : 0f;
        float targetGameVol = !directorOwnsMusic && targetMultiplier > 0f && _activeMusicSource == _gameMusicSource ? BackMusicVolume : 0f;

        // Snapshot 3D loop starting volumes
        _trackedLoops.RemoveAll(l => l.source == null);
        float[] startLoopVols = new float[_trackedLoops.Count];
        for (int i = 0; i < _trackedLoops.Count; i++)
            startLoopVols[i] = _trackedLoops[i].source.volume;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (_menuMusicSource != null)
                _menuMusicSource.volume = Mathf.Lerp(startMenuVol, targetMenuVol, t);
            if (_gameMusicSource != null)
                _gameMusicSource.volume = Mathf.Lerp(startGameVol, targetGameVol, t);

            for (int i = 0; i < _trackedLoops.Count; i++)
            {
                if (_trackedLoops[i].source != null)
                {
                    float loopTarget = targetMultiplier > 0f ? _trackedLoops[i].originalVolume : 0f;
                    _trackedLoops[i].source.volume = Mathf.Lerp(startLoopVols[i], loopTarget, t);
                }
            }

            yield return null;
        }

        // Hard-set final values
        if (_menuMusicSource != null) _menuMusicSource.volume = targetMenuVol;
        if (_gameMusicSource != null) _gameMusicSource.volume = targetGameVol;

        foreach (var loop in _trackedLoops)
        {
            if (loop.source != null)
                loop.source.volume = targetMultiplier > 0f ? loop.originalVolume : 0f;
        }

        _muteCoroutine = null;
    }

    private IEnumerator FadeBetweenSources(AudioSource targetSource, AudioSource currentSource) {
        float t = 0;
        float startTargetVol = targetSource.volume;
        float startCurrentVol = currentSource.volume;

        while (t < _fadeDuration) {
            t += Time.unscaledDeltaTime;
            float normalizedTime = t / _fadeDuration;

            if (targetSource != null)
                targetSource.volume = Mathf.Lerp(startTargetVol, BackMusicVolume, normalizedTime);

            if (currentSource != null)
                currentSource.volume = Mathf.Lerp(startCurrentVol, 0.0f, normalizedTime);

            yield return null;
        }

        if (targetSource != null) targetSource.volume = BackMusicVolume;
        if (currentSource != null) currentSource.volume = 0.0f;

        if (currentSource == _menuMusicSource) {
            currentSource.Stop();
        }
    }

}