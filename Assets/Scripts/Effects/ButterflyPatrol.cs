using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Cycles the butterfly between waypoint children: it sits on the lamp playing
/// the Idle clip, then flies through Position1..PositionN and back to
/// Position0, always turning its body toward the point it is flying to.
/// Attach to the Animator root (the wrapper that holds the PositionN objects).
/// </summary>
public class ButterflyPatrol : MonoBehaviour
{
    private const string WaypointPrefix = "Position";
    private const string SittingStateName = "Idle";
    private const string FlyingStateName = "Flying";
    private const int SittingWaypointIndex = 0;
    private const float MinSegmentDuration = 0.6f;

    [Tooltip("Transform that physically travels between waypoints (the FBX model root). Defaults to the first non-waypoint child.")]
    [SerializeField] private Transform _mover;

    [Tooltip("Seconds the butterfly stays seated on the lamp playing the Idle clip.")]
    [SerializeField] private float _sittingDuration = 8f;

    [Tooltip("Crossfade time when entering the flight state. Keep tiny so wings flap at full speed the moment flight starts.")]
    [SerializeField] private float _flightCrossFade = 0.05f;

    [Tooltip("Flight speed in world units per second.")]
    [SerializeField] private float _flightSpeed = 1.2f;

    [Tooltip("Turning speed toward the flight direction (degrees per second).")]
    [SerializeField] private float _turnSpeed = 240f;

    [Tooltip("Vertical bob amplitude while flying, in meters.")]
    [SerializeField] private float _bobAmplitude = 0.04f;

    [Tooltip("Vertical bob cycles per second while flying.")]
    [SerializeField] private float _bobFrequency = 2.5f;

    private enum Phase
    {
        Sitting,
        Flying
    }

    private Animator _animator;
    private readonly List<Transform> _waypoints = new List<Transform>();
    private Phase _phase = Phase.Sitting;
    private int _targetIndex;
    private float _phaseTimer;
    private float _segmentDuration;
    private Vector3 _segmentStart;
    private Quaternion _facingOffset = Quaternion.identity;

    /// <summary>Collects the waypoint children and starts the sitting phase.</summary>
    private void Start()
    {
        CollectWaypoints();
        _animator = GetComponent<Animator>();
        ResolveMover();
        if (_mover == null || _waypoints.Count < 2)
        {
            enabled = false;
            return;
        }

        // The rest pose was authored facing the viewer, so use the camera
        // direction as the reference "forward" of the model.
        Quaternion restRotation = _mover.rotation;
        Vector3 flatRestForward = Flatten(ResolvedRestForward());
        _facingOffset = Quaternion.Inverse(Quaternion.LookRotation(flatRestForward, Vector3.up)) * restRotation;

        EnterSitting();
    }

    /// <summary>Runs the sitting and flying state machine.</summary>
    private void Update()
    {
        _phaseTimer += Time.deltaTime;
        if (_phase == Phase.Sitting)
        {
            if (_phaseTimer >= _sittingDuration)
            {
                BeginFlight(SittingWaypointIndex + 1);
            }
            return;
        }

        float t = Mathf.Clamp01(_phaseTimer / _segmentDuration);
        Vector3 target = _waypoints[_targetIndex].position;
        Vector3 traveled = Vector3.Lerp(_segmentStart, target, Mathf.SmoothStep(0f, 1f, t));
        traveled += Vector3.up * (_bobAmplitude * Mathf.Sin(Mathf.PI * t));
        _mover.position = traveled;

        Vector3 direction = Flatten(target - _segmentStart);
        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(direction, Vector3.up) * _facingOffset;
            _mover.rotation = Quaternion.RotateTowards(_mover.rotation, desired, _turnSpeed * Time.deltaTime);
        }

        if (_phaseTimer >= _segmentDuration)
        {
            ArriveAtTarget();
        }
    }

    /// <summary>Finds the PositionN children and sorts them by numeric suffix.</summary>
    private void CollectWaypoints()
    {
        _waypoints.Clear();
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith(WaypointPrefix)) continue;
            string suffix = child.name.Substring(WaypointPrefix.Length);
            if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                _waypoints.Add(child);
                while (index >= _waypoints.Count)
                {
                    _waypoints.Add(null);
                }
                if (_waypoints[index] == null || _waypoints[index] == child)
                {
                    _waypoints[index] = child;
                }
            }
        }
    }

    private void ResolveMover()
    {
        if (_mover != null) return;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith(WaypointPrefix))
            {
                _mover = child;
                return;
            }
        }
    }

    private Vector3 ResolvedRestForward()
    {
        Camera mainCamera = Camera.main;
        Vector3 toCamera = mainCamera != null
            ? mainCamera.transform.position - _mover.position
            : _mover.forward;
        Vector3 flat = Flatten(toCamera);
        return flat.sqrMagnitude > 0.0001f ? flat : Flatten(_mover.forward);
    }

    private void BeginFlight(int waypointIndex)
    {
        _targetIndex = waypointIndex;
        _segmentStart = _mover.position;
        float distance = Vector3.Distance(_segmentStart, _waypoints[waypointIndex].position);
        _segmentDuration = Mathf.Max(MinSegmentDuration, distance / _flightSpeed);
        _phaseTimer = 0f;
        _phase = Phase.Flying;
        if (_animator != null) _animator.CrossFade(FlyingStateName, _flightCrossFade);
    }

    private void ArriveAtTarget()
    {
        _mover.position = _waypoints[_targetIndex].position;
        if (_targetIndex == SittingWaypointIndex)
        {
            EnterSitting();
            return;
        }
        BeginFlight((_targetIndex + 1) % _waypoints.Count);
    }

    private void EnterSitting()
    {
        _mover.position = _waypoints[SittingWaypointIndex].position;
        _phase = Phase.Sitting;
        _phaseTimer = 0f;
        if (_animator != null) _animator.CrossFade(SittingStateName, 0.4f);
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : value;
    }
}
