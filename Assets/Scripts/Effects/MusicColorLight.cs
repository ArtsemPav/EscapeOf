using UnityEngine;

/// <summary>
/// Changes a Light's color in sync with the music of the active UVPentagramSequence.
/// Every N beats the light starts lerping toward the next color in the palette
/// (sequential or random order). Intensity follows the loudness of the track:
/// the more dynamic the music, the brighter the light.
///
/// Attach to any GameObject with a Light (e.g. the event's MainLight).
/// Effects only run while UVPentagramSequence.MusicPlaying is true — before the
/// music starts (or after it ends) the light stays in its original static state.
/// </summary>
public class MusicColorLight : MonoBehaviour
{
    [Header("Palette")]
    [Tooltip("Colors the light cycles through while the music plays.")]
    [SerializeField] private Color[] _colors =
    {
        new Color(1f, 0.1f, 0.6f),   // magenta
        new Color(0.1f, 0.7f, 1f),   // cyan
        new Color(0.4f, 1f, 0.2f),   // green
        new Color(1f, 0.5f, 0.1f),   // orange
        new Color(0.55f, 0.2f, 1f)   // purple
    };

    [Tooltip("True = pick a random color each change, False = cycle through the list in order.")]
    [SerializeField] private bool _randomOrder = false;

    [Header("Timing")]
    [Tooltip("How many beats one color lasts. 1 = every beat, 4 = once per bar (in 4/4).")]
    [SerializeField, Min(1)] private int _beatsPerColor = 4;

    [Tooltip("How fast the light lerps to the new color (higher = snappier).")]
    [SerializeField, Min(0.1f)] private float _colorLerpSpeed = 8f;

    [Header("Loudness Reaction")]
    [Tooltip("AudioSource to read loudness from. Auto-found under the sequence instance if left empty.")]
    [SerializeField] private AudioSource _musicSource;

    [Tooltip("Spectrum sample count (power of two, 64–256). More samples = smoother analysis.")]
    [SerializeField, Range(64, 256)] private int _spectrumSamples = 64;

    [Tooltip("Base intensity added on top of the original intensity (loudness 0).")]
    [SerializeField] private float _minIntensityBoost = 0.5f;

    [Tooltip("Maximum extra intensity at full loudness.")]
    [SerializeField] private float _maxIntensityBoost = 4f;

    [Tooltip("How fast intensity follows loudness (higher = snappier).")]
    [SerializeField, Min(0.1f)] private float _loudnessLerpSpeed = 10f;

    private Light _light;
    private Color _baseColor;
    private float _baseIntensity;
    private int _colorIndex;
    private int _beatCounter;
    private float _currentIntensity;
    private float[] _spectrum;

    private void Awake()
    {
        _light = GetComponent<Light>();
        if (_light == null) return;
        _baseColor = _light.color;
        _baseIntensity = _light.intensity;
        _currentIntensity = _baseIntensity;
        _colorIndex = 0;
        // GetSpectrumData requires a power-of-two buffer (min 64)
        int size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_spectrumSamples, 64, 256));
        _spectrum = new float[size];
    }

    private void Start()
    {
        // Auto-find the event's music source (the Audio child of the CatHorror instance)
        if (_musicSource == null && UVPentagramSequence.InstanceMusicSource != null)
            _musicSource = UVPentagramSequence.InstanceMusicSource;
    }

    private void OnEnable() => UVPentagramSequence.OnBeat += OnBeat;

    private void OnDisable()
    {
        UVPentagramSequence.OnBeat -= OnBeat;
        if (_light != null)
        {
            _light.color = _baseColor;
            _light.intensity = _baseIntensity;
        }
    }

    private void OnBeat(int _)
    {
        if (_colors == null || _colors.Length == 0) return;

        _beatCounter++;
        if (_beatCounter % _beatsPerColor == 0)
        {
            if (_randomOrder)
            {
                int previous = _colorIndex;
                _colorIndex = Random.Range(0, _colors.Length);
                if (_colors.Length > 1)
                    while (_colorIndex == previous)
                        _colorIndex = Random.Range(0, _colors.Length);
            }
            else
            {
                _colorIndex = (_colorIndex + 1) % _colors.Length;
            }
        }
    }

    private void Update()
    {
        if (_light == null) return;

        // Effects only while the music is actually playing
        if (!UVPentagramSequence.MusicPlaying || _musicSource == null)
        {
            // Keep the light at its original state
            _light.color = _baseColor;
            _light.intensity = _baseIntensity;
            return;
        }

        if (_colors != null && _colors.Length > 0)
            _light.color = Color.Lerp(_light.color, _colors[_colorIndex], _colorLerpSpeed * Time.deltaTime);

        // Loudness → intensity: sample the spectrum, average it, map to boost
        float loudness = ReadLoudness();
        float targetBoost = Mathf.Lerp(_minIntensityBoost, _maxIntensityBoost, loudness);
        _currentIntensity = Mathf.Lerp(_currentIntensity, _baseIntensity + targetBoost, _loudnessLerpSpeed * Time.deltaTime);
        _light.intensity = _currentIntensity;
    }

    private float ReadLoudness()
    {
        if (_musicSource.clip == null) return 0f;

        _musicSource.GetSpectrumData(_spectrum, 0, FFTWindow.Blackman);
        float sum = 0f;
        for (int i = 0; i < _spectrum.Length; i++)
            sum += _spectrum[i];

        // Normalize: average spectrum value, scaled — real music rarely exceeds ~0.1 average
        float average = sum / _spectrum.Length;
        return Mathf.Clamp01(average * 10f);
    }
}
