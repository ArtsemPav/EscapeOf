using UnityEngine;

/// <summary>
/// Plays 3D footstep sounds synced to the ghost agent's actual movement:
/// one step per <see cref="_stepLength"/> meters traveled. Faster chase =
/// faster steps. Silent while the agent dwells at a waypoint. Requires an
/// attached 3D AudioSource (spatialBlend = 1) — the sound comes from the agent.
/// Left and right foot clips alternate per step, matching the player's
/// FootstepController convention.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GhostFootsteps : MonoBehaviour
{
    [Tooltip("GhostPatrol that owns the moving agent. Auto-resolved to the first one in the scene if left empty.")]
    [SerializeField] private GhostPatrol _patrol;

    [Tooltip("Clip for the left foot. Leave empty to reuse the right-foot clip.")]
    [SerializeField] private AudioClip _leftClip;

    [Tooltip("Clip for the right foot. Leave empty to reuse the left-foot clip.")]
    [SerializeField] private AudioClip _rightClip;

    [Tooltip("Meters of travel per footstep while WALKING (patrol). Lower = faster cadence.")]
    [SerializeField] private float _stepLength = 0.7f;

    [Tooltip("Meters of travel per footstep while RUNNING (chase). Longer stride = fewer, heavier steps instead of a tiny shuffle. Set equal to _stepLength to disable stride scaling.")]
    [SerializeField] private float _runStepLength = 1.6f;

    [Tooltip("Random pitch range per step (1 = original). Keep the range narrow for a consistent gait.")]
    [SerializeField] private Vector2 _pitchRange = new Vector2(0.95f, 1.05f);

    [Tooltip("Random volume range per step (multiplied by AudioSource volume and the floor factor).")]
    [SerializeField] private Vector2 _volumeRange = new Vector2(0.6f, 0.9f);

    [Header("Floor Muffling")]

    [Tooltip("Vertical distance (meters) between the ghost and the player above which they count as different floors.")]
    [SerializeField] private float _floorHeightThreshold = 3f;

    [Tooltip("Volume multiplier while the ghost is on the PLAYER's floor (slightly quieter).")]
    [SerializeField, Range(0f, 1f)] private float _sameFloorVolumeFactor = 0.75f;

    [Tooltip("Volume multiplier while the ghost is on ANOTHER floor (muffled steps through the ceiling).")]
    [SerializeField, Range(0f, 1f)] private float _otherFloorVolumeFactor = 0.35f;

    [Tooltip("Low-pass cutoff (Hz) for the muffled other-floor steps. Lower = duller.")]
    [SerializeField] private float _otherFloorCutoffHz = 900f;

    [Tooltip("Low-pass cutoff (Hz) when the ghost is on the player's floor (20k = no filtering).")]
    [SerializeField] private float _sameFloorCutoffHz = 20000f;

    [Tooltip("Distance (meters) up to which same-floor steps play at full volume.")]
    [SerializeField] private float _fullVolumeDistance = 4f;

    [Tooltip("Distance (meters) at which same-floor steps reach their quietest level.")]
    [SerializeField] private float _fadeoutDistance = 14f;

    [Tooltip("Volume multiplier at and beyond the fade-out distance on the same floor.")]
    [SerializeField, Range(0f, 1f)] private float _farVolumeFactor = 0.35f;

    private AudioSource _source;
    private UnityEngine.AudioLowPassFilter _lowPass;
    private FPSController _player;    private Vector3 _lastPosition;
    private float _distanceSinceStep;
    private bool _nextStepIsLeft = true;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _lowPass = GetComponent<UnityEngine.AudioLowPassFilter>();
        if (_lowPass == null) _lowPass = gameObject.AddComponent<UnityEngine.AudioLowPassFilter>();
    }

    private void Start()
    {
        if (_patrol == null) _patrol = FindFirstObjectByType<GhostPatrol>();
        if (_patrol == null)
        {
            Debug.LogWarning("GhostFootsteps: no GhostPatrol found in the scene.", this);
            enabled = false;
            return;
        }

        _lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 currentPosition = transform.position;
        Vector3 delta = currentPosition - _lastPosition;
        float travel = delta.magnitude;
        _lastPosition = currentPosition;

        if (!_patrol.IsMoving || travel < 0.0001f) return;

        // Stride grows with speed: walking takes short steps, running long strides.
        // Without this the chase cadence goes absurdly fast (~6 steps/sec) and
        // sounds like a child shuffling in tiny steps.
        float stepLength = _patrol.IsChasing ? _runStepLength : _stepLength;

        _distanceSinceStep += travel;
        if (_distanceSinceStep < stepLength) return;

        // Consume the full travel so the cadence stays stable at any speed.
        _distanceSinceStep %= stepLength;
        PlayStep();
    }

    /// <summary>
    /// Plays the alternating left/right footstep clip with randomized pitch and volume.
    /// Volume and tone depend on the ghost's position relative to the player:
    /// same floor = loud up close, fading with horizontal distance; other floor
    /// = uniformly muffled thumps (low volume + low-pass) audible anywhere on
    /// the floor above/below. Distance attenuation is computed manually — the
    /// AudioSource's own rolloff is bypassed by keeping maxDistance large.
    /// </summary>
    private void PlayStep()
    {
        AudioClip clip = _nextStepIsLeft ? _leftClip : _rightClip;
        if (clip == null) clip = _leftClip != null ? _leftClip : _rightClip;
        if (clip == null) return;

        // Flip the foot for the next step only after a clip was actually found.
        _nextStepIsLeft = !_nextStepIsLeft;

        Vector3 playerPosition = GetPlayerPosition();
        float distance = Vector3.Distance(transform.position, playerPosition);
        bool sameFloor = IsPlayerOnGhostFloor();

        float distanceFactor;
        if (sameFloor)
        {
            // Loud up close, smoothly fading to a quiet floor level with distance.
            float fade = Mathf.InverseLerp(_fadeoutDistance, _fullVolumeDistance, distance);
            distanceFactor = Mathf.Lerp(_farVolumeFactor, 1f, fade) * _sameFloorVolumeFactor;
        }
        else
        {
            // Other floor: uniform muffled level everywhere on that floor.
            distanceFactor = _otherFloorVolumeFactor;
        }

        _lowPass.cutoffFrequency = sameFloor ? _sameFloorCutoffHz : _otherFloorCutoffHz;
        _source.pitch = Random.Range(_pitchRange.x, _pitchRange.y);
        _source.volume = Random.Range(_volumeRange.x, _volumeRange.y) * distanceFactor;
        _source.PlayOneShot(clip);
    }

    /// <summary>Cached player position (found once, refreshed if destroyed).</summary>
    private Vector3 GetPlayerPosition()
    {
        if (_player == null)
        {
            _player = FindFirstObjectByType<FPSController>();
            if (_player == null) return transform.position;
        }
        return _player.transform.position;
    }

    /// <summary>True when the ghost and the player share a floor (vertical gap is small).</summary>
    private bool IsPlayerOnGhostFloor()
    {
        return Mathf.Abs(transform.position.y - GetPlayerPosition().y) <= _floorHeightThreshold;
    }
}
