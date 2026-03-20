using UnityEngine;

/// <summary>
/// Simulates organic fire flickering using Perlin noise on a Point Light.
/// Independently modulates intensity and range for a natural fire appearance.
/// </summary>
[RequireComponent(typeof(Light))]
public class FireLightFlicker : MonoBehaviour
{
    [Header("Intensity")]
    [Tooltip("Base light intensity around which flickering occurs.")]
    [SerializeField] private float _baseIntensity = 1f;
    [Tooltip("Maximum intensity deviation from the base value.")]
    [SerializeField] private float _intensityVariance = 0.4f;

    [Header("Range")]
    [Tooltip("Base light range around which flickering occurs.")]
    [SerializeField] private float _baseRange = 10f;
    [Tooltip("Maximum range deviation from the base value.")]
    [SerializeField] private float _rangeVariance = 1.5f;

    [Header("Speed")]
    [Tooltip("How fast the intensity noise scrolls. Higher = more chaotic.")]
    [SerializeField] private float _intensitySpeed = 1.2f;
    [Tooltip("How fast the range noise scrolls. Slightly different from intensity for organic feel.")]
    [SerializeField] private float _rangeSpeed = 0.9f;

    [Header("Smoothing")]
    [Tooltip("Lerp speed toward the noise target. Lower = lazier, higher = snappier.")]
    [SerializeField] [Range(1f, 30f)] private float _smoothing = 12f;

    private Light _light;
    private float _currentIntensity;
    private float _currentRange;

    // Offset so two lamps in scene don't flicker in sync
    private float _noiseOffset;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _noiseOffset = Random.Range(0f, 100f);
    }

    private void Start()
    {
        _currentIntensity = _baseIntensity;
        _currentRange = _baseRange;

        _light.intensity = _baseIntensity;
        _light.range = _baseRange;
    }

    private void Update()
    {
        float time = Time.time + _noiseOffset;

        // Perlin noise returns [0..1], remap to [-1..1] for symmetric variance
        float intensityNoise = Mathf.PerlinNoise(time * _intensitySpeed, 0f) * 2f - 1f;
        float rangeNoise = Mathf.PerlinNoise(0f, time * _rangeSpeed) * 2f - 1f;

        float targetIntensity = _baseIntensity + intensityNoise * _intensityVariance;
        float targetRange = _baseRange + rangeNoise * _rangeVariance;

        float lerpFactor = _smoothing * Time.deltaTime;
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, lerpFactor);
        _currentRange = Mathf.Lerp(_currentRange, targetRange, lerpFactor);

        _light.intensity = Mathf.Max(0f, _currentIntensity);
        _light.range = Mathf.Max(0f, _currentRange);
    }
}
