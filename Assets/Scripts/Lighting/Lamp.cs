using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How this lamp's lights flicker when the lamp is on.</summary>
public enum FlickerMode
{
    /// <summary>No flicker — steady light.</summary>
    None,

    /// <summary>Flickers continuously while on.</summary>
    Constant,

    /// <summary>Stable most of the time, bursts of flicker at random intervals.</summary>
    Occasional,
}

/// <summary>Drives emission on a lamp's meshes in sync with its lights.</summary>
public enum LampEmissionMode
{
    /// <summary>Emission is not controlled by this component — for light-only lamps.</summary>
    None,

    /// <summary>Emission mirrors the lights' on/off state and flicker/fade multiplier.</summary>
    SyncWithLight,
}

/// <summary>
/// Single component that fully describes a lamp for <see cref="LightingSystem"/>:
/// its zone, all controlled lights and (optionally) the emissive glow of its meshes.
///
/// Place on ANY level of the hierarchy:
/// - on a lamp root — auto-finds all Light components in children and renderers on this object;
/// - directly on a Light GameObject with no children — controls just that light;
/// - lights outside the hierarchy can be added via _extraLights.
///
/// Emission is optional (<see cref="_emissionMode"/>): set to None for pure light sources
/// that illuminate room details and have no glow mesh.
///
/// When emission is synced, each renderer gets an instantiated material so lamps
/// sharing a material (e.g. NeonLamp.mat) glow independently. Use _materialIndices
/// to pick which material slot of each renderer is toggled.
/// Registers as an <see cref="ILampZone"/> with <see cref="LightingSystem"/>, so
/// power state, switches, fades and save-load all work without extra wiring.
/// </summary>
public class Lamp : MonoBehaviour, ILampZone
{
    [Tooltip("Zone name this lamp belongs to. E.g. 'Corridor', 'Room1', 'Storage'. " +
             "Case-sensitive. All lamps with the same ZoneId are controlled together.")]
    [SerializeField] private string _zoneId;

    [Tooltip("Additional Light components OUTSIDE this hierarchy that the lamp also controls. " +
             "Child lights are always found automatically — no need to list them here.")]
    [SerializeField] private Light[] _extraLights;

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

    [Header("Emission")]
    [Tooltip("None — materials are not touched (light-only lamps). SyncWithLight — glow follows the lights.")]
    [SerializeField] private LampEmissionMode _emissionMode = LampEmissionMode.None;

    [Tooltip("Renderers whose emission follows the lights. Auto-found on this GameObject if not assigned.")]
    [SerializeField] private Renderer[] _renderers;

    [Tooltip("Index of the material slot on each renderer to control. " +
             "Element i corresponds to _renderers[i]. If omitted or out of range, slot 0 is used.")]
    [SerializeField] private int[] _materialIndices;

    [Tooltip("Emission color multiplied into the emissive map.")]
    [SerializeField] private Color _emissionColor = Color.white;

    [Tooltip("HDR intensity of the glow. Values above 1 bloom when a volume is present.")]
    [SerializeField, Min(0f)] private float _emissionIntensity = 4f;

    [Header("Audio (optional)")]
    [Tooltip("Looping sound (hum/buzz) played while the lamp is on. Leave empty for a silent lamp. " +
             "Plays as a spatial 3D loop through AudioManager (routed to the SFX3D bus, faded with background mutes).")]
    [SerializeField] private AudioClip _audioClip;

    [Tooltip("Volume of the lamp sound loop.")]
    [SerializeField, Range(0f, 1f)] private float _audioVolume = 1f;

    [Tooltip("Distance at which the lamp sound is at full volume.")]
    [SerializeField, Min(0.01f)] private float _audioMinDistance = 1f;

    [Tooltip("Distance at which the lamp sound falls silent (hard linear cutoff).")]
    [SerializeField, Min(0.1f)] private float _audioMaxDistance = 10f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

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

    // ── ILampZone / public ────────────────────────────────────────────────────

    public string ZoneId => _zoneId;

    /// <summary>True when the lamp is logically on (after SetActive(true)).</summary>
    public bool IsOn { get; private set; }

