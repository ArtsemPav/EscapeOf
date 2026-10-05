using UnityEngine;

/// <summary>
/// Pulses a Light on every music beat of the active UVPentagramSequence.
/// Attach to any GameObject with a Light (e.g. the cat's spotlights).
/// Intensity returns to its original value when the music stops or the
/// component is disabled, so no manual restore is needed.
/// </summary>
public class LightBeatPulse : MonoBehaviour
{
    [Tooltip("How much intensity is added on each beat.")]
    [SerializeField] private float _pulseAmount = 2f;

    [Tooltip("How fast the extra intensity decays back (units per second).")]
    [SerializeField] private float _decaySpeed = 6f;

    private Light _light;
    private float _baseIntensity;
    private float _excess;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light != null ? _light.intensity : 0f;
    }

    private void OnEnable() => UVPentagramSequence.OnBeat += OnBeat;
    private void OnDisable()
    {
        UVPentagramSequence.OnBeat -= OnBeat;
        if (_light != null) _light.intensity = _baseIntensity;
    }

    private void OnBeat(int beatIndex) => _excess = _pulseAmount;

    private void Update()
    {
        if (_light == null || _excess <= 0f) return;

        _excess = Mathf.MoveTowards(_excess, 0f, _decaySpeed * Time.deltaTime);
        _light.intensity = _baseIntensity + _excess;
    }
}
