using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The butterfly sits on the lamp (Position0) until the mouse comes near — then
/// it startles and flies to a random waypoint (Position1..N). While the mouse
/// keeps chasing it (stays close during flight), it keeps jumping to another
/// random waypoint on arrival; once the mouse falls behind, it flies back to
/// the lamp and sits again. Attach to the Animator root (the wrapper that
/// holds the PositionN objects).
/// </summary>
public class ButterflyPatrol : MonoBehaviour
{
    private const string WaypointPrefix = "Position";
    private const string SittingStateName = "Idle";
    private const string FlyingStateName = "Flying";
    private const string LeftWingName = "left_wing";
    private const string RightWingName = "right_wing";
    private const int SittingWaypointIndex = 0;
    private const float MinSegmentDuration = 0.6f;

    [Tooltip("Transform that physically travels between waypoints (the FBX model root). Defaults to the first non-waypoint child.")]
    [SerializeField] private Transform _mover;

    [Tooltip("Mouse distance that startles the sitting butterfly, as a fraction of screen height (0.08 = 8%).")]
    [SerializeField] private float _startleRadius = 0.08f;

    [Tooltip("Mouse distance that counts as chasing during flight, as a fraction of screen height.")]
    [SerializeField] private float _chaseRadius = 0.14f;

    [Tooltip("Crossfade time when entering the flight state. Keep tiny so wings flap at full speed the moment flight starts.")]
    [SerializeField] private float _flightCrossFade = 0.05f;

    [Tooltip("Crossfade time when landing back into the sitting state.")]
    [SerializeField] private float _sittingCrossFade = 0.4f;

    [Tooltip("Flight speed in world units per second.")]
    [SerializeField] private float _flightSpeed = 1.6f;

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
    private readonly List<int> _flightWaypointIndices = new List<int>();
    private Phase _phase = Phase.Sitting;
    private int _targetIndex;
    private float _phaseTimer;
    private float _segmentDuration;
    private Vector3 _segmentStart;
    private Quaternion _facingOffset = Quaternion.identity;
    private bool _facingOffsetValid;
    private bool _chased;
    private Camera _mainCamera;

    /// <summary>Collects the waypoint children and starts the sitting phase.</summary>
    private void Start()
    {
        CollectWaypoints();
        _animator = GetComponent<Animator>();
        ResolveMover();
        _mainCamera = Camera.main;
        if (_mover == null || _waypoints.Count < 2 || _flightWaypointIndices.Count == 0)
        {
            enabled = false;
            return;
        }

        // The rest pose was authored facing the viewer, but the model's local
        // axes point sideways — derive the true nose direction from the wing
        // axes (wings spread perpendicular to the body), with the sign chosen
        // toward the camera, and bake it into a fixed rotation offset.
        Vector3 restPosition = _mover.position;
        Vector3 wingSpan = ResolveWingSpanAxis();
        Vector3 restForward = Vector3.Cross(wingSpan, Vector3.up);
        Vector3 toCamera = _mainCamera != null ? _mainCamera.transform.position - restPosition : Vector3.forward;
        if (Vector3.Dot(restForward, Flatten(toCamera)) < 0f) restForward = -restForward;
        if (restForward.sqrMagnitude < 0.0001f) restForward = Flatten(_mover.forward);

        _facingOffset = Quaternion.Inverse(Quaternion.LookRotation(Flatten(restForward), Vector3.up)) * _mover.rotation;
        _facingOffsetValid = true;

        EnterSitting();
    }

    /// <summary>
    /// Returns the world-space axis along which the wings spread, derived from
    /// the left/right wing roots. Falls back to the mover's own sideways axis.
    /// </summary>
    private Vector3 ResolveWingSpanAxis()
    {
        Transform left = FindDescendant(transform, LeftWingName);
        Transform right = FindDescendant(transform, RightWingName);
        if (left != null && right != null)
        {
            Vector3 span = right.position - left.position;
            Vector3 flat = Flatten(span);
            if (flat.sqrMagnitude > 0.0001f) return flat;
        }
        return Flatten(_mover.right);
    }

