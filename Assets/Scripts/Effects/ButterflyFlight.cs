using UnityEngine;

/// <summary>
/// Butterfly behaviour with three states:
/// Idle — sits on the StayPoint, flying animation stopped.
/// Fly — player approached the StayPoint, butterfly follows the waypoint route.
/// Return — player left the StayPoint area, butterfly flies back to the StayPoint,
/// lands and switches back to Idle.
/// Wing flapping is handled by the prefab's built-in Animation component.
/// </summary>
[RequireComponent(typeof(Animation))]
public class ButterflyFlight : MonoBehaviour
{
    private enum ButterflyState
    {
        Idle,
        Fly,
        Return
    }

    [Header("Stay Point")]
    [Tooltip("Point where the butterfly sits in the Idle state. Uses the butterfly's own start position if left empty.")]
    [SerializeField] private Transform _stayPoint;

    [Tooltip("Distance at which the player approaching the StayPoint triggers the Fly state.")]
    [SerializeField] private float _playerApproachDistance = 2f;

    [Tooltip("Distance from the StayPoint beyond which the player is considered to have left (only checked in the Fly state).")]
    [SerializeField] private float _playerLeaveDistance = 3.5f;

    [Tooltip("Distance at which the butterfly is considered landed on the StayPoint.")]
    [SerializeField] private float _landThreshold = 0.1f;

    [Header("Route")]
    [Tooltip("Waypoints the butterfly follows in order. Loops back to the first when the last is reached.")]
    [SerializeField] private Transform[] _waypoints;

    [Tooltip("Distance at which a waypoint is considered reached and the next one is selected.")]
    [SerializeField] private float _waypointReachedThreshold = 0.5f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Radius of the sphere used to detect nearby colliders.")]
    [SerializeField] private float _avoidanceRadius = 0.8f;

    [Tooltip("How strongly the butterfly steers away from detected obstacles.")]
    [SerializeField] private float _avoidanceStrength = 5f;

    [Tooltip("Layer mask for obstacle colliders. Defaults to everything except Ignore Raycast.")]
    [SerializeField] private LayerMask _obstacleMask = ~0;

    [Header("Movement")]
    [Tooltip("Base flight speed in units per second.")]
    [SerializeField] private float _speed = 1.5f;

    [Tooltip("How quickly the butterfly turns toward its movement direction.")]
    [SerializeField] private float _turnSpeed = 3f;

