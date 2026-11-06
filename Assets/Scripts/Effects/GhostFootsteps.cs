using UnityEngine;

/// <summary>
/// Plays 3D footstep sounds synced to the ghost agent's actual movement:
/// one step per <see cref="_stepLength"/> meters traveled. Faster chase =
/// faster steps. Steps are heard ONLY from a distance — the muffled
/// "someone walking in other rooms" ambience; steps near the player are
/// dropped. Chase steps are louder and less muffled than patrol steps.
/// Requires an attached 3D AudioSource (spatialBlend = 1). Left and right
/// foot clips alternate per step, matching the player's FootstepController
/// convention.
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

    [Tooltip("PATROL steps are silent closer than this distance (meters) — you hear the ghost walking in other rooms, never right next to you.")]
    [SerializeField] private float _audibleMinDistance = 8f;

    [Tooltip("CHASE steps are silent closer than this distance (meters). Kept tiny so the approaching hunt is always audible.")]
    [SerializeField] private float _chaseAudibleMinDistance = 1.5f;

    [Tooltip("Volume multiplier for distant PATROL steps — the muffled 'someone walking in another room' ambience.")]
    [SerializeField, Range(0f, 1f)] private float _patrolVolumeFactor = 0.6f;

    [Tooltip("Low-pass cutoff (Hz) for distant patrol steps. Lower = duller, more 'through the wall'.")]
    [SerializeField] private float _patrolCutoffHz = 1500f;

    [Tooltip("Volume multiplier for distant CHASE steps (louder than patrol — the ghost is running at you).")]
    [SerializeField, Range(0f, 1f)] private float _chaseVolumeFactor = 0.85f;

    [Tooltip("Low-pass cutoff (Hz) for distant chase steps. Lower = duller, more 'through the wall'.")]
    [SerializeField] private float _chaseCutoffHz = 3000f;

    [Tooltip("Log every played step (distance, mode) to the console for tuning.")]
    [SerializeField] private bool _debugLogging = false;

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
    /// Steps are heard ONLY from a distance (beyond <see cref="_audibleMinDistance"/>):
    /// the player senses someone walking in OTHER rooms, but the ghost is never
    /// announced right next to them. Distant steps are muffled (low-pass), quieter
    /// while patrolling, louder while chasing. Steps too close are simply dropped.
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
        bool chasing = _patrol.IsChasing;

        // Too close = silence. The ghost is "in your room" — the player must SEE
        // it coming, not hear a step reveal it. The threshold is per-mode: far
        // away for the distant-room ambience, tiny for the chase so the
        // approaching hunt stays audible right up to the catch.
        float minDistance = chasing ? _chaseAudibleMinDistance : _audibleMinDistance;
        if (distance < minDistance)
        {
            if (_debugLogging)
            {
                Debug.Log($"[GhostFootsteps] Step SUPPRESSED: player is only {distance:F1} m away (min {minDistance:F0}), chasing={chasing}.", this);
            }
            return;
        }

        float volumeFactor = chasing ? _chaseVolumeFactor : _patrolVolumeFactor;
        _lowPass.cutoffFrequency = chasing ? _chaseCutoffHz : _patrolCutoffHz;

        _source.pitch = Random.Range(_pitchRange.x, _pitchRange.y);
        _source.volume = Random.Range(_volumeRange.x, _volumeRange.y) * volumeFactor;
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
}
