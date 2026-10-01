using System.Collections;
using UnityEngine;

/// <summary>
/// Плавно наращивает эмиссию рендерера при активации объекта (OnEnable),
/// после разгорания добавляет шумовое мерцание на Mathf.PerlinNoise.
/// Вешается на объект flames факела, Renderer указывает на меш факела.
/// Использует MaterialPropertyBlock — общий материал не изменяется.
/// </summary>
public class EmissionRampOnEnable : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Tooltip("Рендерер, эмиссия которого будет нарастать. Если пуст — берётся с этого объекта.")]
    [SerializeField] private Renderer _targetRenderer;

    [Header("Ignition")]
    [Tooltip("Цвет свечения головки факела.")]
    [SerializeField] private Color _emissionColor = new Color(1f, 0.55f, 0.15f);

    [Tooltip("Целевая интенсивность свечения (HDR).")]
    [SerializeField] private float _targetIntensity = 3f;

    [Tooltip("Время разгорания, в секундах.")]
    [SerializeField] private float _rampDuration = 0.5f;

    [Header("Flicker")]
    [Tooltip("Сила мерцания: 0 = ровный свет, 0.3 = заметное дрожание пламени.")]
    [SerializeField] private float _flickerStrength = 0.3f;

    [Tooltip("Скорость мерцания.")]
    [SerializeField] private float _flickerSpeed = 3f;

    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _igniteRoutine;

    /// <summary>Случайное зерно шума, чтобы факелы не мерцали синхронно.</summary>
    private float _noiseSeed;

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        _noiseSeed = Random.Range(0f, 100f);
        SetEmission(0f);
    }

    private void OnEnable()
    {
        if (_igniteRoutine != null) StopCoroutine(_igniteRoutine);
        _igniteRoutine = StartCoroutine(IgniteRoutine());
    }

    private void OnDisable()
    {
        if (_igniteRoutine != null) StopCoroutine(_igniteRoutine);
        SetEmission(0f);
    }

    private IEnumerator IgniteRoutine()
    {
        // Фаза разгорания: эмиссия плавно нарастает от 0 до целевой.
        float elapsed = 0f;
        while (elapsed < _rampDuration)
        {
            elapsed += Time.deltaTime;
            SetEmission(Mathf.Clamp01(elapsed / _rampDuration));
            yield return null;
        }

        // Фаза горения: модуляция шума Перлина вокруг целевой интенсивности.
        float time = 0f;
        while (true)
        {
            time += Time.deltaTime * _flickerSpeed;
            float noise = Mathf.PerlinNoise(time, _noiseSeed);
            float flicker = 1f + (noise - 0.5f) * 2f * _flickerStrength;
            SetEmission(Mathf.Max(0f, flicker));
            yield return null;
        }
    }

    /// <summary>Устанавливает эмиссию рендерера с заданной долей целевой интенсивности.</summary>
    private void SetEmission(float fraction)
    {
        if (_targetRenderer == null) return;

        _targetRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColorId, _emissionColor * (_targetIntensity * fraction));
        _targetRenderer.SetPropertyBlock(_propertyBlock);
    }
}
