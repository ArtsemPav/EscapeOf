using UnityEngine;

/// <summary>
/// Horror entity that rushes toward the player camera when activated.
/// Attach to a GameObject that is initially inactive.
/// A <see cref="HorrorEvent"/> with <c>AppearThenDisappearAfterDelay</c>
/// activates the GameObject; this script handles the flight in <see cref="OnEnable"/>.
/// The HorrorEvent deactivates the GameObject after its disappear delay.
/// </summary>
public class FlyAtPlayer : MonoBehaviour
{
    [Tooltip("Base flight speed (units per second). Speed increases over time for an accelerating rush.")]
    [SerializeField] private float _speed = 6f;

    [Tooltip("Maximum flight time before the script stops (seconds). Should be <= the HorrorEvent disappear delay.")]
    [SerializeField] private float _lifetime = 2f;

    [Tooltip("Distance from the camera at which the entity stops approaching (units).")]
    [SerializeField] private float _stopDistance = 0.3f;

    [Tooltip("Player camera. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    private Vector3 _startPos;
    private Vector3 _targetPos;
    private float _elapsed;

    private void OnEnable()
    {
        _elapsed = 0f;
        _startPos = transform.position;
        if (_playerCamera == null) _playerCamera = Camera.main;
        _targetPos = _playerCamera != null
            ? _playerCamera.transform.position
            : _startPos + transform.forward * 5f;
    }

    private void Update()
    {
        if (_playerCamera == null) return;

        _elapsed += Time.deltaTime;

        if (_elapsed >= _lifetime)
        {
            enabled = false;
            return;
        }

        float dist = Vector3.Distance(transform.position, _targetPos);
        if (dist <= _stopDistance)
        {
            enabled = false;
            return;
        }

        // Accelerating movement toward the camera.
        float p = _elapsed / _lifetime;
        float currentSpeed = _speed * (0.4f + p * 1.6f);
        transform.position = Vector3.MoveTowards(
            transform.position, _targetPos, currentSpeed * Time.deltaTime);

        // Always face the camera.
        transform.LookAt(_playerCamera.transform);
    }
}
