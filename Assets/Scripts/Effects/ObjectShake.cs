using System.Collections;
using UnityEngine;

/// <summary>
/// Эффект дрожания (тряски) объекта. Поддерживает два режима:
/// постоянное дрожание (например, работающий двигатель) и импульсное —
/// однократная тряска с затуханием (например, удар, ошибка).
/// Тряска применяется к localPosition и/или localRotation относительно
/// начальной позы, что позволяет использовать компонент на дочерних объектах.
/// </summary>
public class ObjectShake : MonoBehaviour
{
    private const float DefaultImpulseDuration = 0.5f;
    private const float FrequencyMultiplier = 30f;

    [Header("Continuous Shake")]
    [Tooltip("Постоянное дрожание включено.")]
    [SerializeField] private bool _shakeContinuously;

    [Tooltip("Амплитуда постоянного дрожания по позициям (метры).")]
    [SerializeField] private float _continuousPositionAmplitude = 0.01f;

    [Tooltip("Амплитуда постоянного дрожания по вращению (градусы).")]
    [SerializeField] private float _continuousRotationAmplitude = 0.3f;

    [Tooltip("Частота постоянного дрожания (Гц).")]
    [SerializeField] private float _continuousFrequency = 25f;

    [Header("Impulse Shake")]
    [Tooltip("Амплитуда импульсной тряски по позициям (метры).")]
    [SerializeField] private float _impulsePositionAmplitude = 0.05f;

    [Tooltip("Амплитуда импульсной тряски по вращению (градусы).")]
    [SerializeField] private float _impulseRotationAmplitude = 2f;

    [Tooltip("Длительность импульсной тряски (секунды).")]
    [SerializeField] private float _impulseDuration = DefaultImpulseDuration;

    [Tooltip("Частота импульсной тряски (Гц).")]
    [SerializeField] private float _impulseFrequency = 40f;

    [Header("Axes")]
    [Tooltip("Какие оси позиции затрагивает тряска.")]
    [SerializeField] private Vector3 _positionAxes = new Vector3(1f, 1f, 1f);

    [Tooltip("Какие оси вращения затрагивает тряска.")]
    [SerializeField] private Vector3 _rotationAxes = new Vector3(1f, 1f, 1f);

    [Header("Audio (optional)")]
    [Tooltip("Зацикленный аудио-источник для постоянного дрожания. Включается/выключается вместе с continuous-режимом.")]
    [SerializeField] private AudioSource _continuousAudio;

    private Vector3 _baseLocalPosition;
    private Vector3 _baseLocalEulerAngles;
    private Coroutine _impulseRoutine;
    private float _impulseBlend;

    private void Awake()
    {
        CacheBaseTransform();
    }

    private void OnEnable()
    {
        if (_shakeContinuously)
            SetContinuousAudio(true);
    }

    private void OnDisable()
    {
        SetContinuousAudio(false);
        ResetTransform();
    }

    private void LateUpdate()
    {
        if (!enabled)
            return;

        Vector3 posOffset = Vector3.zero;
        Vector3 rotOffset = Vector3.zero;

        if (_shakeContinuously)
        {
            float t = Time.time * _continuousFrequency * FrequencyMultiplier * 0.01f;
            posOffset += SamplePerlin(t, _continuousPositionAmplitude, _positionAxes);
            rotOffset += SamplePerlin(t + 100f, _continuousRotationAmplitude, _rotationAxes);
        }

        if (_impulseBlend > 0f)
        {
            float it = (Time.time + 50f) * _impulseFrequency * FrequencyMultiplier * 0.01f;
            posOffset += SamplePerlin(it, _impulsePositionAmplitude * _impulseBlend, _positionAxes);
            rotOffset += SamplePerlin(it + 200f, _impulseRotationAmplitude * _impulseBlend, _rotationAxes);
        }

        transform.localPosition = _baseLocalPosition + posOffset;
        transform.localEulerAngles = _baseLocalEulerAngles + rotOffset;
    }

    /// <summary>
    /// Включить или выключить постоянное дрожание.
    /// </summary>
    public void SetContinuous(bool enabled)
    {
        _shakeContinuously = enabled;
        SetContinuousAudio(enabled);
    }

    /// <summary>
    /// Запустить импульсную тряску с параметрами по умолчанию из Inspector.
    /// </summary>
    public void Shake()
    {
        Shake(_impulsePositionAmplitude, _impulseRotationAmplitude, _impulseDuration);
    }

    /// <summary>
    /// Запустить импульсную тряску с пользовательскими параметрами.
    /// </summary>
    /// <param name="positionAmplitude">Амплитуда смещения позиции (метры).</param>
    /// <param name="rotationAmplitude">Амплитуда вращения (градусы).</param>
    /// <param name="duration">Длительность тряски (секунды).</param>
    public void Shake(float positionAmplitude, float rotationAmplitude, float duration)
    {
        if (_impulseRoutine != null)
            StopCoroutine(_impulseRoutine);
        _impulseRoutine = StartCoroutine(ImpulseRoutine(positionAmplitude, rotationAmplitude, duration));
    }

    private IEnumerator ImpulseRoutine(float posAmp, float rotAmp, float duration)
    {
        float elapsed = 0f;
        _impulsePositionAmplitude = posAmp;
        _impulseRotationAmplitude = rotAmp;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _impulseBlend = 1f - (elapsed / duration);
            yield return null;
        }

        _impulseBlend = 0f;
        _impulseRoutine = null;
    }

    private void CacheBaseTransform()
    {
        _baseLocalPosition = transform.localPosition;
        _baseLocalEulerAngles = transform.localEulerAngles;
    }

    private void ResetTransform()
    {
        _impulseBlend = 0f;
        transform.localPosition = _baseLocalPosition;
        transform.localEulerAngles = _baseLocalEulerAngles;
    }

    private void SetContinuousAudio(bool play)
    {
        if (_continuousAudio == null)
            return;

        if (play && !_continuousAudio.isPlaying)
            _continuousAudio.Play();
        else if (!play && _continuousAudio.isPlaying)
            _continuousAudio.Stop();
    }

    private static Vector3 SamplePerlin(float t, float amplitude, Vector3 axes)
    {
        if (amplitude <= 0f)
            return Vector3.zero;

        float x = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amplitude * axes.x;
        float y = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amplitude * axes.y;
        float z = (Mathf.PerlinNoise(t, t) - 0.5f) * 2f * amplitude * axes.z;
        return new Vector3(x, y, z);
    }
}
