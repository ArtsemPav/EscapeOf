using Escape.Core;
using UnityEngine;

/// <summary>
/// Plays 3D footstep sounds synced to the ghost agent's actual movement:
/// one step per <see cref="_stepLength"/> meters traveled. Faster chase =
/// faster steps. Steps are heard ONLY from a distance — the muffled
/// "someone walking in other rooms" ambience; steps near the player are
/// dropped. Chase steps are louder and less muffled than patrol steps.
/// Steps are also occlusion-aware: when a raycast to the player is blocked
/// (wall, floor slab or a closed door between the agent and the player),
/// the step is heavily muffled and quieted — the "through the thick floor /
/// wall" sound. Only an unobstructed path lets the steps ring clear.
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

    [Header("Occlusion (steps through floors / walls)")]

    [Tooltip("When the direct path to the player is blocked (floor slab, wall, closed door), the step plays this much quieter. 0.15 = a barely audible thump through the ceiling.")]
    [SerializeField, Range(0f, 1f)] private float _occludedVolumeFactor = 0.15f;

    [Tooltip("Low-pass cutoff (Hz) for occluded steps. Low value = a dull muffled thud, as if through a thick concrete floor.")]
    [SerializeField, Range(100f, 4000f)] private float _occludedCutoffHz = 500f;

    [Tooltip("Seconds between occlusion raycasts. Steps only need it when they actually play, so this just spaces the checks out.")]
    [SerializeField, Range(0.05f, 1f)] private float _occlusionCheckInterval = 0.25f;

    private AudioSource _source;
    private UnityEngine.AudioLowPassFilter _lowPass;
    private FPSController _player;    private Vector3 _lastPosition;
    private float _distanceSinceStep;
    private bool _nextStepIsLeft = true;
    private float _nextOcclusionCheckTime;
    private bool _isOccluded;
    private static readonly RaycastHit[] OcclusionHits = new RaycastHit[16];

    /// <summary>Layers that occlude the direct path to the player: everything
    /// except Ignore Raycast and the player's own layer.</summary>
    private static int _occluderMask = -1;

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
    /// When the direct path to the player is occluded (floor slab between the
    /// floors, wall, closed door), the step is heavily muted and low-passed —
    /// a dull thud "through the thick floor" instead of a clear step.
    /// </summary>
    private void PlayStep()
    {
        AudioClip clip = _nextStepIsLeft ? _leftClip : _rightClip;
        if (clip == null) clip = _leftClip != null ? _leftClip : _rightClip;
        if (clip == null) return;

        // Flip the foot for the next step only after a clip was actually found.
        _nextStepIsLeft = !_nextStepIsLeft;

        Vector3 playerPosition = GetPlayerPosition();
        Vector3 agentPosition = transform.position;
        float distance = Vector3.Distance(agentPosition, playerPosition);
        bool chasing = _patrol.IsChasing;

        // Too close = silence. The ghost is "in your room" — the player must SEE
        // it coming, not hear a step reveal it. The threshold is per-mode: far
        // away for the distant-room ambience, tiny for the chase so the
        // approaching hunt stays audible right up to the catch.
        float minDistance = chasing ? _chaseAudibleMinDistance : _audibleMinDistance;
        if (distance < minDistance) return;

        // Occlusion check (raycast, cached between steps): is the direct path
        // to the player blocked by a floor slab / wall / closed door? A ghost
        // one floor below must sound like a muffled thump through concrete,
        // not like someone standing in front of you.
        if (Time.unscaledTime >= _nextOcclusionCheckTime)
        {
            _nextOcclusionCheckTime = Time.unscaledTime + _occlusionCheckInterval;
            _isOccluded = IsPathOccluded(agentPosition, playerPosition);
        }

        // Base mode layering, then the occlusion layering on top.
        float volumeFactor = chasing ? _chaseVolumeFactor : _patrolVolumeFactor;
        float cutoffHz = chasing ? _chaseCutoffHz : _patrolCutoffHz;
        if (_isOccluded)
        {
            volumeFactor *= _occludedVolumeFactor;
            cutoffHz = Mathf.Min(cutoffHz, _occludedCutoffHz);
        }

        _lowPass.cutoffFrequency = cutoffHz;
        _source.pitch = Random.Range(_pitchRange.x, _pitchRange.y);
        _source.volume = Random.Range(_volumeRange.x, _volumeRange.y) * volumeFactor;
        _source.PlayOneShot(clip);
    }

    /// <summary>
    /// True when a raycast from the agent to the player hits geometry before
    /// reaching them: a floor slab, a wall, furniture or (optionally) a closed
    /// door. The player's own colliders are ignored via layer mask.
    /// </summary>
    private bool IsPathOccluded(Vector3 from, Vector3 to)
    {
        if (_occluderMask == -1)
        {
            // Everything except Ignore Raycast. The player's own colliders are
            // skipped by comparing hits against the player component below.
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            _occluderMask = ignoreRaycastLayer >= 0 ? ~(1 << ignoreRaycastLayer) : ~0;
        }

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.01f) return false;

        Vector3 direction = delta / distance;
        int hitCount = Physics.RaycastNonAlloc(
            from, direction, OcclusionHits, distance, _occluderMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = OcclusionHits[i].collider;

            // Skip the player's own colliders (capsule, etc.).
            if (hit.GetComponentInParent<FPSController>() != null) continue;

            // Open door panels do not occlude — you can see/hear through them.
            DoorInteraction door = hit.GetComponentInParent<DoorInteraction>();
            if (door != null && door.IsOpen) continue;

            return true;
        }

        return false;
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
