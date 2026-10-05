using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the animated light pillars (Custom/LightBeamAnimated shader):
/// — pulses the material's _MusicIntensity from the track's loudness
///   (the louder / more dynamic the music, the harder the beams pulse);
/// — keeps each pillar's material _Color in sync with the main event light
///   (MainLight / MusicColorLight), so beams and room light change color together;
/// — stays idle until UVPentagramSequence.MusicPlaying is true (pillars appear
///   together with the particles and disappear when the music ends).
///
/// Attach to the parent of the pillar objects (e.g. LightPilars).
/// Each child needs a MeshRenderer with the Custom/LightBeamAnimated material;
/// the existing ProjectorLightFlicker on the meshes keeps working (it modulates
/// _Color/_fade, this component only feeds _MusicIntensity and _Color).
/// </summary>
public class LightPilarMusicDriver : MonoBehaviour
{
    [Tooltip("Reads loudness from this track. Auto-found from UVPentagramSequence if left empty.")]
    [SerializeField] private AudioSource _musicSource;

    [Tooltip("Main event light whose color the pillars follow. Auto-found if left empty.")]
    [SerializeField] private Light _mainLight;

    [Header("Loudness Reaction")]
    [Tooltip("Base _MusicIntensity when the music is quiet (0 = calm shimmer).")]
    [SerializeField, Range(0f, 2f)] private float _baseIntensity = 0.25f;

    [Tooltip("Extra _MusicIntensity at full loudness (1 = hard pumping pulses).")]
    [SerializeField, Range(0f, 2f)] private float _maxIntensityBoost = 1.2f;

    [Tooltip("How fast intensity follows loudness (higher = snappier).")]
    [SerializeField, Min(0.1f)] private float _loudnessLerpSpeed = 8f;

    [Header("Color Sync")]
    [Tooltip("Brighten the beam color with the loudness on top of the main light color.")]
    [SerializeField] private bool _boostColorWithLoudness = true;

    [Header("Rotation (chaos)")]
    [Tooltip("Seconds between rotation snaps. Short interval = strobe-like swinging.")]
    [SerializeField, Range(0.05f, 2f)] private float _rotationInterval = 0.35f;

    [Tooltip("Degrees per snap.")]
    [SerializeField, Range(5f, 180f)] private float _rotationStepDegrees = 60f;

    [Tooltip("Randomize the step size (50–150% of the base step) each snap.")]
    [SerializeField] private bool _randomStepSize = true;

    [Tooltip("Random direction each snap (otherwise constant direction).")]
    [SerializeField] private bool _randomStepSign = true;

    [Tooltip("Orbit pillars around the parent's pivot (LightPilars origin) on each snap.")]
    [SerializeField] private bool _orbitAroundParent = true;

    [Header("Tilt variation")]
    [Tooltip("Vary each pillar's tilt angle on every snap (within ±35% of its base tilt by default).")]
    [SerializeField] private bool _varyTilt = true;

    [Tooltip("How much the tilt may deviate from the base tilt, 0.35 = ±35%.")]
    [SerializeField, Range(0f, 1f)] private float _tiltVariancePercent = 0.35f;

    private float _rotationTimer;
    private readonly List<Transform> _pillars = new();
    private readonly List<float> _baseTilts = new();

    private Renderer[] _pillarRenderers;
    private Material[] _pillarMaterials;
    private float _currentIntensity;
    private float[] _spectrum;
    private Color _lastMainColor;
    private bool _idle = true;

    private void Awake()
    {
        _pillarRenderers = GetComponentsInChildren<Renderer>(true);
        _pillarMaterials = new Material[_pillarRenderers.Length];
        for (int i = 0; i < _pillarRenderers.Length; i++)
            _pillarMaterials[i] = _pillarRenderers[i].material; // instance materials

        _spectrum = new float[64];
        _lastMainColor = Color.clear;
        SetIntensity(0f);

        // Remember each pillar's base tilt around its local Z axis
        _pillars.Clear();
        _baseTilts.Clear();
        foreach (Transform child in transform)
        {
            _pillars.Add(child);
            _baseTilts.Add(child.localEulerAngles.z);
        }
    }

