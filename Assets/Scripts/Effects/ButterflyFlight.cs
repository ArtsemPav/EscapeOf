using UnityEngine;

/// <summary>
/// Procedural butterfly flight along a waypoint route with player avoidance.
/// Wing flapping is handled by the prefab's built-in Animation component.
/// </summary>
[RequireComponent(typeof(Animation))]
public class ButterflyFlight : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Waypoints the butterfly follows in order. Loops back to the first when the last is reached.")]
    [SerializeField] private Transform[] _waypoints;

    [Tooltip("Distance at which a waypoint is considered reached and the next one is selected.")]
    [SerializeField] private float _waypointReachedThreshold = 0.5f;

    [Header("Movement")]
    [Tooltip("Base flight speed in units per second.")]
    [SerializeField] private float _speed = 1.5f;

    [Tooltip("How quickly the butterfly turns toward its movement direction.")]
    [SerializeField] private float _turnSpeed = 3f;

    [Header("Flutter")]
    [Tooltip("Perlin noise scale — higher values produce more erratic fluttering.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float _flutterScale = 0.5f;

    [Tooltip("Amplitude of vertical bobbing added on top of route movement.")]
    [SerializeField] private float _bobAmplitude = 0.15f;

    [Header("Player Avoidance")]
    [Tooltip("Player transform to flee from. Auto-detected via FPSController if left empty.")]
    [SerializeField] private Transform _player;

    [Tooltip("Distance at which the butterfly starts fleeing from the player.")]
    [SerializeField] private float _fleeDistance = 2.5f;

    [Tooltip("Speed multiplier applied while fleeing.")]
    [SerializeField] private float _fleeSpeedMultiplier = 2.5f;

    [Tooltip("Extra distance added beyond flee range when picking a flee destination.")]
    [SerializeField] private float _fleeOvershoot = 1f;

    private int _currentWaypointIndex;
    private Vector3 _currentTarget;
    private Vector3 _velocity;
    private float _perlinOffset;
    private bool _isFleeing;

    private const float VELOCITY_LERP_RATE = 2f;
    private const float PERLIN_SPEED_SCALE = 0.3f;
    private const float FLEE_DEST_MIN_DISTANCE = 0.5f;
    private const float SQR_VELOCITY_THRESHOLD = 0.01f;

    private void Awake()
    {
        _perlinOffset = Random.Range(0f, 1000f);
    }

    private void Start()
    {
        if (_player == null)
        {
            FPSController playerController = FindFirstObjectByType<FPSController>();
            if (playerController != null)
                _player = playerController.transform;
        }

        if (_waypoints == null || _waypoints.Length == 0)
        {
            Debug.LogWarning($"{nameof(ButterflyFlight)} on '{name}' has no waypoints assigned.", this);
            enabled = false;
            return;
        }

        PickNextWaypoint();
    }

    private void Update()
    {
        EvaluateFleeState();
        MoveTowardTarget();
        ApplyFlutter();
    }

    /// <summary>
    /// Checks whether the player is within flee range and switches target accordingly.
    /// </summary>
    private void EvaluateFleeState()
    {
        if (_player == null)
        {
            _isFleeing = false;
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, _player.position);

        if (distanceToPlayer < _fleeDistance)
        {
            if (!_isFleeing)
            {
                _isFleeing = true;
                PickFleeDestination();
            }
        }
        else if (_isFleeing && distanceToPlayer > _fleeDistance * 1.5f)
        {
            _isFleeing = false;
            PickNextWaypoint();
        }
    }

    /// <summary>
    /// Moves the butterfly toward the current target with smooth velocity and rotation.
    /// </summary>
    private void MoveTowardTarget()
    {
        Vector3 toTarget = _currentTarget - transform.position;
        float currentSpeed = _isFleeing ? _speed * _fleeSpeedMultiplier : _speed;

        if (!_isFleeing && toTarget.magnitude < _waypointReachedThreshold)
        {
            PickNextWaypoint();
            return;
        }

        Vector3 desiredVelocity = toTarget.normalized * currentSpeed;
        _velocity = Vector3.Lerp(_velocity, desiredVelocity, Time.deltaTime * _turnSpeed);

        transform.position += _velocity * Time.deltaTime;

        if (_velocity.sqrMagnitude > SQR_VELOCITY_THRESHOLD)
        {
            Quaternion targetRotation = Quaternion.LookRotation(_velocity.normalized);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation, Time.deltaTime * VELOCITY_LERP_RATE);
        }
    }

    /// <summary>
    /// Applies Perlin-noise-based offset for organic fluttering on top of route movement.
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
    /// Picks a destination point away from the player within flee range plus overshoot.
    /// </summary>
    private void PickFleeDestination()
    {
        Vector3 awayFromPlayer = (transform.position - _player.position).normalized;

        if (awayFromPlayer == Vector3.zero)
            awayFromPlayer = Random.insideUnitSphere.normalized;

        _currentTarget = transform.position + awayFromPlayer * (_fleeDistance + _fleeOvershoot);
    }

    /// <summary>
    /// Draws waypoint path and flee range gizmos in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
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

        if (_player != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(_player.position, _fleeDistance);
        }
    }
}