    /// <summary>Main light's intensity as authored in the scene/prefab.</summary>
    public float OriginalIntensity { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private LightEntry[] _entries;
    private Material[] _materials;
    private Coroutine _flickerCoroutine;
    private float _multiplier = 1f;
    private AudioSource _audioSource;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _entries = BuildEntries();
        OriginalIntensity = FindMaxOriginalIntensity();

        if (_emissionMode == LampEmissionMode.SyncWithLight)
            InstantiateMaterials();

        LightingSystem.Instance?.RegisterZone(this);
    }

    private void OnDestroy()
    {
        LightingSystem.Instance?.UnregisterZone(this);
        StopAudio();
    }

    /// <summary>
    /// Collects controlled lights: all Light components on this GameObject and its
    /// children (inactive included), PLUS any extra lights listed in _extraLights
    /// that live outside this hierarchy. _extraLights never disables the auto-search.
    /// </summary>
    private LightEntry[] BuildEntries()
    {
        var lights = new List<Light>();

        foreach (var light in GetComponentsInChildren<Light>(true))
        {
            if (light != null && !lights.Contains(light))
                lights.Add(light);
        }

        if (_extraLights != null)
        {
            foreach (var light in _extraLights)
            {
                if (light != null && !lights.Contains(light))
                    lights.Add(light);
            }
        }

        var entries = new LightEntry[lights.Count];
        for (int i = 0; i < lights.Count; i++)
            entries[i] = new LightEntry(lights[i]);
        return entries;
    }

    private float FindMaxOriginalIntensity()
    {
        float max = 0f;
        foreach (var entry in _entries)
            max = Mathf.Max(max, entry.OriginalIntensity);
        return max;
    }

    // ── ILampZone API (called by LightingSystem) ──────────────────────────────

    /// <summary>
    /// Turns this lamp's lights on or off. Starts or stops flicker accordingly.
    /// Call this instead of setting Light.enabled directly.
    /// </summary>
    public void SetActive(bool on)
    {
        IsOn = on;

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
        ApplyEmission(on ? 1f : 0f);
        ApplyAudio(on);
    }

