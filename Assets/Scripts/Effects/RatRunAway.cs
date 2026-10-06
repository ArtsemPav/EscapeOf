using UnityEngine;

/// <summary>
/// Rat horror effect: the rat runs in a straight line and disappears.
/// Attach to the rat GameObject that is initially inactive.
/// A <see cref="HorrorEvent"/> with <c>AppearAndStay</c> activates the GameObject;
/// this script handles the run in <see cref="OnEnable"/> and deactivates the
/// GameObject when the run ends (so no HorrorEvent delay is needed).
///
/// The run direction is the object's own forward axis (transform.forward) —
/// rotate the rat in the editor to aim its escape route.
/// </summary>
public class RatRunAway : MonoBehaviour
{
    [Header("Run")]
    [Tooltip("Run speed (units per second).")]
    [SerializeField] private float _speed = 5f;

    [Tooltip("Run duration before the rat disappears (seconds).")]
    [SerializeField] private float _lifetime = 1.5f;

    private const string RunStateName = "Run";

    private Animator _animator;
    private float _elapsed;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        _elapsed = 0f;
        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.Play(RunStateName, 0, 0f);
        }
        else
        {
            Debug.LogWarning($"[RatRunAway '{name}'] No Animator found in children — rat will run without animation.", this);
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (_elapsed >= _lifetime)
        {
            gameObject.SetActive(false);
            return;
        }

        // Move along the object's forward axis — rotate in the editor to aim.
        transform.position += transform.forward * (_speed * Time.deltaTime);
    }
}