    private static Transform FindDescendant(Transform root, string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName) return child;
            Transform found = FindDescendant(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Runs the sitting and flying state machine.</summary>
    private void Update()
    {
        _phaseTimer += Time.deltaTime;
        if (_phase == Phase.Sitting)
        {
            if (IsMouseNear(_mover.position, _startleRadius))
            {
                _chased = false;
                BeginFlight(PickRandomFlightWaypoint(_targetIndex));
            }
            return;
        }

        // Chase check: the mouse staying close during flight keeps the butterfly fleeing.
        if (!_chased && IsMouseNear(_mover.position, _chaseRadius))
        {
            _chased = true;
        }

        float t = Mathf.Clamp01(_phaseTimer / _segmentDuration);
        Vector3 target = _waypoints[_targetIndex].position;
        Vector3 traveled = Vector3.Lerp(_segmentStart, target, Mathf.SmoothStep(0f, 1f, t));
        traveled += Vector3.up * (_bobAmplitude * Mathf.Sin(Mathf.PI * t) + _bobAmplitude * Mathf.Sin(_phaseTimer * Mathf.PI * 2f * _bobFrequency) * 0.5f);
        _mover.position = traveled;

        Vector3 direction = Flatten(target - _segmentStart);
        if (_facingOffsetValid && direction.sqrMagnitude > 0.0001f)
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
        _flightWaypointIndices.Clear();
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

        // Waypoints other than the sitting spot are the flight destinations.
        for (int i = 1; i < _waypoints.Count; i++)
        {
            if (_waypoints[i] != null) _flightWaypointIndices.Add(i);
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
        Camera mainCamera = _mainCamera != null ? _mainCamera : Camera.main;
        Vector3 toCamera = mainCamera != null
            ? mainCamera.transform.position - _mover.position
            : _mover.forward;
        Vector3 flat = Flatten(toCamera);
        return flat.sqrMagnitude > 0.0001f ? flat : Flatten(_mover.forward);
    }    /// <summary>Checks the mouse against the butterfly in screen space (fractions of screen height).</summary>
    private bool IsMouseNear(Vector3 anchor, float radius)
    {
        Camera camera = _mainCamera != null ? _mainCamera : Camera.main;
        if (camera == null) return false;

        Vector3 anchorScreen = camera.WorldToScreenPoint(anchor);
        if (anchorScreen.z <= 0f) return false; // behind the camera

        if (!TryGetMouseScreenPosition(out Vector3 mousePosition)) return false;

        // Normalize by screen height so the radius feels the same on any resolution.
        Vector2 screenDelta = ((Vector2)mousePosition - (Vector2)anchorScreen) / Screen.height;
        return screenDelta.sqrMagnitude <= radius * radius;
    }

    private static bool TryGetMouseScreenPosition(out Vector3 position)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            position = mouse.position.ReadValue();
            return true;
        }
        position = Input.mousePosition;
        return true;
    }

    /// <summary>Picks a random flight waypoint different from the given current one.</summary>
    private int PickRandomFlightWaypoint(int current)
    {
        if (_flightWaypointIndices.Count == 1) return _flightWaypointIndices[0];
        int pick = _flightWaypointIndices[Random.Range(0, _flightWaypointIndices.Count)];
        while (pick == current)
        {
            pick = _flightWaypointIndices[Random.Range(0, _flightWaypointIndices.Count)];
        }
        return pick;
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

        if (_chased)
        {
            // The mouse kept up: jump to another random waypoint immediately.
            _chased = false;
            BeginFlight(PickRandomFlightWaypoint(_targetIndex));
            return;
        }

        // Nobody chased the butterfly — go back to the lamp.
        BeginFlight(SittingWaypointIndex);
    }

    private void EnterSitting()
    {
        _mover.position = _waypoints[SittingWaypointIndex].position;
        _phase = Phase.Sitting;
        _phaseTimer = 0f;
        _chased = false;
        if (_animator != null) _animator.CrossFade(SittingStateName, _sittingCrossFade);
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : value;
    }
}
