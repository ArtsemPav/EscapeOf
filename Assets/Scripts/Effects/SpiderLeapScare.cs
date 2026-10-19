using UnityEngine;

/// <summary>
/// Spider horror effect: the spider leaps at the player, then turns and runs away.
/// Attach to a spider GameObject that is initially inactive.
/// A <see cref="HorrorEvent"/> with <c>AppearAndStay</c> activates the GameObject;
/// this script handles the leap and the escape in <see cref="OnEnable"/> and
/// deactivates the GameObject when the escape ends (so no HorrorEvent delay is needed).
///
/// Phase 1 (leap): plays the leap animation, faces the player and lunges toward
/// the player's camera, stopping a short distance in front of it.
/// Phase 2 (escape): plays the walk animation, turns away from the player and
/// runs along its own forward axis until the flee duration elapses.
/// </summary>
public class SpiderLeapScare : MonoBehaviour
{
    [Header("Leap (toward the player)")]
    [Tooltip("Name of the leap animation state in the Animator Controller.")]
    [SerializeField] private string _leapStateName = "rig|leap";

    [Tooltip("Leap duration before the escape phase starts (seconds).")]
    [SerializeField] private float _leapDuration = 0.6f;

    [Tooltip("Leap speed (units per second).")]
    [SerializeField] private float _leapSpeed = 7f;

    [Tooltip("Distance from the camera at which the leap stops (units).")]
    [SerializeField] private float _leapStopDistance = 0.8f;

    [Tooltip("Player camera. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    [Header("Escape (away from the player)")]
    [Tooltip("Name of the run/walk animation state in the Animator Controller.")]
    [SerializeField] private string _fleeStateName = "rig|walk";

    [Tooltip("Escape speed (units per second).")]
    [SerializeField] private float _fleeSpeed = 6f;

    [Tooltip("Escape duration before the spider disappears (seconds).")]
    [SerializeField] private float _fleeLifetime = 1.5f;

    private const string PlayerTag = "Player";

    private Animator _animator;
    private Transform _player;
    private Camera  _cameraCache;
    private float   _fleeTimer;
    private bool    _escaping;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        _fleeTimer = 0f;
        _escaping  = false;

        if (_playerCamera == null) _playerCamera = Camera.main;
        if (_player == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag(PlayerTag);
            if (playerGO != null) _player = playerGO.transform;
        }

        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.Play(_leapStateName, 0, 0f);
        }
        else
        {
            Debug.LogWarning($"[SpiderLeapScare '{name}'] No Animator found in children — spider will move without animation.", this);
        }
    }

    private void Update()
    {
        if (!_escaping)
        {
            LeapPhase();
        }
        else
        {
            EscapePhase();
        }
    }

    /// <summary>Lunges toward the player's camera; switches to the escape when close or out of time.</summary>
    private void LeapPhase()
    {
        Camera cam = _playerCamera != null ? _playerCamera : (_cameraCache != null ? _cameraCache : Camera.main);
        if (cam != null) _cameraCache = cam;

        if (cam != null)
        {
            Transform camTransform = cam.transform;

            // Face the player while leaping (flat, so the spider stays upright).
            Vector3 lookTarget = camTransform.position;
            lookTarget.y = transform.position.y;
            if ((lookTarget - transform.position).sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(lookTarget - transform.position);

            // Lunge toward a point just short of the camera.
            Vector3 targetPos = camTransform.position - transform.forward * _leapStopDistance;
            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, _leapSpeed * Time.deltaTime);

            float dist = Vector3.Distance(transform.position, camTransform.position);
            if (_fleeTimer >= _leapDuration || dist <= _leapStopDistance)
            {
                StartEscape();
                return;
            }
        }

        _fleeTimer += Time.deltaTime;
    }

    /// <summary>Turns away from the player and runs along forward until the flee time elapses.</summary>
    private void EscapePhase()
    {
        if (_player != null)
        {
            Vector3 awayDir = transform.position - _player.position;
            awayDir.y = 0f;
            if (awayDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(awayDir);
        }

        // Move along the object's forward axis (now pointing away from the player).
        transform.position += transform.forward * (_fleeSpeed * Time.deltaTime);

        _fleeTimer += Time.deltaTime;
        if (_fleeTimer >= _fleeLifetime)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>Switches from the leap to the escape phase and swaps the animation.</summary>
    private void StartEscape()
    {
        _escaping  = true;
        _fleeTimer = 0f;
        if (_animator != null)
            _animator.Play(_fleeStateName, 0, 0f);
    }
}
