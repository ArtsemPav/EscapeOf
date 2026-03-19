using UnityEngine;
using System.Collections;

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
        // Перед началом плавного перехода перезапускаем музыку меню с нуля
        if (_menuMusicSource != null) {
            _menuMusicSource.Stop();
            _menuMusicSource.time = 0; // Сброс времени воспроизведения
            _menuMusicSource.Play();
        }

        // Запускаем плавный переход (игровая музыка просто затихнет, но продолжит играть)
        StartCoroutine(FadeBetweenSources(_menuMusicSource, _gameMusicSource));
    }
    public void PlayGameMusic() {
        // При возврате в игру просто плавно выводим громкость игрового источника
        StartCoroutine(FadeBetweenSources(_gameMusicSource, _menuMusicSource));
    }

    public void PlaySFX(AudioClip clip) {
        if (clip != null)
            _sfxSource.PlayOneShot(clip);
    }

    public AudioSource Play3DLoop(AudioClip clip, Transform target, float volume, float minDistance, float maxDistance) {
        GameObject sfxObj = new GameObject("3D_Loop_SFX");
        sfxObj.transform.position = target.position;
        sfxObj.transform.SetParent(target);

        AudioSource source = sfxObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f; // Полное 3D
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.loop = true;
        source.playOnAwake = false;
        source.Play();

        return source;
    }

    private IEnumerator FadeBetweenSources(AudioSource targetSource, AudioSource currentSource) {
        float t = 0;
        float startTargetVol = targetSource.volume;
        float startCurrentVol = currentSource.volume;

        while (t < _fadeDuration) {
            t += Time.unscaledDeltaTime; // Работает даже при Time.timeScale = 0
            float normalizedTime = t / _fadeDuration;

            if (targetSource != null)
                targetSource.volume = Mathf.Lerp(startTargetVol, _backMusicVolume, normalizedTime);

            if (currentSource != null)
                currentSource.volume = Mathf.Lerp(startCurrentVol, 0.0f, normalizedTime);

            yield return null;
        }

        if (targetSource != null) targetSource.volume = _backMusicVolume;
        if (currentSource != null) currentSource.volume = 0.0f;

        // Опционально: полностью останавливаем источник меню, когда он затих в игре,
        // чтобы не тратить ресурсы, так как при следующем открытии меню он все равно перезапустится.
        if (currentSource == _menuMusicSource) {
            currentSource.Stop();
        }
    }

}