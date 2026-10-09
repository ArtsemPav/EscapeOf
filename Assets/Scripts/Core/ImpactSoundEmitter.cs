using UnityEngine;

/// <summary>
/// Plays an impact sound when the Rigidbody on this object hits something hard enough.
/// Volume scales with the impact velocity; a cooldown prevents the clip from
/// retriggering while the object settles (e.g. rocking after landing).
/// Requires a Rigidbody and a non-trigger Collider.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ImpactSoundEmitter : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Clips to play on impact. One is picked at random each hit.")]
    [SerializeField] private AudioClip[] _impactClips;

    [Tooltip("Volume of the sound at an impact of exactly Min Impact Velocity.")]
    [SerializeField] [Range(0f, 1f)] private float _minVolume = 0.4f;

    [Tooltip("Volume of the sound at impacts of Min Impact Velocity or stronger.")]
    [SerializeField] [Range(0f, 1f)] private float _maxVolume = 1f;

    [Header("Impact")]
    [Tooltip("Relative collision velocity (m/s) below which no sound is played.")]
    [SerializeField] private float _minImpactVelocity = 1.5f;

    [Tooltip("Relative collision velocity (m/s) at which the sound reaches full volume.")]
    [SerializeField] private float _maxImpactVelocity = 6f;

    [Tooltip("Seconds between impacts during which new impacts are ignored.")]
    [SerializeField] private float _cooldown = 0.15f;

    [Header("Spatial")]
    [Tooltip("3D spread of the sound. 1 = fully positional (default).")]
    [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 1f;

    [Tooltip("Distance at which the sound starts attenuating.")]
    [SerializeField] private float _minDistance = 1f;

    [Tooltip("Distance at which the sound becomes inaudible.")]
    [SerializeField] private float _maxDistance = 15f;

    private const float MinPitch = 0.9f;
    private const float MaxPitch = 1.1f;

    private AudioSource _audioSource;
    private float _lastImpactTime;

    private void Awake()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = _spatialBlend;
        _audioSource.minDistance = _minDistance;
        _audioSource.maxDistance = _maxDistance;
        _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_impactClips == null || _impactClips.Length == 0) return;
        if (Time.time - _lastImpactTime < _cooldown) return;

        float impactVelocity = collision.relativeVelocity.magnitude;
        if (impactVelocity < _minImpactVelocity) return;

        _lastImpactTime = Time.time;

        // Louder for harder hits, full volume from Max Impact Velocity up.
        float strength = Mathf.InverseLerp(_minImpactVelocity, _maxImpactVelocity, impactVelocity);
        float volume = Mathf.Lerp(_minVolume, _maxVolume, strength);

        // Slight pitch variation so repeated impacts don't sound identical.
        _audioSource.pitch = Random.Range(MinPitch, MaxPitch);
        _audioSource.PlayOneShot(_impactClips[Random.Range(0, _impactClips.Length)], volume);
    }
}
