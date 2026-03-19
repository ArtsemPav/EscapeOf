using UnityEngine;
using System.Collections;

public class AudioManager : MonoBehaviour {
    public static AudioManager Instance { get; private set; }

    [Header("Источники звука")]
    [SerializeField] private AudioSource _musicSource;
    public AudioSource _sfxSource;

    [Header("Аудио клипы")]
    public AudioClip _menuMusic;
    public AudioClip _gameMusic;

    [Header("Настройки")]
    [SerializeField] private float _fadeDuration = 1.5f; // Время затухания

    private Coroutine _fadeCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
            return;
        }
    }

    public void PlayMusic(AudioClip newClip) {
        if (newClip == null) return;

        // Если уже играет эта же музыка, ничего не делаем
        if (_musicSource.clip == newClip && _musicSource.isPlaying) return;

        // Останавливаем предыдущую корутину, если она была
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        // Запускаем плавную смену трека
        _fadeCoroutine = StartCoroutine(FadeMusic(newClip));
    }

    private IEnumerator FadeMusic(AudioClip newClip) {
        float startVolume = _musicSource.volume; // Убедись, что стартовая громкость не 0

        // Используем Time.unscaledDeltaTime вместо Time.deltaTime
        for (float t = 0; t < _fadeDuration; t += Time.unscaledDeltaTime) {
            _musicSource.volume = Mathf.Lerp(startVolume, 0, t / _fadeDuration);
            yield return null;
        }

        _musicSource.volume = 0;
        _musicSource.Stop();
        _musicSource.clip = newClip;
        _musicSource.Play();

        for (float t = 0; t < _fadeDuration; t += Time.unscaledDeltaTime) {
            _musicSource.volume = Mathf.Lerp(0, startVolume, t / _fadeDuration);
            yield return null;
        }

        _musicSource.volume = startVolume;
    }

    // Удобные методы-обертки
    public void PlayMenuMusic() => PlayMusic(_menuMusic);
    public void PlayGameMusic() => PlayMusic(_gameMusic);

    public void PlaySFX(AudioClip clip) {
        if (clip != null)
            _sfxSource.PlayOneShot(clip);
    }
}