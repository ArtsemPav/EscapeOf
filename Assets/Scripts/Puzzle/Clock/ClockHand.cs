using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Draggable clock hand. Handles discrete rotation, audio feedback, save/load, and UI hints.
/// Drag mechanic mirrors LockDial. Step count is determined by <see cref="ClockHandType"/>.
///
/// Input is gated by a raycast so that only the hand under the cursor responds —
/// the other hand ignores input even though both are active in puzzle mode.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ClockHand : MonoBehaviour, ISaveable
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const float FullRevolutionDegrees  = 360f;
    private const float SnapAngleThreshold     = 0.1f;
    private const float MinCursorDistanceSqr   = 1f;
    private const float MinDragDeltaThreshold  = 0.05f;

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Hand Settings")]
    [SerializeField] private ClockHandType _handType = ClockHandType.Minute;

    [Header("Rotation")]
    [Tooltip("Local axis to rotate around.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;

    [Tooltip("Inverts the mapping between mouse movement and hand rotation direction. Enable if dragging right moves the hand left.")]
    [SerializeField] private bool _invertRotationDirection = false;

    [Tooltip("Speed of the smooth rotation in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Tooltip("Maximum rotation speed from user input in degrees per second.")]
    [SerializeField] private float _maxInputRotationSpeed = 540f;

    [Header("Rotation Constraint")]
    [Tooltip("When enabled, counter-clockwise movement is ignored. The player must complete a full revolution to correct a mistake.")]
    [SerializeField] private bool _clockwiseOnly = false;

    [Tooltip("Inverts the clockwise sign for the _clockwiseOnly filter only. Enable when the rotation axis points away from the camera (e.g. Vector3.back).")]
    [SerializeField] private bool _invertClockwiseDirection = false;

    [Header("UI Feedback")]
    [SerializeField] private string _pressHintText  = "Нажать";
    [SerializeField] private string _rotateHintText = "Вращать";

    [Header("Audio")]
    [SerializeField] private AudioClip _tickSound;
    [SerializeField, Range(0f, 1f)] private float _tickVolume = 0.6f;

    // ── State ──────────────────────────────────────────────────────────────────

    private int        _currentStep;
    private int        _stepsPerRevolution;
    private float      _stepAngle;

    private Quaternion _targetRotation;
    private bool       _isDragging;
    private bool       _isGrabbed;     // true only while THIS hand was clicked on
    private bool       _isHovered;     // true while cursor is over this hand's collider
    private float      _previousMouseAngle;
    private float      _angleAccumulator;
    private float      _dragRotationDelta;

    private Camera               _mainCamera;
    private PuzzleModeController _puzzleMode;
    private Collider             _collider;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True if position was restored from a save file. Used to skip randomization on load.</summary>
    public bool WasRestoredFromSave { get; private set; }

    /// <summary>The type of this clock hand (Minute or Hour).</summary>
    public ClockHandType HandType => _handType;

    /// <summary>
    /// Current time value in minutes (0–59 for Minute; hour × 5 for Hour, range 0–55).
    /// </summary>
    public int CurrentMinute => _handType == ClockHandType.Minute
        ? _currentStep
        : _currentStep * 5;

    /// <summary>Raised each time the hand advances or retreats by one discrete step.</summary>
    public event Action OnStepChanged;

    /// <summary>Raised when the mouse button is released after dragging this hand.</summary>
    public event Action OnReleased;

    /// <summary>Raised when the grab state changes. True = user started dragging this hand; false = released.</summary>
    public event Action<bool> OnGrabChanged;

    /// <summary>Raised when the cursor enters or leaves this hand's collider while puzzle mode is active.</summary>
    public event Action<bool> OnHoverChanged;

    /// <summary>Snap the hand to the given minute value without animation.</summary>
    /// <param name="minute">0–59 for Minute; 0–55 (multiples of 5) for Hour.</param>
    public void SnapToMinute(int minute)
    {
        int step = _handType == ClockHandType.Minute
            ? minute
            : minute / 5;

        SnapToStep(WrapStep(step));
    }

    /// <summary>
    /// Places the hand at a random step that is at least <paramref name="minStepsAway"/>
    /// steps from <paramref name="avoidStep"/> on the circular scale.
    /// </summary>
    public void RandomizePosition(int avoidStep, int minStepsAway)
    {
        int step;
        int attempts = 0;
        const int maxAttempts = 200;

        do
        {
            step = UnityEngine.Random.Range(0, _stepsPerRevolution);
            attempts++;
        }
        while (CircularDistance(step, avoidStep, _stepsPerRevolution) < minStepsAway
               && attempts < maxAttempts);

        SnapToStep(step);
    }

    // ── ISaveable ──────────────────────────────────────────────────────────────

    /// <summary>Fixed save ID derived from hand type — no manual setup required.</summary>
    public string SaveId => _handType == ClockHandType.Minute
        ? "clock_minute_hand"
        : "clock_hour_hand";

    /// <summary>Serializes the current step index.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new SaveData { currentStep = _currentStep });

    /// <summary>Restores the hand position from saved data.</summary>
    public void LoadSaveData(string json)
    {
        var data          = JsonUtility.FromJson<SaveData>(json);
        _currentStep      = data.currentStep;
        WasRestoredFromSave = true;
        SnapToStep(_currentStep);
    }

    [Serializable]
    private struct SaveData
    {
        public int currentStep;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _stepAngle          = _handType == ClockHandType.Minute ? 6f : 30f;
        _stepsPerRevolution = Mathf.RoundToInt(FullRevolutionDegrees / _stepAngle);
        _targetRotation     = transform.localRotation;
        _mainCamera         = Camera.main;
        _collider           = GetComponent<Collider>();

        _puzzleMode = GetComponentInParent<PuzzleModeController>();
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered += HandlePuzzleEntered;
            _puzzleMode.OnExited  += HandlePuzzleExited;
        }
        else
        {
            Debug.LogWarning($"[{nameof(ClockHand)}] PuzzleModeController not found in parent for {gameObject.name}.", this);
        }

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered -= HandlePuzzleEntered;
            _puzzleMode.OnExited  -= HandlePuzzleExited;
        }

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        ApplySmoothRotation();

        if (_puzzleMode != null && _puzzleMode.IsActive && !_puzzleMode.IsSolved)
        {
            ProcessInput();
            UpdateHover();
            UpdateUI();
        }
        else if (_isHovered)
        {
            SetHover(false);
        }
    }

    // ── Hover ──────────────────────────────────────────────────────────────────

    private void UpdateHover()
    {
        var mouse = Mouse.current;
        bool over = mouse != null && IsMouseOverThisHand(mouse);
        if (over != _isHovered)
            SetHover(over);
    }

    private void SetHover(bool hovered)
    {
        _isHovered = hovered;
        OnHoverChanged?.Invoke(_isHovered);
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    private void ProcessInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // On button press: raycast to see if the cursor is over THIS hand's collider.
        if (mouse.leftButton.wasPressedThisFrame)
        {
            _isGrabbed = IsMouseOverThisHand(mouse);
            if (_isGrabbed) OnGrabChanged?.Invoke(true);
        }

        if (!mouse.leftButton.isPressed)
        {
            EndDrag();
            if (_isGrabbed) OnGrabChanged?.Invoke(false);
            _isGrabbed = false;
            return;
        }

        // Only the hand that was grabbed processes drag.
        if (!_isGrabbed) return;

        HandleDrag(mouse);
    }

    /// <summary>
    /// Returns true when the mouse cursor ray hits this hand's collider.
    /// </summary>
    private bool IsMouseOverThisHand(Mouse mouse)
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null || _collider == null) return false;

        Ray ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
        return _collider.Raycast(ray, out _, 200f);
    }

    private void HandleDrag(Mouse mouse)
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Vector2 screenCenter = _mainCamera.WorldToScreenPoint(transform.position);
        Vector2 toMouse      = mouse.position.ReadValue() - screenCenter;

        if (toMouse.sqrMagnitude < MinCursorDistanceSqr) return;

        float currentAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        if (!_isDragging)
        {
            StartDrag(currentAngle);
            return;
        }

        float delta    = Mathf.DeltaAngle(_previousMouseAngle, currentAngle);
        float maxDelta = _maxInputRotationSpeed * Time.deltaTime;
        delta = Mathf.Clamp(delta, -maxDelta, maxDelta);

        // Atan2 decreases when moving CW on screen, so delta < 0 = CW.
        // AngleAxis with forward axis: positive = CCW in local space.
        // To match the visual direction (drag right → hand goes right/CW),
        // we invert: a rightward CW drag (delta < 0) should advance the hand CW.
        // _invertRotationDirection lets the designer flip this if the model is mirrored.
        delta = _invertRotationDirection ? delta : -delta;

        // Clockwise-only filter after the direction inversion is applied.
        // "Clockwise" now means delta > 0 after inversion.
        if (_clockwiseOnly)
        {
            bool isClockwise = _invertClockwiseDirection ? delta < 0f : delta > 0f;
            if (!isClockwise) delta = 0f;
        }

        _previousMouseAngle = Mathf.MoveTowardsAngle(_previousMouseAngle, currentAngle, _maxInputRotationSpeed * Time.deltaTime);
        _dragRotationDelta += delta;
        _angleAccumulator  += delta;

        ProcessStepAccumulator();
    }

    private void StartDrag(float angle)
    {
        _previousMouseAngle = angle;
        _isDragging         = true;
        _dragRotationDelta  = 0f;
    }

    private void EndDrag()
    {
        if (!_isDragging) return;
        ResetDragState();
        OnReleased?.Invoke();
    }

    private void ProcessStepAccumulator()
    {
        while (_angleAccumulator >= _stepAngle)
        {
            _angleAccumulator -= _stepAngle;
            RotateDiscrete(1);
        }

        while (_angleAccumulator <= -_stepAngle)
        {
            _angleAccumulator += _stepAngle;
            RotateDiscrete(-1);
        }
    }

    private void RotateDiscrete(int direction)
    {
        _currentStep    = WrapStep(_currentStep + direction);
        _targetRotation = CalculateStepRotation(_currentStep);

        if (_tickSound != null)
            AudioManager.Instance?.PlaySFX(_tickSound, _tickVolume);

        OnStepChanged?.Invoke();
    }

    // ── Visuals ────────────────────────────────────────────────────────────────

    private void ApplySmoothRotation()
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
        _currentStep    = WrapStep(step);
        _targetRotation = CalculateStepRotation(_currentStep);
        transform.localRotation = _targetRotation;
    }

    // ── UI ─────────────────────────────────────────────────────────────────────

    private void HandlePuzzleEntered()
    {
        UpdateUI();
    }

    private void HandlePuzzleExited()
    {
        if (_isHovered) SetHover(false);
        InteractionUI.Instance?.SetHint(false);
        InteractionUI.Instance?.SetCrosshair(CrosshairMode.Default);
    }

    private void UpdateUI()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool isHolding     = _isGrabbed && mouse.leftButton.isPressed;
        CrosshairMode mode = isHolding ? CrosshairMode.Grab : CrosshairMode.Hand;
        string hint        = isHolding ? _rotateHintText : _pressHintText;

        InteractionUI.Instance?.SetCrosshair(mode);
        InteractionUI.Instance?.SetHint(true, hint, false, mode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Quaternion CalculateStepRotation(int step)
        => Quaternion.AngleAxis(step * _stepAngle, _rotationAxis.normalized);

    private int WrapStep(int step)
        => ((step % _stepsPerRevolution) + _stepsPerRevolution) % _stepsPerRevolution;

    private static int CircularDistance(int a, int b, int modulo)
    {
        int diff = Mathf.Abs(a - b) % modulo;
        return diff > modulo / 2 ? modulo - diff : diff;
    }

    private void ResetDragState()
    {
        _isDragging        = false;
        _angleAccumulator  = 0f;
        _dragRotationDelta = 0f;
    }
}
