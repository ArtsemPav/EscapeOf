using System.Collections;
using UnityEngine;

/// <summary>How this lamp flickers when it is on.</summary>
public enum FlickerMode
{
    /// <summary>No flicker — steady light.</summary>
    None,

    /// <summary>Flickers continuously while on.</summary>
    Constant,

    /// <summary>Stable most of the time, bursts of flicker at random intervals.</summary>
    Occasional,
}

/// <summary>
/// Place this component directly on a Light GameObject (including inside prefabs).
/// Assigns the light to a named zone managed by LightingSystem.
/// Multiple lights across different prefabs can share the same ZoneId.
/// Each lamp has its own flicker settings, so some lamps in a room can flicker
/// while others stay steady.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightZone : MonoBehaviour
{
    [Tooltip("Zone name this light belongs to. E.g. 'Corridor', 'Room1', 'Storage'. " +
             "Case-sensitive. All lights with the same ZoneId are controlled together.")]
    [SerializeField] private string _zoneId;

    [Header("Flicker")]
    [SerializeField] private FlickerMode _flickerMode = FlickerMode.None;

    [Tooltip("Minimum intensity multiplier during a flicker dip (0 = total blackout, 0.5 = half brightness).")]
    [SerializeField] [Range(0f, 1f)] private float _flickerMinMultiplier = 0.1f;

    [Tooltip("How many times per second the intensity is randomised during constant flicker.")]
    [SerializeField] [Range(2f, 50f)] private float _flickerFrequency = 20f;

    [Header("Occasional Flicker")]
    [Tooltip("Min seconds between occasional flicker bursts.")]
    [SerializeField] private float _occasionalMinInterval = 3f;

    [Tooltip("Max seconds between occasional flicker bursts.")]
    [SerializeField] private float _occasionalMaxInterval = 12f;

    [Tooltip("Min duration of one flicker burst in seconds.")]
    [SerializeField] private float _occasionalMinDuration = 0.2f;

    [Tooltip("Max duration of one flicker burst in seconds.")]
    [SerializeField] private float _occasionalMaxDuration = 1.2f;

    // ── Public ────────────────────────────────────────────────────────────────

    public string ZoneId => _zoneId;

    /// <summary>The Light component on this GameObject.</summary>
    public Light Light { get; private set; }

    /// <summary>The light's intensity as authored in the scene/prefab.</summary>
    public float OriginalIntensity { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Coroutine _flickerCoroutine;
    private bool _isLogicallyOn;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Light = GetComponent<Light>();
        OriginalIntensity = Light.intensity;
        LightingSystem.Instance?.RegisterZone(this);
    }

    private void OnDestroy()
    {
        LightingSystem.Instance?.UnregisterZone(this);
    }

    // ── API (called by LightingSystem) ────────────────────────────────────────

    /// <summary>
    /// Turns this lamp on or off. Starts or stops flicker accordingly.
    /// Call this instead of setting Light.enabled directly.
    /// </summary>
    public void SetActive(bool on)
    {
        _isLogicallyOn = on;

        if (!on)
        {
            StopFlicker();
            Light.enabled = false;
            Light.intensity = OriginalIntensity; // reset for next fade-in
        }
        else
        {
            Light.enabled = true;
            StartFlicker();
        }
    }

    /// <summary>
    /// Sets intensity as a multiplier of OriginalIntensity (used during fade transitions).
    /// Does NOT start/stop flicker — call SetActive() for that.
    /// </summary>
    public void SetIntensityMultiplier(float multiplier)
    {
        if (Light != null)
            Light.intensity = OriginalIntensity * Mathf.Clamp01(multiplier);
    }

    // ── Flicker ───────────────────────────────────────────────────────────────

    private void StartFlicker()
    {
        if (_flickerMode == FlickerMode.None) return;
        StopFlicker();
        _flickerCoroutine = _flickerMode == FlickerMode.Constant
            ? StartCoroutine(ConstantFlickerRoutine())
            : StartCoroutine(OccasionalFlickerRoutine());
    }

    /// <summary>Stops any active flicker coroutine and restores intensity to OriginalIntensity.</summary>
    public void StopFlicker()
    {
        if (_flickerCoroutine == null) return;
        StopCoroutine(_flickerCoroutine);
        _flickerCoroutine = null;
        // Restore to full intensity so the fade system has a clean baseline.
        if (Light != null)
            Light.intensity = OriginalIntensity;
    }

    private IEnumerator ConstantFlickerRoutine()
    {
        float stepDelay = 1f / Mathf.Max(_flickerFrequency, 1f);
        var wait = new WaitForSeconds(stepDelay);

        while (_isLogicallyOn)
        {
            Light.intensity = OriginalIntensity * Random.Range(_flickerMinMultiplier, 1f);
            yield return wait;
        }

        Light.intensity = OriginalIntensity;
    }

    private IEnumerator OccasionalFlickerRoutine()
    {
        float burstStepDelay = 1f / Mathf.Max(_flickerFrequency, 1f);

        while (_isLogicallyOn)
        {
            // Stable phase — wait a random interval before next burst.
            float waitTime = Random.Range(_occasionalMinInterval, _occasionalMaxInterval);
            yield return new WaitForSeconds(waitTime);

            if (!_isLogicallyOn) break;

            // Burst phase — rapid intensity randomisation for a short duration.
            float burstEnd = Time.time + Random.Range(_occasionalMinDuration, _occasionalMaxDuration);
            while (Time.time < burstEnd && _isLogicallyOn)
            {
                Light.intensity = OriginalIntensity * Random.Range(_flickerMinMultiplier, 1f);
                yield return new WaitForSeconds(burstStepDelay);
            }

            // Return to full brightness after the burst.
            if (_isLogicallyOn)
                Light.intensity = OriginalIntensity;
        }
    }
}
