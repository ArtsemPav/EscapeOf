using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Rotary dial on a Lock object. Delegates camera switching, cursor management,
/// and Esc handling to a sibling <see cref="PuzzleModeController"/>.
///
/// Flow:
///   1. Player presses E while looking at the Lock — PuzzleModeController activates
///      the puzzle camera, blocks FPS input, and frees the cursor.
///   2. While in puzzle mode:
///      • LMB  — rotate the dial one step counter-clockwise.
///      • RMB  — rotate the dial one step clockwise.
///      • Esc  — PuzzleModeController exits puzzle mode automatically.
///   3. Optionally checks a target step and fires _onUnlocked when reached.
///
/// Requires a sibling <see cref="PuzzleModeController"/> on the same or parent GameObject.
/// Implements ISaveable to persist current step and unlock state across sessions.
/// </summary>
public class LockDial : MonoBehaviour, IInteractable, ISaveable
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Rotation Settings")]
    [Tooltip("Degrees per discrete step. 22.5° gives 16 positions per revolution.")]
    [SerializeField] private float _stepAngle = 22.5f;

    [Tooltip("Local axis to rotate around (e.g. Y for a front-facing combination dial).")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.up;

    [Tooltip("Speed of the smooth rotation animation in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Tooltip("How many pixels of horizontal mouse drag equal one rotation step.")]
    [SerializeField] private float _pixelsPerStep = 30f;

    [Header("Unlock Condition (optional)")]
    [Tooltip("Enable to fire OnUnlocked when the dial reaches the Target Step.")]
    [SerializeField] private bool _checkTargetStep = false;

    [Tooltip("Step index (0-based) that unlocks the dial. Total steps = 360 / Step Angle.")]
    [SerializeField] private int _targetStep = 0;

    [Header("Interaction Text")]
    [SerializeField] private string _interactText = "Осмотреть замок";
    [SerializeField] private string _unlockedInteractText = "Открыто";

    [Header("Events")]
    [Tooltip("Fired when the dial reaches the target step (requires Check Target Step).")]
    [SerializeField] private UnityEvent _onUnlocked;

    [Tooltip("Fired on every step rotation.")]
    [SerializeField] private UnityEvent _onRotated;

    [Header("Save")]
    [Tooltip("Stable unique ID. Right-click → Generate Save ID to auto-fill.")]
    [SerializeField] private string _saveId;

    // ── State ──────────────────────────────────────────────────────────────────

    private int _currentStep;
    private bool _isUnlocked;

    private Quaternion _targetRotation;
    private bool _isAnimating;

    private int _stepsPerRevolution;

    /// <summary>Angle (degrees) of the mouse relative to dial center on the previous frame while LMB was held.</summary>
    private float _previousMouseAngle;
    private bool _isDragging;
    private float _angleAccumulator;

    private Camera _mainCamera;

    private PuzzleModeController _puzzleMode;

    // ── Public Properties ──────────────────────────────────────────────────────

    /// <summary>Current step index within a revolution (0 to StepsPerRevolution - 1).</summary>
    public int CurrentStep => _currentStep;

    /// <summary>Number of discrete positions per full revolution.</summary>
    public int StepsPerRevolution => _stepsPerRevolution;

    /// <summary>True if the dial has been successfully unlocked.</summary>
    public bool IsUnlocked => _isUnlocked;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>Serializes current step and unlock state.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new LockDialSaveData
    {
        currentStep = _currentStep,
        isUnlocked  = _isUnlocked,
    });

    /// <summary>Restores state and snaps the transform without animation.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<LockDialSaveData>(json);
        _currentStep = data.currentStep;
        _isUnlocked  = data.isUnlocked;
        ApplyRotationSnap();
    }

    [Serializable]
    private struct LockDialSaveData
    {
        public int  currentStep;
        public bool isUnlocked;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _stepsPerRevolution = Mathf.RoundToInt(360f / _stepAngle);
        _targetRotation     = transform.localRotation;

        _puzzleMode  = GetComponentInParent<PuzzleModeController>();
        _mainCamera  = Camera.main;

        if (_puzzleMode == null)
            Debug.LogWarning("[LockDial] No PuzzleModeController found in parent hierarchy.", this);

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_isAnimating)
            AnimateRotation();

        if (_puzzleMode != null && _puzzleMode.IsActive && !_isUnlocked)
            HandleDialInput();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => !_isUnlocked && (_puzzleMode == null || !_puzzleMode.IsActive);

    /// <summary>Enters puzzle mode via PuzzleModeController on player interaction.</summary>
    public void Interact()
    {
        if (_isUnlocked || (_puzzleMode != null && _puzzleMode.IsActive)) return;
        _puzzleMode?.EnterPuzzleMode();
    }

    public string GetInteractText() => _isUnlocked ? _unlockedInteractText : _interactText;

    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Input Handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// While LMB is held, computes the angle of the mouse cursor relative to the
    /// dial's screen-space centre and converts the angular delta into discrete steps.
    /// Clockwise mouse movement → positive step; counter-clockwise → negative step.
    /// </summary>
    private void HandleDialInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (!mouse.leftButton.isPressed)
        {
            _isDragging = false;
            return;
        }

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        Vector2 screenCenter = _mainCamera.WorldToScreenPoint(transform.position);
        Vector2 mousePos     = mouse.position.ReadValue();
        Vector2 toMouse      = mousePos - screenCenter;

        if (toMouse.sqrMagnitude < 1f) return;

        float currentAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        if (!_isDragging)
        {
            _previousMouseAngle = currentAngle;
            _isDragging         = true;
            return;
        }

        float delta = Mathf.DeltaAngle(_previousMouseAngle, currentAngle);
        _previousMouseAngle = currentAngle;

        // Accumulate angle delta and fire steps when threshold is crossed.
        // Positive delta = counter-clockwise in screen space = we map to -1 step.
        // Negative delta = clockwise in screen space = we map to +1 step.
        _angleAccumulator += delta;

        while (_angleAccumulator >= _stepAngle)
        {
            _angleAccumulator -= _stepAngle;
            RotateStep(1);
        }

        while (_angleAccumulator <= -_stepAngle)
        {
            _angleAccumulator += _stepAngle;
            RotateStep(-1);
        }
    }

    // ── Rotation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the dial by one discrete step.
    /// +1 = clockwise, -1 = counter-clockwise.
    /// </summary>
    private void RotateStep(int direction)
    {
        _currentStep = ((_currentStep + direction) % _stepsPerRevolution + _stepsPerRevolution) % _stepsPerRevolution;

        float totalAngle = _currentStep * _stepAngle;
        _targetRotation  = Quaternion.AngleAxis(totalAngle, _rotationAxis.normalized);
        _isAnimating     = true;

        _onRotated.Invoke();

        if (_checkTargetStep && _currentStep == _targetStep)
            Unlock();
    }

    private void AnimateRotation()
    {
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            _targetRotation,
            _rotationSpeed * Time.deltaTime
        );

        if (Quaternion.Angle(transform.localRotation, _targetRotation) < 0.1f)
        {
            transform.localRotation = _targetRotation;
            _isAnimating = false;
        }
    }

    private void ApplyRotationSnap()
    {
        float totalAngle        = _currentStep * _stepAngle;
        transform.localRotation = Quaternion.AngleAxis(totalAngle, _rotationAxis.normalized);
        _targetRotation         = transform.localRotation;
    }

    // ── Unlock ─────────────────────────────────────────────────────────────────

    private void Unlock()
    {
        _isUnlocked       = true;
        _angleAccumulator = 0f;
        _isDragging       = false;
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
    private void SnapToCurrentStep()
    {
        int steps    = Mathf.RoundToInt(360f / _stepAngle);
        int safeStep = ((_currentStep % steps) + steps) % steps;
        transform.localRotation = Quaternion.AngleAxis(safeStep * _stepAngle, _rotationAxis.normalized);
    }
}
