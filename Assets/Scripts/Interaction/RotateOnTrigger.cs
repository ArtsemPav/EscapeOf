using System;
using UnityEngine;

/// <summary>
/// Rotates the object by a specific angle when triggered (e.g., when a lock is solved).
/// Implements ISaveable to persist rotation state.
/// </summary>
public class RotateOnTrigger : MonoBehaviour, ISaveable
{
    [Header("Save Settings")]
    [SerializeField] private string _saveId;

    [Header("Rotation Settings")]
    [Tooltip("The axis to rotate around.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.up;

    [Tooltip("The target angle to rotate by (relative to current rotation).")]
    [SerializeField] private float _targetAngle = 90f;

    [Tooltip("How fast the rotation should be (degrees per second).")]
    [SerializeField] private float _rotationSpeed = 180f;

    private Quaternion _initialRotation;
    private Quaternion _targetRotation;
    private bool _isRotating = false;
    private bool _isTriggered = false;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isTriggered = _isTriggered });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isTriggered = data.isTriggered;

        if (_isTriggered)
        {
            // Snap instantly to the final rotation using the stored initial rotation as base
            _targetRotation         = _initialRotation * Quaternion.AngleAxis(_targetAngle, _rotationAxis);
            transform.localRotation = _targetRotation;
        }
    }

    [Serializable]
    private struct SaveData
    {
        public bool isTriggered;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        // Store the object's original rotation as the base for all future calculations
        _initialRotation = transform.localRotation;
        _targetRotation  = _initialRotation;
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
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
            
            // Save state once rotation is complete
            SaveManager.Instance?.Save();
        }
    }

    /// <summary>
    /// Starts the rotation process.
    /// </summary>
    public void TriggerRotation()
    {
        if (_isTriggered) return;

        _targetRotation = _initialRotation * Quaternion.AngleAxis(_targetAngle, _rotationAxis);
        _isRotating  = true;
        _isTriggered = true;
    }
}
