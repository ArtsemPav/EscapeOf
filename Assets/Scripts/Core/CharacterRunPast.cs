using UnityEngine;

/// <summary>
/// Moves the character from its current position toward a destination
/// using the Run animation state (Animator int parameter "State" = 4).
/// Wire StartRun() to HorrorEvent._onActivated.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterRunPast : MonoBehaviour
{
    private const string StateParam = "State";
    private const int    RunState   = 4;

    [Tooltip("Transform the character runs toward. Place it past the door.")]
    [SerializeField] private Transform _destination;

    [Tooltip("Movement speed in units per second.")]
    [SerializeField] private float _runSpeed = 4f;

    [Tooltip("Rotation lerp speed toward the destination.")]
    [SerializeField] private float _turnSpeed = 10f;

    private Animator _animator;
    private bool     _running;

    private void Awake() => _animator = GetComponent<Animator>();

    /// <summary>Starts the character running toward the destination. Wire to HorrorEvent._onActivated.</summary>
    public void StartRun()
    {
        if (_destination == null)
        {
            Debug.LogWarning("[CharacterRunPast] No destination assigned.", this);
            return;
        }
        _animator.SetInteger(StateParam, RunState);
        _running = true;
    }

    private void Update()
    {
        if (!_running || _destination == null) return;

        // Ignore Y so the character doesn't float or sink
        Vector3 flatTarget = new Vector3(_destination.position.x, transform.position.y, _destination.position.z);
        Vector3 dir        = flatTarget - transform.position;

        if (dir.sqrMagnitude < 0.01f)
        {
            _running = false;
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, flatTarget, _runSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(dir), _turnSpeed * Time.deltaTime);
    }
}
