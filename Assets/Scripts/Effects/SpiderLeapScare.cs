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

    [Tooltip("Если назначен — паук прыгает на этот объект вместо игрока. " +
             "Пусто = прежнее поведение (прыжок к камере игрока).")]
    [SerializeField] private Transform _leapTarget;

    [Header("Escape (away from the player)")]
    [Tooltip("Name of the run/walk animation state in the Animator Controller.")]
    [SerializeField] private string _fleeStateName = "rig|walk";

    [Tooltip("Escape speed (units per second).")]
    [SerializeField] private float _fleeSpeed = 6f;

    [Tooltip("Escape duration before the spider disappears (seconds).")]
    [SerializeField] private float _fleeLifetime = 1.5f;

    [Tooltip("Куда ползти после прыжка. Назначь пустышку-маркер в сцене. " +
             "Пусто = прежнее поведение (убегание от игрока/цели прыжка).")]
    [SerializeField] private Transform _fleePoint;

    [Tooltip("Дистанция до точки, на которой паук считается прибывшим и исчезает.")]
    [SerializeField] private float _fleeArriveDistance = 0.3f;

    [Tooltip("Скорость доворота в сторону точки (градусов в секунду).")]
    [SerializeField] private float _turnSpeed = 540f;

    private const string PlayerTag = "Player";

    private Animator _animator;
    private Transform _player;
    private float   _fleeTimer;
    private bool    _escaping;
    private bool    _leaping;

    private const float LeapStartTimeout = 0.5f; // fallback if the leap state never plays

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
            // Движение стартует только когда Animator реально применит позу leap,
            // иначе паук первые кадры "едет" в старой позе (движение обгоняет анимацию).
            _leaping = false;
            StartCoroutine(StartMoveWhenLeapPlays());
        }
        else
        {
            _leaping = true; // без анимации — двигаемся сразу
            Debug.LogWarning($"[SpiderLeapScare '{name}'] No Animator found in children — spider will move without animation.", this);
        }
    }

    /// <summary>Unblocks movement once the Animator has entered the leap state (usually 1 frame).</summary>
    private System.Collections.IEnumerator StartMoveWhenLeapPlays()
    {
        float elapsed = 0f;
        while (elapsed < LeapStartTimeout)
        {
            if (_animator.GetCurrentAnimatorStateInfo(0).IsName(_leapStateName))
            {
                // Ещё один кадр — чтобы поза гарантированно была сэмплирована на рендере.
                yield return null;
                break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        _leaping = true;
    }

    private void Update()
    {
        if (!_leaping) return;

        if (!_escaping)
        {
            LeapPhase();
        }
        else
        {
            EscapePhase();
        }
    }

    /// <summary>Lunges toward the leap target (or the player's camera); switches to the escape when close or out of time.</summary>
    private void LeapPhase()
    {
        // Цель прыжка: назначенный объект, иначе камера игрока (прежнее поведение).
        Transform target = _leapTarget != null
            ? _leapTarget
            : (_playerCamera != null ? _playerCamera.transform : null);

        if (target != null)
        {
            // Face the target while leaping (flat, so the spider stays upright).
            Vector3 lookTarget = target.position;
            lookTarget.y = transform.position.y;
            if ((lookTarget - transform.position).sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(lookTarget - transform.position);

            // Lunge toward a point just short of the target.
            Vector3 targetPos = target.position - transform.forward * _leapStopDistance;
            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, _leapSpeed * Time.deltaTime);

            float dist = Vector3.Distance(transform.position, target.position);
            if (_fleeTimer >= _leapDuration || dist <= _leapStopDistance + 0.05f)
            {
                StartEscape();
                return;
            }
        }

        _fleeTimer += Time.deltaTime;
    }

    /// <summary>Turns toward the flee point (or away from the player) and crawls until arrival or timeout.</summary>
    private void EscapePhase()
    {
        if (_fleePoint != null)
        {
            // Ползём к заданной точке: доворачиваемся и движемся по forward.
            Vector3 toPoint = _fleePoint.position - transform.position;
            toPoint.y = 0f;

            if (toPoint.sqrMagnitude <= _fleeArriveDistance * _fleeArriveDistance)
            {
                gameObject.SetActive(false); // добрался — исчез
                return;
            }

            if (toPoint.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPoint);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, _turnSpeed * Time.deltaTime);
            }
        }
        else
        {
            // Фолбэк: прежнее убегание от цели прыжка/игрока.
            Transform fleeFrom = _leapTarget != null ? _leapTarget : _player;

            if (fleeFrom != null)
            {
                Vector3 awayDir = transform.position - fleeFrom.position;
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(awayDir);
            }
        }

        // Move along the object's forward axis.
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
        {
            // CullUpdateTransforms: если рендерер вне камеры, Animator не применяет
            // смену состояния и паук бежит в застывшей позе leap. Перезапуск
            // компонента сбрасывает culling-кэш и гарантирует проигрыш walk.
            _animator.enabled = false;
            _animator.enabled = true;
            _animator.Play(_fleeStateName, 0, 0f);
        }
    }
}
