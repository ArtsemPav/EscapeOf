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

    [Header("Climax Mode")]
    [Tooltip("If enabled this light stays at its original state until the climax chaos starts " +
             "(particles + pillars moment), then joins the color/loudness dance with everyone else.")]
    [SerializeField] private bool _startAtClimax = false;

    [Tooltip("Room lights that should follow the music in addition to this light " +
             "(e.g. the ceiling neon lamp of the room).")]
    [SerializeField] private Light[] _extraLightsToDrive;

    private Light _light;
    private Color _baseColor;
    private float _baseIntensity;
    private int _colorIndex;
    private int _beatCounter;
    private float _currentIntensity;
    private float[] _spectrum;
    private Color[] _extraBaseColors;
    private float[] _extraBaseIntensities;
    private Vector3? _lastCatPosition;
    private bool _inPreClimax;

    [Header("Cat Movement Sync")]
    [Tooltip("If true, the color snaps when the CAT moves by Cat Move Distance (dance-sync), instead of on beats.")]
    [SerializeField] private bool _syncColorToCatMovement = true;

    [Tooltip("Cat displacement (meters) that triggers a color snap.")]
    [SerializeField, Min(0.01f)] private float _catMoveDistance = 0.15f;

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

        // Remember the original state of the extra room lights for restore
        if (_extraLightsToDrive != null && _extraLightsToDrive.Length > 0)
        {
            _extraBaseColors = new Color[_extraLightsToDrive.Length];
            _extraBaseIntensities = new float[_extraLightsToDrive.Length];
            for (int i = 0; i < _extraLightsToDrive.Length; i++)
            {
                if (_extraLightsToDrive[i] == null) continue;
                _extraBaseColors[i] = _extraLightsToDrive[i].color;
                _extraBaseIntensities[i] = _extraLightsToDrive[i].intensity;
            }
        }
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

        // Pre-climax intro: colors advance on beats (smooth lerp).
        // Climax: in cat-sync mode the color advances from the cat's dance instead.
        bool colorFromBeats = !_syncColorToCatMovement || _inPreClimax;
        if (colorFromBeats && _beatCounter % _beatsPerColor == 0)
            AdvanceColor();
    }

    private void Update()
    {
        if (_light == null) return;

        // Climax mode: keep the original look until the chaos starts
        if (_startAtClimax && !UVPentagramSequence.ClimaxStarted)
        {
            RestoreOriginal();
            return;
        }

        // Effects only while the music is actually playing
        if (!UVPentagramSequence.MusicPlaying || _musicSource == null)
        {
            // Keep the light at its original state
            RestoreOriginal();
            return;
        }

        // Smooth intro while the music plays but the chaos hasn't started:
        // colors advance on beats and blend slowly (gentle dance lighting)
        _inPreClimax = !UVPentagramSequence.ClimaxStarted;

        // Track the cat's movement — a position change is the trigger for the
        // color snap, so the light color follows the cat's dance steps exactly
        TrackCatMovement();

        // Pre-climax: smooth lerp toward the palette color.
        // Climax: the color SNAPS to the palette entry picked by the cat's movement.
        if (_colors != null && _colors.Length > 0)
        {
            float lerpSpeed = _inPreClimax ? _colorLerpSpeed : 1000f; // 1000 ≈ instant snap
            _light.color = Color.Lerp(_light.color, _colors[_colorIndex], lerpSpeed * Time.deltaTime);
        }

        // Loudness → intensity: sample the spectrum, average it, map to boost
        float loudness = ReadLoudness();
        float targetBoost = Mathf.Lerp(_minIntensityBoost, _maxIntensityBoost, loudness);
        // Pre-climax: gentler intensity (half boost), climax: full boost
        float boostScale = _inPreClimax ? 0.5f : 1f;
        _currentIntensity = Mathf.Lerp(_currentIntensity, _baseIntensity + targetBoost * boostScale, _loudnessLerpSpeed * Time.deltaTime);
        _light.intensity = _currentIntensity;

        DriveExtraLights(loudness);
    }

    /// <summary>
    /// Watches the cat's animation node. When the cat has moved at least
    /// _catMoveDistance since the last color snap, the color index advances —
    /// the light color changes in sync with the cat's movements.
    /// </summary>
    private void TrackCatMovement()
    {
        Transform cat = UVPentagramSequence.InstanceCatTransform;
        if (cat == null) return;

        if (_lastCatPosition.HasValue)
        {
            float moved = Vector3.Distance(cat.position, _lastCatPosition.Value);
            if (moved >= _catMoveDistance)
            {
                _lastCatPosition = cat.position;
                AdvanceColor();
            }
        }
        else
        {
            _lastCatPosition = cat.position;
        }
    }

    private void AdvanceColor()
    {
        if (_colors == null || _colors.Length == 0) return;

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

    private void RestoreOriginal()
    {
        _light.color = _baseColor;
        _light.intensity = _baseIntensity;
        _lastCatPosition = null; // re-anchor when the dance resumes
        DriveExtraLights(0f, restoreOnly: true);
    }

    /// <summary>
    /// Applies the same color cycle and loudness boost to the extra room lights.
    /// Their original intensity is preserved as the base; a scale factor adjusts
    /// how much louder-than-base they get compared to the main light.
    /// </summary>
    private void DriveExtraLights(float loudness, bool restoreOnly = false)
    {
        if (_extraLightsToDrive == null) return;

        for (int i = 0; i < _extraLightsToDrive.Length; i++)
        {
            Light extra = _extraLightsToDrive[i];
            if (extra == null) continue;

            if (restoreOnly)
            {
                if (_extraBaseColors != null)
                {
                    extra.color = _extraBaseColors[i];
                    extra.intensity = _extraBaseIntensities[i];
                }
                continue;
            }

            if (_colors != null && _colors.Length > 0)
                extra.color = Color.Lerp(extra.color, _colors[_colorIndex], _colorLerpSpeed * Time.deltaTime);

            float baseIntensity = _extraBaseIntensities != null ? _extraBaseIntensities[i] : extra.intensity;
            float targetBoost = Mathf.Lerp(_minIntensityBoost, _maxIntensityBoost, loudness);
            extra.intensity = Mathf.Lerp(extra.intensity, baseIntensity + targetBoost * 0.5f, _loudnessLerpSpeed * Time.deltaTime);
        }
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
