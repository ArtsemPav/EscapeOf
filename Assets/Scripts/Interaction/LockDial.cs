using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Rotary combination dial. Delegates camera switching, cursor management,
/// and Esc handling to a sibling <see cref="PuzzleModeController"/>.
///
/// Flow:
///   1. Player presses E → PuzzleModeController activates the puzzle camera,
///      blocks FPS input, and frees the cursor.
///   2. While in puzzle mode:
///      • Hold LMB and move the mouse — the dial follows the cursor angle
///        relative to its screen-space centre.
///      • Esc — PuzzleModeController exits puzzle mode automatically.
///   3. Optionally checks a target step and fires OnUnlocked when reached.
///
/// Requires a <see cref="PuzzleModeController"/> on the same or a parent GameObject.
/// Implements <see cref="ISaveable"/> to persist state across sessions.
/// </summary>
public class LockDial : MonoBehaviour, IInteractable, ISaveable
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const float FullRevolutionDegrees = 360f;
    private const float SnapAngleThreshold    = 0.1f;
    private const float MinCursorDistanceSqr  = 1f;

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Rotation")]
    [Tooltip("Degrees per discrete step. 22.5° gives 16 positions per revolution.")]
    [SerializeField] private float _stepAngle = 22.5f;

    [Tooltip("Local axis to rotate around (e.g. Z for a front-facing dial).")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;

    [Tooltip("Speed of the smooth rotation tween in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Header("Unlock Condition")]
    [Tooltip("Fire OnUnlocked when the dial reaches Target Step.")]
    [SerializeField] private bool _checkTargetStep;

    [Tooltip("Step index (0-based) that unlocks the dial.")]
    [SerializeField] private int _targetStep;

    [Header("Interaction Text")]
    [SerializeField] private string _interactText         = "Осмотреть замок";
    [SerializeField] private string _unlockedInteractText = "Открыто";

    [Header("Events")]
    [Tooltip("Fired when the dial reaches the target step (requires Check Target Step).")]
    [SerializeField] private UnityEvent _onUnlocked;

    [Tooltip("Fired on every discrete step rotation.")]
    [SerializeField] private UnityEvent _onRotated;

    [Header("Save")]
    [Tooltip("Stable unique ID. Right-click → Generate Save ID to auto-fill.")]
    [SerializeField] private string _saveId;

    // ── State ──────────────────────────────────────────────────────────────────

    private int  _currentStep;
    private bool _isUnlocked;
    private int  _stepsPerRevolution;

    private Quaternion _targetRotation;

    private bool  _isDragging;
    private float _previousMouseAngle;
    private float _angleAccumulator;

    private Camera               _mainCamera;
    private PuzzleModeController _puzzleMode;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Current step index within a revolution (0 to StepsPerRevolution - 1).</summary>
    public int CurrentStep => _currentStep;

    /// <summary>Total number of discrete positions per full revolution.</summary>
    public int StepsPerRevolution => _stepsPerRevolution;

    /// <summary>True after the dial has been successfully unlocked.</summary>
    public bool IsUnlocked => _isUnlocked;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>Serializes current step and unlock state to JSON.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new SaveData
    {
        currentStep = _currentStep,
        isUnlocked  = _isUnlocked,
    });

    /// <summary>Restores state from JSON and snaps the transform without animation.</summary>
    public void LoadSaveData(string json)
    {
        var data     = JsonUtility.FromJson<SaveData>(json);
        _currentStep = data.currentStep;
        _isUnlocked  = data.isUnlocked;
        SnapToStep(_currentStep);
    }

    [Serializable]
    private struct SaveData
    {
        public int  currentStep;
        public bool isUnlocked;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _stepsPerRevolution = Mathf.RoundToInt(FullRevolutionDegrees / _stepAngle);
        _targetRotation     = transform.localRotation;
        _puzzleMode         = GetComponentInParent<PuzzleModeController>();
        _mainCamera         = Camera.main;

        if (_puzzleMode == null)
            Debug.LogWarning("[LockDial] PuzzleModeController not found in parent hierarchy.", this);

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        TweenRotation();

        if (_puzzleMode != null && _puzzleMode.IsActive && !_isUnlocked)
            HandleMouseInput();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Returns true when the dial is interactive and puzzle mode is not yet active.</summary>
    public bool CanInteract() => !_isUnlocked && (_puzzleMode == null || !_puzzleMode.IsActive);

    /// <summary>Enters puzzle mode on player interaction.</summary>
    public void Interact()
    {
        if (!CanInteract()) return;
        _puzzleMode?.EnterPuzzleMode();
    }

    public string GetInteractText()      => _isUnlocked ? _unlockedInteractText : _interactText;
    public bool IsPickable()             => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Input ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tracks the angle of the cursor relative to the dial's screen-space centre.
    /// Converts angular delta into discrete rotation steps while LMB is held.
    /// </summary>
    private void HandleMouseInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (!mouse.leftButton.isPressed)
        {
            ResetDragState();
            return;
        }

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        Vector2 screenCenter = _mainCamera.WorldToScreenPoint(transform.position);
        Vector2 toMouse      = mouse.position.ReadValue() - screenCenter;

        if (toMouse.sqrMagnitude < MinCursorDistanceSqr) return;

        float currentAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        if (!_isDragging)
        {
            _previousMouseAngle = currentAngle;
            _isDragging         = true;
            return;
        }

        float angleDelta    = Mathf.DeltaAngle(_previousMouseAngle, currentAngle);
        _previousMouseAngle = currentAngle;
        _angleAccumulator  += angleDelta;

        while (_angleAccumulator >= _stepAngle)
        {
            _angleAccumulator -= _stepAngle;
            ApplyStep(1);
        }

        while (_angleAccumulator <= -_stepAngle)
        {
            _angleAccumulator += _stepAngle;
            ApplyStep(-1);
        }
    }

    private void ResetDragState()
    {
        _isDragging       = false;
        _angleAccumulator = 0f;
    }

    // ── Rotation ───────────────────────────────────────────────────────────────

    /// <summary>Advances the dial by one discrete step. +1 = clockwise, -1 = counter-clockwise.</summary>
    private void ApplyStep(int direction)
    {
        _currentStep    = WrapStep(_currentStep + direction);
        _targetRotation = StepToRotation(_currentStep);

        _onRotated.Invoke();

        if (_checkTargetStep && _currentStep == _targetStep)
            Unlock();
    }

    private void TweenRotation()
    {
        if (Quaternion.Angle(transform.localRotation, _targetRotation) < SnapAngleThreshold)
        {
            transform.localRotation = _targetRotation;
            return;
        }

        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            _targetRotation,
            _rotationSpeed * Time.deltaTime
        );
    }

    /// <summary>Instantly snaps the transform to the given step without animation.</summary>
    private void SnapToStep(int step)
    {
        _targetRotation         = StepToRotation(step);
        transform.localRotation = _targetRotation;
    }

    private Quaternion StepToRotation(int step)
        => Quaternion.AngleAxis(step * _stepAngle, _rotationAxis.normalized);

    private int WrapStep(int step)
        => ((step % _stepsPerRevolution) + _stepsPerRevolution) % _stepsPerRevolution;

    // ── Unlock ─────────────────────────────────────────────────────────────────

    private void Unlock()
    {
        _isUnlocked = true;
        ResetDragState();
        _puzzleMode?.ExitPuzzleMode();
        _onUnlocked.Invoke();
        SaveManager.Instance?.Save();
    }

    // ── Editor Utilities ───────────────────────────────────────────────────────

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Snap to Current Step (Editor)")]
    private void EditorSnapToCurrentStep() => SnapToStep(WrapStep(_currentStep));
}
