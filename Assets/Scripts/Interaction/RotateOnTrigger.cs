using UnityEngine;

/// <summary>
/// Rotates the object by a specific angle when triggered (e.g., when a lock is solved).
/// </summary>
public class RotateOnTrigger : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("The axis to rotate around.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.up;

    [Tooltip("The target angle to rotate by (relative to current rotation).")]
    [SerializeField] private float _targetAngle = 90f;

    [Tooltip("How fast the rotation should be (degrees per second).")]
    [SerializeField] private float _rotationSpeed = 180f;

    private Quaternion _targetRotation;
    private bool _isRotating = false;

    private void Awake()
    {
        // Initialize target rotation to current rotation
        _targetRotation = transform.localRotation;
    }

    private void Update()
    {
        if (!_isRotating) return;

        // Smoothly rotate towards the target rotation
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation, 
            _targetRotation, 
            _rotationSpeed * Time.deltaTime
        );

        // Check if we reached the target
        if (Quaternion.Angle(transform.localRotation, _targetRotation) < 0.1f)
        {
            transform.localRotation = _targetRotation;
            _isRotating = false;
        }
    }

    /// <summary>
    /// Starts the rotation process.
    /// </summary>
    public void TriggerRotation()
    {
        _targetRotation = transform.localRotation * Quaternion.AngleAxis(_targetAngle, _rotationAxis);
        _isRotating = true;
    }
}
