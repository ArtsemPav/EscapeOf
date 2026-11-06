using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One patrol point of the invisible ghost entity. While the entity dwells
/// here, it can provoke horror events: play 3D footsteps / knock sounds.
/// Events fire only if the player is currently in one of the allowed rooms
/// (matched by <see cref="RoomVisibilityManager.CurrentRoomName"/>), with a
/// cooldown to prevent spam. Sound objects are played via a short sequence
/// of <see cref="AudioSource"/> steps to fake walking around the room.
/// </summary>
public class GhostWaypoint : MonoBehaviour
{
    [System.Serializable]
    public class SoundStep
    {
        [Tooltip("Delay before this step plays (seconds), counted from provocation start.")]
        public float Delay;

        [Tooltip("AudioSource (3D spatial) placed where the sound should come from.")]
        public AudioSource Source;
    }

    [Tooltip("Room names (as in RoomVisibilityManager) where the player must be for events to fire. Empty = any room.")]
    [SerializeField] private List<string> _allowedRooms = new List<string>();

    [Tooltip("Trigger radius: if the player is within this range of the waypoint, provocation can fire.")]
    [SerializeField] private float _playerDetectionRadius = 12f;

    [Tooltip("Minimum seconds between two provocations from ANY waypoint (global cooldown).")]
    [SerializeField] private float _globalCooldown = 45f;

    [Tooltip("Cooldown specific to this waypoint after it fired.")]
    [SerializeField] private float _perWaypointCooldown = 180f;

    [Tooltip("Chance (0-1) that a detected provocation actually fires. Lets you tune spookiness without removing waypoints.")]
    [Range(0f, 1f)]
    [SerializeField] private float _provocationChance = 0.6f;

    [Tooltip("Relative chance this waypoint is picked during patrol (weighted random). 1 = normal, 3 = three times as likely. Use to keep the ghost mostly on some floors (e.g. 2F weight 3) while it still roams the basement (weight 1).")]
    [SerializeField] private float _visitWeight = 1f;

    [Tooltip("Sound steps: sequence of delayed AudioSource plays (footsteps / knocks).")]
    [SerializeField] private List<SoundStep> _soundSteps = new List<SoundStep>();

    private static float _lastGlobalProvocationTime = float.MinValue;
    private float _lastProvocationTime = float.MinValue;
    private Transform _player;

    public List<GhostWaypoint> Connected { get; } = new List<GhostWaypoint>();

    /// <summary>Relative pick chance during patrol (weighted random).</summary>
    public float VisitWeight => _visitWeight;

    /// <summary>
    /// Called by GhostPatrol when the entity starts dwelling at this waypoint.
    /// Evaluates whether a provocation should fire and schedules its sound steps.
    /// </summary>
    public void OnGhostArrived()
    {
        if (!PlayerInRange()) return;
        if (!PlayerInAllowedRoom()) return;

        float now = Time.time;
        if (now - _lastGlobalProvocationTime < _globalCooldown) return;
        if (now - _lastProvocationTime < _perWaypointCooldown) return;
        if (Random.value > _provocationChance) return;

        _lastProvocationTime = now;
        _lastGlobalProvocationTime = now;

        foreach (SoundStep step in _soundSteps)
        {
            if (step.Source == null) continue;
            float delay = step.Delay;
            AudioSource source = step.Source;
            StartCoroutine(PlayAfterDelay(source, delay));
        }
    }

    private System.Collections.IEnumerator PlayAfterDelay(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (source != null) source.Play();
    }

    private bool PlayerInRange()
    {
        if (_player == null)
        {
            FPSController player = FindFirstObjectByType<FPSController>();
            if (player == null) return false;
            _player = player.transform;
        }
        return Vector3.Distance(transform.position, _player.position) <= _playerDetectionRadius;
    }

    private bool PlayerInAllowedRoom()
    {
        if (_allowedRooms == null || _allowedRooms.Count == 0) return true;
        string current = RoomVisibilityManager.Instance != null
            ? RoomVisibilityManager.Instance.CurrentRoomName
            : null;
        return current != null && _allowedRooms.Contains(current);
    }
}
