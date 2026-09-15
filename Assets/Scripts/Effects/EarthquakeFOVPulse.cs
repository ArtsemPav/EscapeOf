using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Эффект землетрясения: мелкое частое дрожание камеры через Perlin-шум Cinemachine
/// и лёгкий быстрый пульс FOV для дезориентации. Запускается методом <see cref="Play"/>
/// (например, из UnityEvent хоррор-события). По завершении возвращает камеру в исходное состояние.
/// </summary>
public class EarthquakeFOVPulse : MonoBehaviour
{
    [Header("Shake (Perlin noise)")]
    [Tooltip("Множитель амплитуды шума во время землетрясения. Итоговая амплитуда = амплитуды профиля × это значение.")]
    [SerializeField, Min(0f)] private float _shakeAmplitude = 1f;

    [Tooltip("Множитель частоты шума во время землетрясения. Больше — дрожание быстрее.")]
    [SerializeField, Min(0f)] private float _shakeFrequency = 1.2f;

    [Header("FOV Pulse")]
    [Tooltip("Насколько FOV отклоняется от исходного значения (в градусах, ±).")]
    [SerializeField, Min(0f)] private float _fovAmplitude = 6f;

    [Tooltip("Количество колебаний FOV в секунду.")]
    [SerializeField, Min(0.1f)] private float _pulseFrequency = 4.5f;

    [Header("Timing")]
    [Tooltip("Общая длительность эффекта в секундах.")]
    [SerializeField, Min(0.1f)] private float _duration = 10f;

    [Tooltip("Длительность плавного нарастания эффекта в секундах.")]
    [SerializeField, Min(0f)] private float _fadeIn = 0.5f;

    [Tooltip("Длительность плавного затухания эффекта в секундах.")]
    [SerializeField, Min(0f)] private float _fadeOut = 1.5f;

    private CinemachineCamera _playerCinemachineCamera;
    private CinemachineBasicMultiChannelPerlin _noise;
    private Camera _mainCamera;
    private float _baseFov;
    private Coroutine _effectRoutine;

    private void Awake()
    {
        CacheCamera();
    }

    private void OnDisable()
    {
        StopEffect();
    }

    /// <summary>
    /// Запустить землетрясение с параметрами из Inspector.
    /// </summary>
    public void Play()
    {
        CacheCamera();

        if (_mainCamera == null)
        {
            Debug.LogWarning($"[{nameof(EarthquakeFOVPulse)}] Main camera not found — effect skipped.", this);
            return;
        }

        if (_effectRoutine != null)
            StopCoroutine(_effectRoutine);

        _effectRoutine = StartCoroutine(EffectRoutine());
    }

    /// <summary>
    /// Запустить землетрясение с пользовательской длительностью.
    /// </summary>
    /// <param name="duration">Длительность эффекта (секунды).</param>
    public void Play(float duration)
    {
        _duration = duration;
        Play();
    }

    private void CacheCamera()
    {
        _mainCamera = Camera.main;

        if (_mainCamera == null)
            return;

        // Игровая CinemachineCamera может не быть родителем Camera.main — ищем по объекту "PlayerCamera".
        _playerCinemachineCamera = _mainCamera.GetComponentInParent<CinemachineCamera>();
        if (_playerCinemachineCamera == null)
        {
            GameObject playerCameraObject = GameObject.Find("PlayerCamera");
            if (playerCameraObject != null)
                _playerCinemachineCamera = playerCameraObject.GetComponent<CinemachineCamera>();
        }

        _noise = _playerCinemachineCamera != null
            ? _playerCinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>()
            : null;
        _baseFov = _playerCinemachineCamera != null
            ? _playerCinemachineCamera.Lens.FieldOfView
            : _mainCamera.fieldOfView;
    }

    private IEnumerator EffectRoutine()
    {
        float elapsed = 0f;

        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;

            // Огибающая: плавный вход → полное дрожание → плавный выход.
            float fadeIn = _fadeIn > 0f ? Mathf.Clamp01(elapsed / _fadeIn) : 1f;
            float fadeOut = _fadeOut > 0f ? Mathf.Clamp01((_duration - elapsed) / _fadeOut) : 1f;
            float envelope = Mathf.Min(fadeIn, fadeOut);

            if (_noise != null)
            {
                _noise.AmplitudeGain = _shakeAmplitude * envelope;
                _noise.FrequencyGain = _shakeFrequency;
            }

            if (_fovAmplitude > 0f)
            {
                float fovOffset = Mathf.Sin(elapsed * _pulseFrequency * Mathf.PI * 2f) * _fovAmplitude * envelope;
                ApplyFov(_baseFov + fovOffset);
            }

            yield return null;
        }

        ResetCamera();
        _effectRoutine = null;
    }

    private void ApplyFov(float fov)
    {
        if (_playerCinemachineCamera != null)
        {
            var lens = _playerCinemachineCamera.Lens;
            lens.FieldOfView = fov;
            _playerCinemachineCamera.Lens = lens;
        }
        else
        {
            _mainCamera.fieldOfView = fov;
        }
    }

    private void ResetCamera()
    {
        if (_noise != null)
        {
            _noise.AmplitudeGain = 0f;
        }

        if (_mainCamera != null)
        {
            ApplyFov(_baseFov);
        }
    }

    private void StopEffect()
    {
        if (_effectRoutine != null)
        {
            StopCoroutine(_effectRoutine);
            _effectRoutine = null;
            ResetCamera();
        }
    }
}
