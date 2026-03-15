using System.Collections;
using UnityEngine;

/// <summary>
/// Slow horror-style neon light flicker.
/// Animates both the Light intensity and the MeshRenderer emission in sync.
/// Attach to the lamp mesh GameObject; assign the child Light in the Inspector.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class NeonLightFlicker : MonoBehaviour
{
    [SerializeField] private Light _flickerLight;

    [Header("Intensity")]
    [Tooltip("Minimum intensity as a fraction of the original (0 = fully off).")]
    [SerializeField] [Range(0f, 1f)] private float _minIntensity = 0.3f;
    [Tooltip("Maximum intensity as a fraction of the original (1 = fully on).")]
    [SerializeField] [Range(0.5f, 1.5f)] private float _maxIntensity = 1.0f;

    [Header("Slow Flicker")]
    [SerializeField] private float _slowFlickerMinDuration = 0.4f;
    [SerializeField] private float _slowFlickerMaxDuration = 1.4f;

    [Header("Rapid Flicker")]
    [Tooltip("Chance per slow cycle that a rapid burst will occur.")]
    [SerializeField] [Range(0f, 0.5f)] private float _rapidFlickerChance = 0.08f;

    [Header("Dark Pause")]
    [Tooltip("Chance per slow cycle that the lamp will go completely dark.")]
    [SerializeField] [Range(0f, 0.3f)] private float _darkPauseChance = 0.04f;
    [SerializeField] private float _darkPauseMinDuration = 0.3f;
    [SerializeField] private float _darkPauseMaxDuration = 1.8f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MeshRenderer _meshRenderer;
    private Material _instanceMaterial;
    private Color _baseEmissionColor;
    private float _baseIntensity;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        // Create a material instance so we don't modify the shared asset
        _instanceMaterial = _meshRenderer.material;
    }

    private void Start()
    {
        if (_flickerLight == null)
        {
            Debug.LogWarning($"[NeonLightFlicker] No Light assigned on '{name}'. Flickering disabled.", this);
            return;
        }

        _baseIntensity = _flickerLight.intensity;
        _baseEmissionColor = _instanceMaterial.GetColor(EmissionColorId);

        StartCoroutine(FlickerLoop());
    }

    private void OnDestroy()
    {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);
    }

    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            float roll = Random.value;

            if (roll < _darkPauseChance)
            {
                yield return StartCoroutine(DarkPause(
                    Random.Range(_darkPauseMinDuration, _darkPauseMaxDuration)));
            }
            else if (roll < _darkPauseChance + _rapidFlickerChance)
            {
                yield return StartCoroutine(RapidFlicker(Random.Range(2, 6)));
            }
            else
            {
                float targetNormalized = Random.Range(_minIntensity, _maxIntensity);
                float duration = Random.Range(_slowFlickerMinDuration, _slowFlickerMaxDuration);
                yield return StartCoroutine(SmoothStep(targetNormalized, duration));
            }
        }
    }

    // Eased transition to a target intensity fraction
    private IEnumerator SmoothStep(float targetNormalized, float duration)
    {
        float startNormalized = _baseIntensity > 0f
            ? _flickerLight.intensity / _baseIntensity
            : 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            ApplyNormalized(Mathf.Lerp(startNormalized, targetNormalized, t));
            yield return null;
        }

        ApplyNormalized(targetNormalized);
    }

    // Lamp goes completely dark for a moment, then snaps back on
    private IEnumerator DarkPause(float duration)
    {
        ApplyNormalized(0f);
        yield return new WaitForSeconds(duration);
        ApplyNormalized(1f);
    }

    // Quick on/off bursts — rapid horror flicker
    private IEnumerator RapidFlicker(int count)
    {
        for (int i = 0; i < count; i++)
        {
            ApplyNormalized(0f);
            yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
            ApplyNormalized(Random.Range(0.7f, 1.1f));
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
        }
    }

    /// <summary>
    /// Applies a normalized [0..1] multiplier to both Light intensity and material emission.
    /// </summary>
    private void ApplyNormalized(float normalized)
    {
        _flickerLight.intensity = _baseIntensity * normalized;
        _instanceMaterial.SetColor(EmissionColorId, _baseEmissionColor * normalized);
    }
}
