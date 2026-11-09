using System.Collections;
using System.Collections.Generic;
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
/// Extra Light components can be linked via Linked Lights to mirror this lamp's
/// state, fade multiplier and flicker in tandem (they must NOT have a LightZone).
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

    [Header("Linked Lights")]
    [Tooltip("Additional Light components (on child or nearby GameObjects, WITHOUT a LightZone component) " +
             "that mirror this lamp exactly: same on/off state, fade multiplier and flicker pattern. " +
             "Use to make several bulbs flicker in tandem from this one script.")]
    [SerializeField] private Light[] _linkedLights;

    /// <summary>
    /// Raised whenever the lamp's brightness multiplier changes (flicker step or fade).
    /// Passes the multiplier in 0..1 relative to full brightness. ZoneEmission subscribes
    /// to this so the lamp mesh's glow flickers in tandem with the light.
    /// </summary>
    public event System.Action<float> IntensityMultiplierChanged;

    /// <summary>A Light component together with its intensity as authored in the scene/prefab.</summary>
    private struct LightEntry
    {
        public Light Light;
        public float OriginalIntensity;

        public LightEntry(Light light)
        {
            Light = light;
            OriginalIntensity = light != null ? light.intensity : 0f;
        }
    }

    // ── Public ────────────────────────────────────────────────────────────────

    public string ZoneId => _zoneId;

    /// <summary>The Light component on this GameObject.</summary>
    public Light Light { get; private set; }

    /// <summary>The main light's intensity as authored in the scene/prefab.</summary>
    public float OriginalIntensity { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private LightEntry[] _entries;
    private Coroutine _flickerCoroutine;
    private bool _isLogicallyOn;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Light = GetComponent<Light>();
        OriginalIntensity = Light.intensity;

        _entries = BuildEntries();
        LightingSystem.Instance?.RegisterZone(this);
    }

    /// <summary>Collects the main light plus non-null, non-duplicate linked lights.</summary>
    private LightEntry[] BuildEntries()
    {
        var entries = new List<LightEntry> { new LightEntry(Light) };
        if (_linkedLights != null)
        {
            foreach (var linked in _linkedLights)
            {
                if (linked == null || linked == Light) continue;
                entries.Add(new LightEntry(linked));
            }
        }
        return entries.ToArray();
    }

    private void OnDestroy()
    {
        LightingSystem.Instance?.UnregisterZone(this);
    }

    // ── API (called by LightingSystem) ────────────────────────────────────────

    /// <summary>
    /// Turns this lamp (and its linked lights) on or off. Starts or stops flicker accordingly.
    /// Call this instead of setting Light.enabled directly.
    /// </summary>
    public void SetActive(bool on)
    {
        _isLogicallyOn = on;

        foreach (var entry in _entries)
        {
            if (entry.Light == null) continue;

            if (!on)
            {
                entry.Light.enabled = false;
                entry.Light.intensity = entry.OriginalIntensity; // reset for next fade-in
            }
            else
            {
                entry.Light.enabled = true;
            }
        }

        if (on) StartFlicker(); else StopFlicker();
        NotifyMultiplier(on ? 1f : 0f);
    }

    /// <summary>
    /// Sets intensity of this lamp and its linked lights as a multiplier of their
    /// original intensities (used during fade transitions).
    /// Does NOT start/stop flicker — call SetActive() for that.
    /// </summary>
    public void SetIntensityMultiplier(float multiplier)
    {
        float clamped = Mathf.Clamp01(multiplier);
        foreach (var entry in _entries)
        {
            if (entry.Light != null)
                entry.Light.intensity = entry.OriginalIntensity * clamped;
        }
        NotifyMultiplier(clamped);
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

    /// <summary>Stops any active flicker coroutine and restores intensities to their originals.</summary>
    public void StopFlicker()
    {
        if (_flickerCoroutine == null) return;
        StopCoroutine(_flickerCoroutine);
        _flickerCoroutine = null;
        // Restore to full intensity so the fade system has a clean baseline.
        foreach (var entry in _entries)
        {
            if (entry.Light != null)
                entry.Light.intensity = entry.OriginalIntensity;
        }
    }

    private IEnumerator ConstantFlickerRoutine()
    {
        float stepDelay = 1f / Mathf.Max(_flickerFrequency, 1f);
        var wait = new WaitForSeconds(stepDelay);

        while (_isLogicallyOn)
        {
            float dip = Random.Range(_flickerMinMultiplier, 1f);
            foreach (var entry in _entries)
            {
                if (entry.Light != null)
                    entry.Light.intensity = entry.OriginalIntensity * dip;
            }
            NotifyMultiplier(dip);
            yield return wait;
        }

        SetAllToOriginalIntensity();
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
                float dip = Random.Range(_flickerMinMultiplier, 1f);
                foreach (var entry in _entries)
                {
                    if (entry.Light != null)
                        entry.Light.intensity = entry.OriginalIntensity * dip;
                }
                NotifyMultiplier(dip);
                yield return new WaitForSeconds(burstStepDelay);
            }

            // Return to full brightness after the burst.
            if (_isLogicallyOn)
                SetAllToOriginalIntensity();
        }
    }

    /// <summary>Restores every light (main + linked) to its original intensity.</summary>
    private void SetAllToOriginalIntensity()
    {
        foreach (var entry in _entries)
        {
            if (entry.Light != null)
                entry.Light.intensity = entry.OriginalIntensity;
        }
        NotifyMultiplier(1f);
    }

    /// <summary>Raises <see cref="IntensityMultiplierChanged"/> if there are subscribers.</summary>
    private void NotifyMultiplier(float multiplier)
    {
        IntensityMultiplierChanged?.Invoke(multiplier);
    }
}
