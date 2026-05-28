using UnityEngine;
using System.Collections;
using System;

/// <summary>
/// Component for an individual puzzle cylinder. 
/// Handles 90-degree rotations.
/// </summary>
public class BoardPuzzlePipe : MonoBehaviour
{
    public event Action OnRotated;

    [Header("Animation")]
    [SerializeField] private float _rotationDuration = 0.3f;
    [SerializeField] private Vector3 _rotationAxis = new Vector3(0, 0, 1);

    [Header("Audio")]
    [Tooltip("Sound played when the cylinder rotates.")]
    [SerializeField] private AudioClip _rotateSound;

    private bool _isRotating = false;
    private bool _isLocked = false;

    private void OnDisable()
    {
        // If the object is deactivated mid-rotation, reset the lock so it is not permanently blocked.
        _isRotating = false;
    }

    /// <summary>
    /// Permanently blocks rotation on this cylinder. Used when the puzzle is solved.
    /// </summary>
    public void Lock() => _isLocked = true;

    /// <summary>
    /// Returns whether the cylinder is locked.
    /// </summary>
    public bool IsLocked => _isLocked;

    /// <summary>
    /// Rotates the cylinder 90 degrees clockwise around the specified axis.
    /// Does nothing if the cylinder is already rotating or locked.
    /// </summary>
    public void Rotate()
    {
        if (_isRotating || _isLocked) return;

        if (_rotateSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(_rotateSound);
        }

        StartCoroutine(RotateRoutine());
    }

    private IEnumerator RotateRoutine()
    {
        _isRotating = true;

        Quaternion startRotation = transform.localRotation;
        Quaternion endRotation = startRotation * Quaternion.Euler(_rotationAxis * -90f);

        float elapsed = 0;
        while (elapsed < _rotationDuration)
        {
            elapsed += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(startRotation, endRotation, elapsed / _rotationDuration);
            yield return null;
        }

        transform.localRotation = endRotation;
        _isRotating = false;
        OnRotated?.Invoke();
    }
}
