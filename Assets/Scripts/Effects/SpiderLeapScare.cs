using UnityEngine;

/// <summary>
/// Spider horror effect with three phases:
///   1. Leap — plays the leap animation and lunges toward the leap target
///      (a scene object, or the player's camera if none assigned).
///   2. Eat — plays the eating animation, standing still at the target.
///   3. Escape — turns toward the flee point (or away from the player) and
///      crawls there, then disappears.
/// Attach to a spider GameObject that is initially inactive.
/// A HorrorEvent activates the GameObject; this script handles all phases
/// and deactivates the GameObject when the escape ends.
/// </summary>
public class SpiderLeapScare : MonoBehaviour
{
    [Header("Leap")]
    [Tooltip("Name of the leap animation state in the Animator Controller.")]
    [SerializeField] private string _leapStateName = "rig|leap";

    [Tooltip("Leap duration before the escape phase starts (seconds).")]
    [SerializeField] private float _leapDuration = 0.6f;

    [Tooltip("Leap speed (units per second).")]
    [SerializeField] private float _leapSpeed = 7f;

    [Tooltip("Distance from the target at which the leap stops (units).")]
    [SerializeField] private float _leapStopDistance = 0.8f;

    [Tooltip("Player camera. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Если назначен — паук прыгает на этот объект вместо игрока. " +
             "Пусто = прежнее поведение (прыжок к камере игрока).")]
    [SerializeField] private Transform _leapTarget;

    [Header("Eat (at the target)")]
    [Tooltip("Name of the eating animation state (played at the leap target before fleeing).")]
    [SerializeField] private string _eatStateName = "rig|eating";

    [Tooltip("How long the spider eats before crawling to the flee point (seconds).")]
    [SerializeField] private float _eatDuration = 2f;

    [Header("Escape (to the flee point)")]
    [Tooltip("Name of the run/walk animation state in the Animator Controller.")]
    [SerializeField] private string _fleeStateName = "rig|walk";

    [Tooltip("Escape speed (units per second).")]
    [SerializeField] private float _fleeSpeed = 6f;

    [Tooltip("Escape duration before the spider disappears (seconds). Safety timeout.")]
    [SerializeField] private float _fleeLifetime = 1.5f;

    [Tooltip("Куда ползти после поедания. Назначь пустышку-маркер в сцене. " +
             "Пусто = прежнее поведение (убегание от игрока/цели прыжка).")]
    [SerializeField] private Transform _fleePoint;

    [Tooltip("Дистанция до точки, на которой паук считается прибывшим и исчезает.")]
    [SerializeField] private float _fleeArriveDistance = 0.3f;

    [Tooltip("Скорость доворота в сторону точки (градусов в секунду).")]
    [SerializeField] private float _turnSpeed = 540f;

    private const string PlayerTag = "Player";

    private enum Phase { Leap, Eat, Escape }

    private const float LeapStartTimeout = 0.5f; // fallback if the leap state never plays

    private Animator _animator;
    private Transform _player;
    private float   _phaseTimer;
    private bool    _leaping;   // movement unlocked once the animator entered the leap state
    private Phase   _phase;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        _phaseTimer = 0f;
        _phase      = Phase.Leap;

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
        Debug.Log($"[SpiderLeapScare '{name}'] Leap released after {elapsed:F2}s (timeout={elapsed >= LeapStartTimeout})", this);
    }

    private void Update()
    {
        if (!_leaping) return;

        switch (_phase)
        {
            case Phase.Leap:   LeapPhase();   break;
            case Phase.Eat:    EatPhase();    break;
            case Phase.Escape: EscapePhase(); break;
        }
    }

    /// <summary>Lunges toward the leap target (or the player's camera); switches to eating when close or out of time.</summary>
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
            if (_phaseTimer >= _leapDuration || dist <= _leapStopDistance + 0.05f)
            {
                StartEating();
                return;
            }
        }

        _phaseTimer += Time.deltaTime;
    }

    /// <summary>Spider lands at the target and eats for _eatDuration seconds.</summary>
    private void StartEating()
    {
        _phase      = Phase.Eat;
        _phaseTimer = 0f;
        Debug.Log($"[SpiderLeapScare '{name}'] StartEating at pos={transform.position}", this);
        if (_animator != null)
        {
            // Перезапуск компонента сбрасывает culling-кэш и гарантирует проигрыш eating.
            _animator.enabled = false;
            _animator.enabled = true;
            _animator.Play(_eatStateName, 0, 0f);
        }
    }

    private void EatPhase()
    {
        _phaseTimer += Time.deltaTime;
        if (_phaseTimer >= _eatDuration)
            StartEscape();
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

        _phaseTimer += Time.deltaTime;
        if (_phaseTimer >= _fleeLifetime)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>Switches from eating to the escape phase and swaps the animation.</summary>
    private void StartEscape()
    {
        _phase      = Phase.Escape;
        _phaseTimer = 0f;
        Debug.Log($"[SpiderLeapScare '{name}'] StartEscape at pos={transform.position}", this);
        if (_animator != null)
        {
            // Перезапуск компонента сбрасывает culling-кэш и гарантирует проигрыш walk.
            _animator.enabled = false;
            _animator.enabled = true;
            _animator.Play(_fleeStateName, 0, 0f);
        }
    }
}
