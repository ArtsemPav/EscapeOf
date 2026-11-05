using UnityEngine;

/// <summary>
/// Animates a metronome needle swinging around a local axis (Z by default).
/// The needle moves linearly (triangle wave, like a real metronome)
/// and plays a tick sound as a 3D one-shot at each extreme position.
/// The tempo (beats per minute) is randomly changed once every
/// _tempoChangeIntervalMinutes minutes within [_minBpm, _maxBpm].
/// </summary>
public class MetronomeNeedle : MonoBehaviour
{
    [Header("Swing")]
    [Tooltip("Maximum needle angle in degrees on each side of the rest position.")]
    [SerializeField] private float _amplitude = 30f;

    [Tooltip("Beats per minute. One beat = one full half-swing between extremes.")]
    [SerializeField, Min(1f)] private float _beatsPerMinute = 60f;

    [Tooltip("Local axis the needle rotates around.")]
    [SerializeField] private Vector3 _swingAxis = Vector3.forward;

    [Header("Tempo Change")]
    [Tooltip("Interval in minutes between random tempo changes.")]
    [SerializeField, Min(0.1f)] private float _tempoChangeIntervalMinutes = 5f;

    [Tooltip("Lowest random tempo in beats per minute.")]
    [SerializeField, Min(1f)] private float _minBpm = 50f;

    [Tooltip("Highest random tempo in beats per minute.")]
    [SerializeField, Min(1f)] private float _maxBpm = 110f;

    [Header("Sound")]
    [Tooltip("Tick clip played on every extreme position as a 3D one-shot at the needle position.")]
    [SerializeField] private AudioClip _tickSound;

    [Tooltip("Volume of the tick one-shot.")]
    [SerializeField, Range(0f, 1f)] private float _tickVolume = 0.6f;

    [Tooltip("Distance at which the tick is at full volume.")]
    [SerializeField, Min(0.01f)] private float _tickMinDistance = 1f;

    [Tooltip("Distance beyond which the tick is no longer audible.")]
    [SerializeField, Min(0.01f)] private float _tickMaxDistance = 10f;

    // ── Constants ──────────────────────────────────────────────────────────────

    private const float SecondsPerMinute = 60f;

    // ── State ──────────────────────────────────────────────────────────────────

    private Quaternion _initialLocalRotation;
    private float _currentAngle;   // degrees relative to the rest position
    private float _direction = 1f; // +1 swinging to the positive extreme, -1 to the negative
    private float _secondsSinceLastTempoChange;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _initialLocalRotation = transform.localRotation;
        _currentAngle = 0f;
        _secondsSinceLastTempoChange = 0f;
    }

    private void Update()
    {
        AdvanceSwing(Time.deltaTime);
        UpdateTempoTimer(Time.deltaTime);
    }

    // ── Swing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the needle linearly between the extremes and ticks on each bounce.
    /// One beat (half-swing) lasts SecondsPerMinute / _beatsPerMinute seconds.
    /// </summary>
    private void AdvanceSwing(float deltaTime)
    {
        float degreesPerSecond = 2f * _amplitude * _beatsPerMinute / SecondsPerMinute;
        _currentAngle += _direction * degreesPerSecond * deltaTime;

        if (_currentAngle >= _amplitude)
        {
            // Reflect off the positive extreme so speed stays continuous.
            _currentAngle = 2f * _amplitude - _currentAngle;
            _direction = -1f;
            PlayTick();
        }
        else if (_currentAngle <= -_amplitude)
        {
            _currentAngle = -2f * _amplitude - _currentAngle;
            _direction = 1f;
            PlayTick();
        }

        transform.localRotation = _initialLocalRotation *
            Quaternion.AngleAxis(_currentAngle, _swingAxis);
    }

    // ── Tempo ──────────────────────────────────────────────────────────────────

    /// <summary>Picks a new random tempo once every configured interval.</summary>
    private void UpdateTempoTimer(float deltaTime)
    {
        _secondsSinceLastTempoChange += deltaTime;

        float intervalSeconds = _tempoChangeIntervalMinutes * SecondsPerMinute;
        if (_secondsSinceLastTempoChange < intervalSeconds)
            return;

        _secondsSinceLastTempoChange -= intervalSeconds;
        _beatsPerMinute = Random.Range(_minBpm, _maxBpm);
    }

    // ── Sound ──────────────────────────────────────────────────────────────────

    /// <summary>Plays the tick as a 3D one-shot at the needle's world position through AudioManager.</summary>
    private void PlayTick()
    {
        if (_tickSound == null)
            return;

        AudioManager.Instance?.Play3DOneShot(
            _tickSound,
            transform.position,
            _tickVolume,
            _tickMinDistance,
            _tickMaxDistance);
    }
}