    [Header("Flutter")]
    [Tooltip("Perlin noise scale — higher values produce more erratic fluttering.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float _flutterScale = 0.5f;

    [Tooltip("Amplitude of vertical bobbing added on top of route flight.")]
    [SerializeField] private float _bobAmplitude = 0.15f;

    [Tooltip("Maximum wing bank angle in degrees when turning.")]
    [SerializeField] private float _maxBankAngle = 15f;

    [Tooltip("Yaw offset in degrees to correct the model's forward direction. 0 = model faces +Z, 180 = model faces -Z, 90 = model faces +X, -90 = model faces -X.")]
    [SerializeField] private float _forwardOffset = 0f;

    [Header("Player")]
    [Tooltip("Player transform. Auto-detected via FPSController if left empty.")]
    [SerializeField] private Transform _player;

    private const string FLYING_CLIP_NAME = "Flying";
    private const string IDLE_CLIP_NAME = "Idle";
    private const float VELOCITY_LERP_RATE = 2f;
    private const float PERLIN_SPEED_SCALE = 0.3f;
    private const float SQR_VELOCITY_THRESHOLD = 0.01f;

    private ButterflyState _state = ButterflyState.Idle;
    private Vector3 _stayPosition;
    private Quaternion _stayRotation;
    private Vector3 _currentTarget;
    private Vector3 _velocity;
    private Vector3 _avoidanceForce;
    private float _perlinOffset;
    private float _initialRotationX;
    private float _currentYaw;
    private float _previousYaw;
    private Animation _animation;
    private int _currentWaypointIndex;

    private void Awake()
    {
        _perlinOffset = Random.Range(0f, 1000f);
        _animation = GetComponent<Animation>();
        _initialRotationX = transform.eulerAngles.x;
        _currentYaw = transform.eulerAngles.y + _forwardOffset;
        _previousYaw = _currentYaw;
    }

    private void Start()
    {
        if (_player == null)
        {
            FPSController playerController = FindFirstObjectByType<FPSController>();
            if (playerController != null)
                _player = playerController.transform;
        }

        if (_stayPoint == null)
        {
            _stayPosition = transform.position;
            _stayRotation = transform.rotation;
        }
        else
        {
            _stayPosition = _stayPoint.position;
            _stayRotation = _stayPoint.rotation;
        }

        // Start sitting on the stay point
        transform.position = _stayPosition;
        transform.rotation = _stayRotation;

        if (_waypoints == null || _waypoints.Length == 0)
        {
            Debug.LogWarning($"{nameof(ButterflyFlight)} on '{name}' has no waypoints assigned.", this);
            enabled = false;
            return;
        }

        SetState(ButterflyState.Idle);
    }

    private void Update()
    {
        switch (_state)
        {
            case ButterflyState.Idle:
                EvaluatePlayerApproach();
                break;

            case ButterflyState.Fly:
                EvaluatePlayerLeft();
                ComputeAvoidance();
                MoveTowardTarget();
                ApplyFlutter();
                break;

            case ButterflyState.Return:
                ComputeAvoidance();
                MoveTowardStayPoint();
                ApplyFlutter();
                break;
        }
    }

    /// <summary>
    /// Idle: watches the player distance to the StayPoint and takes off when the player comes close.
    /// </summary>
    private void EvaluatePlayerApproach()
    {
        if (_player == null)
            return;

        float distanceToPlayer = Vector3.Distance(_stayPosition, _player.position);

        if (distanceToPlayer < _playerApproachDistance)
            SetState(ButterflyState.Fly);
    }

    /// <summary>
    /// Fly: follows the route while the player stays near the StayPoint; switches to Return when the player leaves.
    /// </summary>
    private void EvaluatePlayerLeft()
    {
        if (_player == null)
            return;

        float distanceToPlayer = Vector3.Distance(_stayPosition, _player.position);

        if (distanceToPlayer > _playerLeaveDistance)
            SetState(ButterflyState.Return);
    }

    /// <summary>
    /// Applies the new state: stops or starts the flying animation and resets the route index when landing.
    /// </summary>
    private void SetState(ButterflyState newState)
    {
        _state = newState;

        switch (newState)
        {
            case ButterflyState.Idle:
                if (_animation != null && _animation.GetClip(IDLE_CLIP_NAME) != null)
                    _animation.Play(IDLE_CLIP_NAME);
                else
                    _animation?.Stop();
                break;

            case ButterflyState.Fly:
                if (_animation != null && _animation.GetClip(FLYING_CLIP_NAME) != null)
                    _animation.Play(FLYING_CLIP_NAME);
                else
                    _animation?.Play();

                _currentWaypointIndex = 0;
                PickNextWaypoint();
                break;

            case ButterflyState.Return:
                _currentWaypointIndex = 0;
                break;
        }
    }

    /// <summary>
    /// Detects nearby colliders via OverlapSphere and computes a steering force
    /// that pushes the butterfly away from the closest obstacle surface.
    /// </summary>
    private void ComputeAvoidance()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, _avoidanceRadius, _obstacleMask,
            QueryTriggerInteraction.Ignore);

        if (hits.Length == 0)
        {
            _avoidanceForce = Vector3.zero;
            return;
        }

        Vector3 strongestPush = Vector3.zero;
        float strongestWeight = 0f;

        foreach (Collider hit in hits)
        {
            Vector3 closestPoint = hit.ClosestPoint(transform.position);
            Vector3 toButterfly = transform.position - closestPoint;
            float distance = toButterfly.magnitude;

            if (distance < 0.001f)
            {
                toButterfly = Random.insideUnitSphere.normalized;
                distance = 0.01f;
            }

            float weight = 1f - (distance / _avoidanceRadius);
            weight = Mathf.Clamp01(weight);

            if (weight > strongestWeight)
            {
                strongestWeight = weight;
                strongestPush = toButterfly.normalized * weight;
            }
        }

        _avoidanceForce = strongestPush * _avoidanceStrength;
    }

