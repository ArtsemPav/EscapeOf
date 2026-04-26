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

    private bool _isRotating = false;

    /// <summary>
    /// Rotates the cylinder 90 degrees clockwise around the specified axis.
    /// </summary>
    public void Rotate()
    {
        if (_isRotating) return;
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
