using UnityEngine;

/// <summary>
/// Simulates film projector flicker on a light-ray mesh by modulating
/// the material's _Color (brightness) and _fade (beam length/opacity).
/// Combines a periodic shutter pulse, Perlin noise for organic variation,
/// and random brief dropouts.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class ProjectorLightFlicker : MonoBehaviour
{
    private const string ColorProperty = "_Color";
    private const string FadeProperty = "_fade";

    [Header("Color Flicker")]
    [Tooltip("How strongly the shutter modulates color brightness (0 = none, 1 = full on/off).")]
    [SerializeField] [Range(0f, 1f)] private float _colorShutterDepth = 0.3f;

    [Tooltip("Amplitude of organic noise on color brightness.")]
    [SerializeField] [Range(0f, 1f)] private float _colorNoiseAmplitude = 0.15f;

    [Header("Fade Flicker")]
    [Tooltip("Base _fade value around which flickering occurs. Set to 0 to use the material's current value.")]
    [SerializeField] private float _baseFade = 0f;

    [Tooltip("Maximum deviation of _fade from the base value.")]
    [SerializeField] private float _fadeVariance = 0.3f;

    [Header("Shutter")]
    [Tooltip("Frequency of the shutter pulse in Hz. Lower values make flicker more visible.")]
    [SerializeField] private float _shutterFrequency = 10f;

    [Header("Organic Noise")]
    [Tooltip("How fast the Perlin noise scrolls. Higher = more chaotic.")]
    [SerializeField] private float _noiseSpeed = 3f;

    [Header("Dropouts")]
    [Tooltip("Probability per second of a brief dropout (film stutter).")]
    [SerializeField] private float _dropoutChancePerSecond = 0.2f;

    [Tooltip("Minimum duration of a dropout in seconds.")]
    [SerializeField] private float _dropoutMinDuration = 0.05f;

    [Tooltip("Maximum duration of a dropout in seconds.")]
    [SerializeField] private float _dropoutMaxDuration = 0.25f;

    [Tooltip("Brightness multiplier during a dropout (0 = black, 1 = no effect).")]
    [SerializeField] [Range(0f, 1f)] private float _dropoutBrightness = 0.1f;

    [Header("Smoothing")]
    [Tooltip("Lerp speed toward the target value. Higher = snappier.")]
    [SerializeField] [Range(1f, 50f)] private float _smoothing = 15f;

    private MeshRenderer _meshRenderer;
    private Material _instanceMaterial;
    private Color _baseColor;
    private float _baseFadeValue;
    private float _currentColorFactor;
    private float _currentFade;
    private float _noiseOffset;
    private float _dropoutTimer;
    private float _dropoutDuration;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _instanceMaterial = _meshRenderer.material;

        _baseColor = _instanceMaterial.GetColor(ColorProperty);

        if (_baseFade <= 0f)
            _baseFadeValue = _instanceMaterial.GetFloat(FadeProperty);
        else
            _baseFadeValue = _baseFade;

        _noiseOffset = Random.Range(0f, 100f);
        _currentColorFactor = 1f;
        _currentFade = _baseFadeValue;
    }

    private void OnDestroy()
    {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        float time = Time.time + _noiseOffset;

        // Periodic shutter pulse (cosine-based, 0..1 range)
        float shutter = Mathf.Cos(time * _shutterFrequency * Mathf.PI * 2f);
        shutter = 1f - _colorShutterDepth * (1f - Mathf.Max(0f, shutter));

        // Organic Perlin noise overlay
        float noise = (Mathf.PerlinNoise(time * _noiseSpeed, 0f) * 2f - 1f) * _colorNoiseAmplitude;

        // Dropout logic — random brief dimming simulating film stutter
        if (_dropoutTimer > 0f)
        {
            _dropoutTimer -= Time.deltaTime;
            if (_dropoutTimer <= 0f)
                _dropoutDuration = 0f;
        }
        else if (Random.value < _dropoutChancePerSecond * Time.deltaTime)
        {
            _dropoutDuration = Random.Range(_dropoutMinDuration, _dropoutMaxDuration);
            _dropoutTimer = _dropoutDuration;
        }

        float dropoutFactor = _dropoutDuration > 0f ? _dropoutBrightness : 1f;

        // Color brightness target
        float targetColorFactor = Mathf.Clamp01((shutter + noise) * dropoutFactor);
        _currentColorFactor = Mathf.Lerp(_currentColorFactor, targetColorFactor, _smoothing * Time.deltaTime);

        // Apply color modulation
        Color flickerColor = _baseColor * _currentColorFactor;
        flickerColor.a = _baseColor.a;
        _instanceMaterial.SetColor(ColorProperty, flickerColor);

        // Fade target — modulate beam length/opacity
        float fadeNoise = (Mathf.PerlinNoise(0f, time * _noiseSpeed * 0.7f) * 2f - 1f) * _fadeVariance;
        float targetFade = Mathf.Max(0f, _baseFadeValue + fadeNoise * _currentColorFactor);
        _currentFade = Mathf.Lerp(_currentFade, targetFade, _smoothing * Time.deltaTime);
        _instanceMaterial.SetFloat(FadeProperty, _currentFade);
    }
}