    /// <summary>
    /// Moves the butterfly toward the current waypoint with smooth velocity and rotation.
    /// </summary>
    private void MoveTowardTarget()
    {
        Vector3 toTarget = _currentTarget - transform.position;

        if (toTarget.magnitude < _waypointReachedThreshold)
        {
            PickNextWaypoint();
            return;
        }

        Vector3 desiredVelocity = toTarget.normalized * _speed + _avoidanceForce;

        _velocity = Vector3.Lerp(_velocity, desiredVelocity, Time.deltaTime * _turnSpeed);

        transform.position += _velocity * Time.deltaTime;
        RotateTowardMovement(_velocity);
    }

    /// <summary>
    /// Moves the butterfly back to the StayPoint and lands when close enough.
    /// </summary>
    private void MoveTowardStayPoint()
    {
        Vector3 toStay = _stayPosition - transform.position;

        if (toStay.magnitude < _landThreshold)
        {
            _velocity = Vector3.zero;
            transform.position = _stayPosition;
            transform.rotation = _stayRotation;
            SetState(ButterflyState.Idle);
            return;
        }

        Vector3 desiredVelocity = toStay.normalized * _speed + _avoidanceForce;

        _velocity = Vector3.Lerp(_velocity, desiredVelocity, Time.deltaTime * _turnSpeed);

        transform.position += _velocity * Time.deltaTime;
        RotateTowardMovement(_velocity);
    }

    /// <summary>
    /// Smoothly turns the butterfly toward its movement direction and applies banking.
    /// </summary>
    private void RotateTowardMovement(Vector3 velocity)
    {
        if (velocity.sqrMagnitude <= SQR_VELOCITY_THRESHOLD)
            return;

        Vector3 flatDirection = velocity.normalized;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude <= SQR_VELOCITY_THRESHOLD)
            return;

        float targetYaw = Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;
        targetYaw += _forwardOffset;

        _currentYaw = Mathf.LerpAngle(_currentYaw, targetYaw, Time.deltaTime * VELOCITY_LERP_RATE);

        float yawDelta = Mathf.DeltaAngle(_previousYaw, _currentYaw);
        float bank = Mathf.Clamp01(Mathf.Abs(yawDelta) / 5f) * _maxBankAngle * Mathf.Sign(yawDelta);
        _previousYaw = _currentYaw;

        transform.rotation = Quaternion.Euler(_initialRotationX, _currentYaw, bank);
    }

    /// <summary>
    /// Applies Perlin-noise-based offset for organic fluttering on top of route flight.
    /// </summary>
    private void ApplyFlutter()
    {
        float t = Time.time * _flutterScale + _perlinOffset;
        float offsetX = Mathf.PerlinNoise(t, _perlinOffset) - 0.5f;
        float offsetY = (Mathf.PerlinNoise(_perlinOffset, t) - 0.5f) * _bobAmplitude;
        float offsetZ = Mathf.PerlinNoise(t * 0.7f, _perlinOffset + 50f) - 0.5f;

        Vector3 flutter = new Vector3(offsetX, offsetY, offsetZ);
        transform.position += flutter * Time.deltaTime * PERLIN_SPEED_SCALE;
    }

    /// <summary>
    /// Selects the next waypoint in the route, looping back to the first after the last.
    /// </summary>
    private void PickNextWaypoint()
    {
        if (_waypoints == null || _waypoints.Length == 0)
            return;

        _currentTarget = _waypoints[_currentWaypointIndex].position;
        _currentWaypointIndex = (_currentWaypointIndex + 1) % _waypoints.Length;
    }

    /// <summary>
    /// Draws the StayPoint, waypoint path and player approach range gizmos in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 stay = _stayPoint != null ? _stayPoint.position : transform.position;

        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.DrawSphere(stay, 0.1f);

        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(stay, _playerApproachDistance);

        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawWireSphere(stay, _playerLeaveDistance);

        if (_waypoints != null && _waypoints.Length > 0)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);

            for (int i = 0; i < _waypoints.Length; i++)
            {
                if (_waypoints[i] == null)
                    continue;

                Gizmos.DrawSphere(_waypoints[i].position, 0.1f);

                int next = (i + 1) % _waypoints.Length;
                if (_waypoints[next] != null)
                    Gizmos.DrawLine(_waypoints[i].position, _waypoints[next].position);
            }
        }
    }
}
