using UnityEngine;

/// <summary>
/// Animation states available in character.controller (Animator int parameter "State").
/// </summary>
public enum CharacterAnimationState
{
    LowCrawl    = 0, // Crawling on the ground
    ActionPose  = 1, // Action pose A
    ActionPose2 = 2, // Action pose B
    Sitting     = 3, // Sitting pose
    Run         = 4  // Running
}

/// <summary>
/// Moves the character from its current position toward a destination
/// playing the selected animation state.
/// The Animator is kept disabled until StartRun() is called, so AnyState
/// transitions in the controller cannot fire prematurely.
/// Wire StartRun() to HorrorEvent._onActivated.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterRunPast : MonoBehaviour
{
    private const string StateParam = "State";

    [Tooltip("Transform the character moves toward. Place it past the door.")]
    [SerializeField] private Transform _destination;

    [Tooltip("Movement speed in units per second.")]
    [SerializeField] private float _runSpeed = 4f;

    [Tooltip("Rotation lerp speed toward the destination.")]
    [SerializeField] private float _turnSpeed = 10f;

    [Tooltip("Animation state to play when the character activates.")]
    [SerializeField] private CharacterAnimationState _animationState = CharacterAnimationState.Run;

    private Animator _animator;
    private bool     _moving;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        // Set the parameter BEFORE disabling so it's ready when enabled later.
        _animator.SetInteger(StateParam, (int)_animationState);
        // Keep Animator off so AnyState transitions don't fire with the
        // default parameter value before StartRun() is called.
        _animator.enabled = false;
    }

    /// <summary>Activates the character, plays the selected animation, and starts moving toward the destination.</summary>
    public void StartRun()
    {
        if (_destination == null)
        {
            Debug.LogWarning("[CharacterRunPast] No destination assigned.", this);
            return;
        }

        // Parameter is already set in Awake. Enable Animator and force the state.
        _animator.enabled = true;
        _animator.Play(_animationState.ToString(), 0, 0f);

        _moving = true;
    }

    private void Update()
    {
        if (!_moving || _destination == null) return;

        // Ignore Y so the character doesn't float or sink
        Vector3 flatTarget = new Vector3(_destination.position.x, transform.position.y, _destination.position.z);
        Vector3 dir        = flatTarget - transform.position;

        if (dir.sqrMagnitude < 0.01f)
        {
            _moving = false;
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, flatTarget, _runSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(dir), _turnSpeed * Time.deltaTime);
    }
}
