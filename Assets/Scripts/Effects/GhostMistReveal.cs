using UnityEngine;

/// <summary>
/// Shows the invisible spirit as a dissolving cloud of mist: the particle
/// system is teleported to the ghost agent, plays once, and fades out —
/// the entity manifests for a couple of seconds, then vanishes again.
/// Wired to GhostPatrol's OnAgentManifested event (waypoint arrivals and the
/// moment the hunt starts). The particle system must be a disabled,
/// non-looping, non-play-on-awake child prefab instance (GhostMistCloud).
/// </summary>
public class GhostMistReveal : MonoBehaviour
{
    [Tooltip("GhostPatrol whose manifestation moments drive the reveal. Auto-resolved if left empty.")]
    [SerializeField] private GhostPatrol _patrol;

    [Tooltip("One-shot mist cloud particle system (disabled at start).")]
    [SerializeField] private ParticleSystem _mistCloud;

    /// <summary>The GameObject the mist cloud is parented to (the agent).</summary>
    private Transform _agent;

    private void Awake()
    {
        if (_patrol == null) _patrol = FindFirstObjectByType<GhostPatrol>();
        if (_mistCloud == null)
        {
            Debug.LogWarning("GhostMistReveal: no mist cloud assigned.", this);
            enabled = false;
            return;
        }
        _agent = _mistCloud.transform.parent;
    }

    private void OnEnable()
    {
        if (_patrol != null) _patrol.OnAgentManifested += ShowMist;
    }

    private void OnDisable()
    {
        if (_patrol != null) _patrol.OnAgentManifested -= ShowMist;
    }

    /// <summary>Teleports the cloud to the agent and replays it from the beginning.</summary>
    private void ShowMist()
    {
        if (_mistCloud == null) return;
        if (_agent != null) _mistCloud.transform.position = _agent.position;
        _mistCloud.gameObject.SetActive(true);
        _mistCloud.Play(true);
    }
}
