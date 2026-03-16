using UnityEngine;

/// <summary>
/// Audio-reactive neon light flicker.
/// Plays an AudioClip in a loop and maps its real-time RMS amplitude to
/// both the Light intensity and MeshRenderer emission each frame.
/// No coroutines, no overlapping sounds — the audio waveform IS the flicker pattern.
///
/// Tuning guide:
///   _sensitivity  — raise if the light barely reacts, lower if it's always fully bright.
///   _smoothing    — low (1–3) = snappy transients; high (10+) = slow atmospheric glow.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class NeonLightFlicker : MonoBehaviour
{
    [SerializeField] private Light _flickerLight;

    [Header("Audio")]
    [Tooltip("Pool of clips to randomly pick from. Each clip plays fully, then a new one is picked at random.\n" +
             "Add as many clips as needed — drag them directly here in the Inspector.")]
    [SerializeField] private AudioClip[] _flickerSounds;
    [Tooltip("Playback volume (0–1).")]
    [SerializeField] [Range(0f, 1f)] private float _soundVolume = 0.8f;
    [Tooltip("3D blend (0 = fully 2D, 1 = fully 3D positional).")]
    [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 1f;
    [Tooltip("Distance at which the sound is at full volume.")]
    [SerializeField] private float _minDistance = 1f;
    [Tooltip("Distance at which the sound becomes inaudible.")]
    [SerializeField] private float _maxDistance = 8f;

    [Header("Light Response")]
    [Tooltip("Audio samples read per frame for RMS calculation. Automatically rounded to nearest power of 2.")]
    [SerializeField] private int _sampleSize = 64;
    [Tooltip("Multiplier applied to the RMS value to map it into the 0–1 light range.\n" +
             "Increase if the light barely reacts; decrease if it stays fully on.")]
    [SerializeField] private float _sensitivity = 80f;
    [Tooltip("Light intensity fraction when audio is silent.")]
    [SerializeField] [Range(0f, 1f)] private float _minIntensity = 0f;
    [Tooltip("Light intensity fraction at audio peak.")]
    [SerializeField] [Range(0f, 2f)] private float _maxIntensity = 1f;
    [Tooltip("How quickly the light follows audio amplitude.\n" +
             "Low (1–3) = slow glow. High (15–30) = sharp snappy flickers.")]
    [SerializeField] private float _smoothing = 20f;
    [Tooltip("Power curve applied after smoothing. 1 = linear.\n" +
             "Values below 1 push toward bright (e.g. 0.5).\n" +
             "Values above 1 push toward dark, maximising on/off contrast (e.g. 2–4).")]
    [SerializeField] [Range(0.1f, 5f)] private float _contrast = 2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MeshRenderer _meshRenderer;
    private Material _instanceMaterial;
    private Color _baseEmissionColor;
    private float _baseIntensity;

    private AudioSource _audioSource;
    private float[] _samples;
    private float _currentNormalized;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _instanceMaterial = _meshRenderer.material;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.spatialBlend = _spatialBlend;
        _audioSource.minDistance = _minDistance;
        _audioSource.maxDistance = _maxDistance;
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.volume = _soundVolume;

        _sampleSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_sampleSize, 64, 8192));
        _samples = new float[_sampleSize];
    }

    private void Start()
    {
        if (_flickerLight == null)
        {
            Debug.LogWarning($"[NeonLightFlicker] No Light assigned on '{name}'.", this);
            return;
        }

        _baseIntensity = _flickerLight.intensity;
        _baseEmissionColor = _instanceMaterial.GetColor(EmissionColorId);

        if (_flickerSounds != null && _flickerSounds.Length > 0)
            PlayNextClip();
        else
            Debug.LogWarning($"[NeonLightFlicker] No audio clips assigned on '{name}'.", this);
    }

    private void OnDestroy()
    {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);
    }

    private void Update()
    {
        // When current clip finishes, immediately start a different one
        if (!_audioSource.isPlaying)
            PlayNextClip();

        // Read raw samples directly from the clip at the current playback position.
        // AudioClip.GetData bypasses all spatial attenuation and volume, so the
        // light flickers correctly regardless of how far the player is from the lamp.
        _audioSource.clip.GetData(_samples, _audioSource.timeSamples);
        float rms = CalculateRMS(_samples);

        // Scale RMS into 0–1, then smooth toward it so the light doesn't teleport
        float target = Mathf.Clamp01(rms * _sensitivity);
        _currentNormalized = Mathf.Lerp(_currentNormalized, target, _smoothing * Time.deltaTime);

        // Apply contrast curve: pushes values away from mid-grey toward 0 or 1
        float contrasted = Mathf.Pow(_currentNormalized, _contrast);

        // Remap into the configured intensity band and apply to light + emission
        float mapped = Mathf.Lerp(_minIntensity, _maxIntensity, contrasted);
        ApplyNormalized(mapped);
    }

    /// <summary>Applies a normalized [0..1] multiplier to Light intensity and material emission.</summary>
    private void ApplyNormalized(float normalized)
    {
        _flickerLight.intensity = _baseIntensity * normalized;
        _instanceMaterial.SetColor(EmissionColorId, _baseEmissionColor * normalized);
    }

    /// <summary>Picks a random clip from the pool and plays it.</summary>
    private void PlayNextClip()
    {
        _audioSource.clip = _flickerSounds[Random.Range(0, _flickerSounds.Length)];
        _audioSource.Play();
    }

    /// <summary>Root Mean Square — perceptual loudness of a sample buffer.</summary>
    private static float CalculateRMS(float[] samples)
    {
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];
        return Mathf.Sqrt(sum / samples.Length);
    }
}
