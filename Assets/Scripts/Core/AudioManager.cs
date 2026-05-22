using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    // ── Background mute state ──────────────────────────────────────────────────

    private bool _backgroundMuted;
    private class TrackedLoop {
        public AudioSource source;
        public float originalVolume;
    }
    private readonly List<TrackedLoop> _trackedLoops = new List<TrackedLoop>();
    private Coroutine _muteCoroutine;
    private AudioSource _activeMusicSource;

    // ── Background layer state ─────────────────────────────────────────────────

    private AudioSource _backgroundLayerSource;
    private float _backgroundLayerTargetVolume;
    private Coroutine _layerCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
            return;
        }
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
        
        StopMuteCoroutine();

        // Перед началом плавного перехода перезапускаем музыку меню с нуля
        if (_menuMusicSource != null) {
            _menuMusicSource.Stop();
            _menuMusicSource.Play();
        }

        // Запускаем плавный переход (игровая музыка просто затихнет, но продолжит играть)
        StartCoroutine(FadeBetweenSources(_menuMusicSource, _gameMusicSource));
    }
    public void PlayGameMusic() {
        _activeMusicSource = _gameMusicSource;
        if (_backgroundMuted) return; // Stay silent if muted, but update target

        StopMuteCoroutine();
        
        // При возврате в игру просто плавно выводим громкость игрового источника
        StartCoroutine(FadeBetweenSources(_gameMusicSource, _menuMusicSource));
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
        source.Play();

        _trackedLoops.Add(new TrackedLoop { source = source, originalVolume = volume });
        return source;
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

        // Determine targets based on active source and multiplier
        float targetMenuVol = (targetMultiplier > 0f && _activeMusicSource == _menuMusicSource) ? _backMusicVolume : 0f;
        float targetGameVol = (targetMultiplier > 0f && _activeMusicSource == _gameMusicSource) ? _backMusicVolume : 0f;

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
                targetSource.volume = Mathf.Lerp(startTargetVol, _backMusicVolume, normalizedTime);

            if (currentSource != null)
                currentSource.volume = Mathf.Lerp(startCurrentVol, 0.0f, normalizedTime);

            yield return null;
        }

        if (targetSource != null) targetSource.volume = _backMusicVolume;
        if (currentSource != null) currentSource.volume = 0.0f;

        if (currentSource == _menuMusicSource) {
            currentSource.Stop();
        }
    }

}