    private void OnDestroy()
    {
        // Cleanup instanced materials
        if (_pillarMaterials == null) return;
        foreach (Material mat in _pillarMaterials)
            if (mat != null) Destroy(mat);
    }

    private void Update()
    {
        if (!UVPentagramSequence.MusicPlaying)
        {
            if (!_idle)
            {
                SetIntensity(0f);
                _idle = true;
            }
            return;
        }

        _idle = false;

        if (_musicSource == null)
            _musicSource = UVPentagramSequence.InstanceMusicSource;
        if (_mainLight == null)
        {
            // Auto-find the event's MainLight by name
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
                if (light.name == "MainLight") { _mainLight = light; break; }
        }

        float loudness = ReadLoudness();
        float targetIntensity = Mathf.Clamp01(_baseIntensity + _maxIntensityBoost * loudness);
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, _loudnessLerpSpeed * Time.deltaTime);
        SetIntensity(_currentIntensity);

        // Follow the main light color (music-driven spectrum cycle)
        if (_mainLight != null && _mainLight.color != _lastMainColor)
        {
            _lastMainColor = _mainLight.color;
            Color boosted = _boostColorWithLoudness
                ? _lastMainColor * (1f + _currentIntensity * 0.5f)
                : _lastMainColor;
            SetColor(boosted);
        }

        SnapRotate();
    }

    // ── Rotation chaos — short-interval snapping swings ───────────────────────

    /// <summary>
    /// Every _rotationInterval seconds, snaps each pillar (or the whole group,
    /// when orbiting around the parent pivot) by a step around the Y axis.
    /// The parent (LightPilars) origin is used as the rotation pivot, so beams
    /// sweep across the room like searchlights.
    /// </summary>
    private void SnapRotate()
    {
        _rotationTimer += Time.deltaTime;
        if (_rotationTimer < _rotationInterval) return;
        _rotationTimer = 0f;

        float step = _rotationStepDegrees;
        if (_randomStepSize)
            step *= Random.Range(0.5f, 1.5f);
        if (_randomStepSign && Random.value < 0.5f)
            step = -step;

        Transform pivot = transform.parent != null ? transform.parent : transform;

        if (_orbitAroundParent)
        {
            // Rotate the whole group around the parent's pivot — beams sweep the room
            pivot.Rotate(0f, step, 0f, Space.World);
        }
        else
        {
            // Spin each pillar around its own origin
            foreach (Transform child in transform)
                child.Rotate(0f, step, 0f, Space.World);
        }

        if (_varyTilt)
            VaryTilts();
    }

    /// <summary>
    /// Nudges every pillar's tilt around its local Z axis by a small random
    /// amount each snap (up to ±_tiltVariancePercent of its base tilt).
    /// </summary>
    private void VaryTilts()
    {
        for (int i = 0; i < _pillars.Count; i++)
        {
            if (_pillars[i] == null) continue;

            float baseTilt = _baseTilts[i];
            // Normalize base tilt to the nearest ±90 range for stable variance
            float variance = baseTilt * _tiltVariancePercent;
            float newTilt = baseTilt + Random.Range(-variance, variance);
            Vector3 euler = _pillars[i].localEulerAngles;
            _pillars[i].localRotation = Quaternion.Euler(euler.x, euler.y, newTilt);
        }
    }

    private float ReadLoudness()
    {
        if (_musicSource == null || _musicSource.clip == null) return 0f;

        _musicSource.GetSpectrumData(_spectrum, 0, FFTWindow.Blackman);
        float sum = 0f;
        for (int i = 0; i < _spectrum.Length; i++)
            sum += _spectrum[i];

        float average = sum / _spectrum.Length;
        return Mathf.Clamp01(average * 10f);
    }

    private void SetIntensity(float value)
    {
        if (_pillarMaterials == null) return;
        foreach (Material mat in _pillarMaterials)
            if (mat != null) mat.SetFloat("_MusicIntensity", value);
    }

    private void SetColor(Color color)
    {
        if (_pillarMaterials == null) return;
        foreach (Material mat in _pillarMaterials)
            if (mat != null) mat.SetColor("_Color", color);
    }
}
