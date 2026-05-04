using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls a single mechanical cylinder in a combination lock.
/// Rotates -36 degrees per click.
/// </summary>
public class LockCylinder : MonoBehaviour
{
    private const float StepAngle = -36f;
    private const float RotationSpeed = 360f;

    [SerializeField] private int _currentIndex = 0;
    [SerializeField] private Vector3 _rotationAxis = Vector3.up;

    private Quaternion _targetRotation;

    public int CurrentValue => _currentIndex;

    private void Awake()
    {
        _targetRotation = transform.localRotation;
        UpdateRotation(true);
    }

    private void Update()
    {
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation, 
            _targetRotation, 
            RotationSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// Rotates the cylinder by one step.
    /// </summary>
    /// <param name="clockwise">If true, rotates 36 degrees. If false, rotates -36 degrees.</param>
    public void Rotate(bool clockwise)
    {
        if (clockwise)
        {
            _currentIndex = (_currentIndex - 1 + 10) % 10;
        }
        else
        {
            _currentIndex = (_currentIndex + 1) % 10;
        }
        UpdateRotation(false);
    }

    private void UpdateRotation(bool immediate)
    {
        _targetRotation = Quaternion.AngleAxis(_currentIndex * StepAngle, _rotationAxis);
        if (immediate)
        {
            transform.localRotation = _targetRotation;
        }
    }

    /// <summary>
    /// For restoring state from save system.
    /// </summary>
    public void SetValue(int value)
    {
        _currentIndex = Mathf.Clamp(value, 0, 9);
        UpdateRotation(true);
    }
}
