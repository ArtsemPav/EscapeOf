using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Direction of dial rotation.
/// </summary>
public enum RotationDirection { Clockwise, CounterClockwise }

/// <summary>
/// Represents one step in a combination lock sequence.
/// </summary>
[Serializable]
public struct ComboStep
{
    [Tooltip("Value from 0 to 99 (if step angle is 3.6).")]
    public int TargetValue;
    [Tooltip("Required direction of rotation to reach this value.")]
    public RotationDirection RequiredDirection;
}

/// <summary>
/// Rotary combination dial. Delegates camera switching, cursor management,
/// and Esc handling to a sibling <see cref="PuzzleModeController"/>.
/// </summary>
public class LockDial : MonoBehaviour, IInteractable, ISaveable
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const float FullRevolutionDegrees = 360f;
    private const float SnapAngleThreshold    = 0.1f;
    private const float MinCursorDistanceSqr  = 1f;

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Rotation")]
    [Tooltip("Degrees per discrete step. 3.6° gives 100 positions per revolution.")]
    [SerializeField] private float _stepAngle = 3.6f;

    [Tooltip("Local axis to rotate around (e.g. Z for a front-facing dial).")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;

    [Tooltip("Speed of the smooth rotation tween in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Header("Combination Settings")]
    [Tooltip("Sequence of 4 steps to unlock.")]
    [SerializeField] private ComboStep[] _combination = new ComboStep[4];

    [Header("Interaction Text")]
    [SerializeField] private string _interactText         = "Осмотреть замок";
    [SerializeField] private string _unlockedInteractText = "Открыто";

    [Header("Events")]
    [Tooltip("Fired when the entire combination is correctly entered.")]
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
    private float _sessionRotationDelta; // Суммарный поворот за текущий клик
    
    private int               _comboIndex; 
    private RotationDirection _lastDirection;

    private Camera               _mainCamera;
    private PuzzleModeController _puzzleMode;

    // ── Public API ─────────────────────────────────────────────────────────────

    public int CurrentStep => _currentStep;
    public int StepsPerRevolution => _stepsPerRevolution;
    public bool IsUnlocked => _isUnlocked;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() => JsonUtility.ToJson(new SaveData
    {
        currentStep = _currentStep,
        isUnlocked  = _isUnlocked,
    });

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

    public bool CanInteract() => !_isUnlocked && (_puzzleMode == null || !_puzzleMode.IsActive);

    public void Interact()
    {
        if (!CanInteract()) return;
        _puzzleMode?.EnterPuzzleMode();
    }

    public string GetInteractText()      => _isUnlocked ? _unlockedInteractText : _interactText;
    public bool IsPickable()             => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Input ──────────────────────────────────────────────────────────────────

    private void HandleMouseInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (!mouse.leftButton.isPressed)
        {
            if (_isDragging)
            {
                // Направление фиксируется в момент отпускания по суммарному смещению
                if (Mathf.Abs(_sessionRotationDelta) > 0.01f)
                {
                    _lastDirection = _sessionRotationDelta > 0 
                        ? RotationDirection.Clockwise 
                        : RotationDirection.CounterClockwise;
                }
                
                CheckCurrentComboStep();
                ResetDragState();
            }
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
            _previousMouseAngle   = currentAngle;
            _isDragging           = true;
            _sessionRotationDelta = 0f; // Начали новую сессию — "отсчет от 0"
            return;
        }

        float angleDelta       = Mathf.DeltaAngle(_previousMouseAngle, currentAngle);
        _previousMouseAngle    = currentAngle;
        _sessionRotationDelta += angleDelta; // Копим общее смещение

        _angleAccumulator += angleDelta;

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
        _isDragging           = false;
        _angleAccumulator     = 0f;
        _sessionRotationDelta = 0f;
    }

    // ── Combination Logic ──────────────────────────────────────────────────────

    private void CheckCurrentComboStep()
    {
        if (_combination == null || _combination.Length == 0) return;
        if (_comboIndex >= _combination.Length) return;

        ComboStep target = _combination[_comboIndex];

        // Логируем результат всего действия: число и результирующее направление
        string dirStr = _lastDirection == RotationDirection.Clockwise ? "Вправо (CW)" : "Влево (CCW)";
        Debug.Log($"[LockDial] {name}: Число {_currentStep}, Результирующее направление: {dirStr}");

        if (_currentStep == target.TargetValue && _lastDirection == target.RequiredDirection)
        {
            _comboIndex++;
            Debug.Log($"<color=green>[LockDial] Шаг {_comboIndex} из {_combination.Length} пройден!</color>");

            if (_comboIndex >= _combination.Length)
                Unlock();
        }
        else
        {
            _comboIndex = 0;
            Debug.Log("<color=red>[LockDial] Ошибка! Комбинация сброшена.</color>");
        }
    }

    // ── Rotation ───────────────────────────────────────────────────────────────

    private void ApplyStep(int direction)
    {
        _currentStep    = WrapStep(_currentStep + direction);
        _targetRotation = StepToRotation(_currentStep);
        _onRotated.Invoke();
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