    /// <summary>
    /// Sets intensity of the controlled lights as a multiplier of their original
    /// intensities (used during fade transitions).
    /// Does NOT start/stop flicker — call SetActive() for that.
    /// </summary>
    public void SetIntensityMultiplier(float multiplier)
    {
        float clamped = Mathf.Clamp01(multiplier);
        foreach (var entry in _entries)
        {
            if (entry.Light != null)
            {
                // Enable during fade-in so the intensity lerp is visible;
                // Light.enabled is still authoritative via SetActive afterwards.
                if (!IsOn && clamped > 0f)
                    entry.Light.enabled = true;
                entry.Light.intensity = entry.OriginalIntensity * clamped;
            }
        }
        ApplyEmission(clamped);
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

    // ── Audio ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts or stops the optional lamp sound loop to match the given on state.
    /// The loop is created as a child of this lamp through AudioManager's
    /// Play3DLoop, so it is spatial, routed to the SFX3D bus and faded by the
    /// background tracking system (MuteBackground/UnmuteBackground).
    /// </summary>
    private void ApplyAudio(bool on)
    {
        if (_audioClip == null) return;

        if (on)
        {
            if (_audioSource == null)
            {
                _audioSource = AudioManager.Instance != null
                    ? AudioManager.Instance.Play3DLoop(_audioClip, transform, _audioVolume, _audioMinDistance, _audioMaxDistance)
                    : CreateFallbackAudioSource();
            }
            else if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
        else
        {
            StopAudio();
        }
    }

    /// <summary>Fallback when no AudioManager exists in the scene: a plain local looping source.</summary>
    private AudioSource CreateFallbackAudioSource()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = _audioClip;
        source.loop = true;
        source.spatialBlend = 1f;
        // Linear rolloff so maxDistance is a hard cutoff, matching Play3DLoop.
        source.rolloffMode = AudioRolloffMode.Linear;
        source.volume = _audioVolume;
        source.minDistance = _audioMinDistance;
        source.maxDistance = _audioMaxDistance;
        source.playOnAwake = false;
        source.Play();
        return source;
    }

    /// <summary>Stops and releases the lamp sound loop. Safe to call when no sound was started.</summary>
    private void StopAudio()
    {
        if (_audioSource == null) return;
        if (AudioManager.Instance != null)
        {
            // Handles both Play3DLoop-created sources and pre-configured ones.
            AudioManager.Instance.UnregisterLoopSource(_audioSource);
            if (_audioSource != null) Destroy(_audioSource.gameObject);
        }
        else
        {
            _audioSource.Stop();
            Destroy(_audioSource);
        }
        _audioSource = null;
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

    private IEnumerator ConstantFlickerRoutine()
    {
        float stepDelay = 1f / Mathf.Max(_flickerFrequency, 1f);
        var wait = new WaitForSeconds(stepDelay);

        while (IsOn)
        {
            float dip = Random.Range(_flickerMinMultiplier, 1f);
            foreach (var entry in _entries)
            {
                if (entry.Light != null)
                    entry.Light.intensity = entry.OriginalIntensity * dip;
            }
            ApplyEmission(dip);
            yield return wait;
        }

        SetAllToOriginalIntensity();
    }

    private IEnumerator OccasionalFlickerRoutine()
    {
        float burstStepDelay = 1f / Mathf.Max(_flickerFrequency, 1f);

        while (IsOn)
        {
            // Stable phase — wait a random interval before next burst.
            float waitTime = Random.Range(_occasionalMinInterval, _occasionalMaxInterval);
            yield return new WaitForSeconds(waitTime);

            if (!IsOn) break;

            // Burst phase — rapid intensity randomisation for a short duration.
            float burstEnd = Time.time + Random.Range(_occasionalMinDuration, _occasionalMaxDuration);
            while (Time.time < burstEnd && IsOn)
            {
                float dip = Random.Range(_flickerMinMultiplier, 1f);
                foreach (var entry in _entries)
                {
                    if (entry.Light != null)
                        entry.Light.intensity = entry.OriginalIntensity * dip;
                }
                ApplyEmission(dip);
                yield return new WaitForSeconds(burstStepDelay);
            }

            // Return to full brightness after the burst.
            if (IsOn)
                SetAllToOriginalIntensity();
        }
    }

    /// <summary>Restores every controlled light to its original intensity.</summary>
    private void SetAllToOriginalIntensity()
    {
        foreach (var entry in _entries)
        {
            if (entry.Light != null)
                entry.Light.intensity = entry.OriginalIntensity;
        }
        ApplyEmission(1f);
    }

    // ── Emission ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Instances the target materials once so lamps sharing a material glow independently.
    /// </summary>
    private void InstantiateMaterials()
    {
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponents<Renderer>();

        _materials = new Material[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            int slotIndex = GetMaterialIndex(i, renderer);
            var sourceMaterial = renderer.sharedMaterials[slotIndex];
            if (sourceMaterial == null) continue;

            var instance = new Material(sourceMaterial);
            var materials = renderer.sharedMaterials;
            materials[slotIndex] = instance;
            renderer.sharedMaterials = materials;
            _materials[i] = instance;
        }
    }

    /// <summary>Resolved material slot for renderer i (clamped to the renderer's valid range).</summary>
    private int GetMaterialIndex(int rendererIndex, Renderer renderer)
    {
        if (_materialIndices != null &&
            rendererIndex < _materialIndices.Length &&
            _materialIndices[rendererIndex] >= 0)
        {
            return Mathf.Min(_materialIndices[rendererIndex], renderer.sharedMaterials.Length - 1);
        }
        return 0;
    }

    /// <summary>
    /// Applies the emission state for the given brightness multiplier (0..1).
    /// With _emissionMode None this does nothing.
    /// </summary>
    private void ApplyEmission(float multiplier)
    {
        if (_emissionMode != LampEmissionMode.SyncWithLight) return;
        if (_materials == null) return;

        bool lit = multiplier > 0.001f;
        foreach (var material in _materials)
        {
            if (material == null || !material.HasProperty(EmissionColorId)) continue;

            if (lit)
            {
                material.EnableKeyword(EmissionKeyword);
                material.SetColor(EmissionColorId, _emissionColor * (_emissionIntensity * multiplier));
            }
            else
            {
                material.DisableKeyword(EmissionKeyword);
                material.SetColor(EmissionColorId, Color.black);
            }
        }
    }
}
