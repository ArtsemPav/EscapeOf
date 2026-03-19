using UnityEngine;

/// <summary>
/// Рефакторинг: Мерцание неона, синхронизированное со звуком через AudioManager.
/// Скрипт запрашивает 3D-источник звука у менеджера и анализирует его волновую форму (RMS)
/// для управления интенсивностью света и эмиссией материала.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class NeonLightFlicker : MonoBehaviour {
    [SerializeField] private Light _flickerLight;

    [Header("Audio Settings")]
    [Tooltip("Пул клипов для случайного выбора.")]
    [SerializeField] private AudioClip[] _flickerSounds;
    [SerializeField] [Range(0f, 1f)] private float _soundVolume = 0.8f;
    [SerializeField] private float _minDistance = 1f;
    [SerializeField] private float _maxDistance = 8f;

    [Header("Light Response")]
    [SerializeField] private int _sampleSize = 64;
    [SerializeField] private float _sensitivity = 80f;
    [SerializeField] [Range(0f, 1f)] private float _minIntensity = 0f;
    [SerializeField] [Range(0f, 2f)] private float _maxIntensity = 1f;
    [SerializeField] private float _smoothing = 20f;
    [SerializeField] [Range(0.1f, 5f)] private float _contrast = 2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MeshRenderer _meshRenderer;
    private Material _instanceMaterial;
    private Color _baseEmissionColor;
    private float _baseIntensity;

    private AudioSource _audioSource;
    private float[] _samples;
    private float _currentNormalized;

    private void Awake() {
        _meshRenderer = GetComponent<MeshRenderer>();
        _instanceMaterial = _meshRenderer.material;

        _sampleSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_sampleSize, 64, 8192));
        _samples = new float[_sampleSize];
    }

    private void Start() {
        if (_flickerLight == null) {
            Debug.LogWarning($"[NeonLightFlicker] Light не назначен на '{name}'.", this);
            return;
        }

        _baseIntensity = _flickerLight.intensity;
        _baseEmissionColor = _instanceMaterial.GetColor(EmissionColorId);

        // Запрашиваем 3D-источник у AudioManager
        if (AudioManager.Instance != null && _flickerSounds != null && _flickerSounds.Length > 0) {
            AudioClip randomClip = _flickerSounds[Random.Range(0, _flickerSounds.Length)];

            // Метод Play3DLoop должен быть реализован в AudioManager
            _audioSource = AudioManager.Instance.Play3DLoop(
                randomClip,
                transform,
                _soundVolume,
                _minDistance,
                _maxDistance
            );
        } else {
            Debug.LogWarning($"[NeonLightFlicker] Не удалось инициализировать звук на '{name}'. Проверь AudioManager и массив клипов.", this);
        }
    }

    private void OnDestroy() {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);

        // Если лампа удаляется, удаляем и созданный для неё объект звука
        if (_audioSource != null)
            Destroy(_audioSource.gameObject);
    }

    private void Update() {
        if (_audioSource == null || _audioSource.clip == null) return;

        // Обработка паузы: останавливаем анализ и ставим звук на паузу
        if (Time.timeScale <= 0f) {
            if (_audioSource.isPlaying) _audioSource.Pause();
            return;
        }

        // Возобновляем звук после выхода из паузы
        if (!_audioSource.isPlaying) {
            _audioSource.UnPause();
        }

        // Читаем данные напрямую из клипа в позиции воспроизведения
        _audioSource.clip.GetData(_samples, _audioSource.timeSamples);
        float rms = CalculateRMS(_samples);

        // Рассчитываем целевое значение мерцания
        float target = Mathf.Clamp01(rms * _sensitivity);

        // Используем Time.deltaTime (он > 0 вне паузы)
        _currentNormalized = Mathf.Lerp(_currentNormalized, target, _smoothing * Time.deltaTime);

        // Применяем кривую контрастности и маппинг интенсивности
        float contrasted = Mathf.Pow(_currentNormalized, _contrast);
        float mapped = Mathf.Lerp(_minIntensity, _maxIntensity, contrasted);

        ApplyNormalized(mapped);
    }

    private void ApplyNormalized(float normalized) {
        _flickerLight.intensity = _baseIntensity * normalized;
        _instanceMaterial.SetColor(EmissionColorId, _baseEmissionColor * normalized);
    }

    private static float CalculateRMS(float[] samples) {
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];
        return Mathf.Sqrt(sum / samples.Length);
    }
}
