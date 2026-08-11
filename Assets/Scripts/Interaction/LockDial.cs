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
/// Rotary combination dial. Handles rotation logic, combination validation, 
/// and UI feedback in puzzle mode.
/// </summary>
public class LockDial : MonoBehaviour, ISaveable, IPuzzleDropHandler, IPuzzleDropTarget
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const float FullRevolutionDegrees = 360f;
    private const float SnapAngleThreshold    = 0.1f;
    private const float MinCursorDistanceSqr  = 1f;
    private const float MinDragDeltaThreshold = 0.05f;
    private const float ComboResetStepMultiplier = 2f;

    // Max distance for the IsMouseOverDial raycast — the puzzle camera is close to the dial.
    private const float DialRaycastMaxDistance = 10f;

    // ── Inspector ──────────────────────────────────────────────────────────────
    [SerializeField] private PuzzleModeController _puzzleMode;
    [Header("Rotation")]
    [Tooltip("Degrees per discrete step. 3.6° gives 100 positions per revolution.")]
    [SerializeField] private float _stepAngle = 3.6f;

    [Tooltip("Local axis to rotate around.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;

    [Tooltip("Speed of the smooth rotation in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Tooltip("Maximum rotation speed from user input in degrees per second.")]
    [SerializeField] private float _maxInputRotationSpeed = 720f;

    [Header("Combination Settings")]
    [Tooltip("Sequence of steps to unlock.")]
    [SerializeField] private ComboStep[] _combination = new ComboStep[4];

    [Tooltip("If true, combination target values will be randomized on start.")]
    [SerializeField] private bool _randomizeOnStart = true;

    [Header("Audio")]
    [SerializeField] private AudioClip _tickSound;
    [SerializeField, Range(0f, 1f)] private float _tickVolume = 1f;
    [SerializeField] private AudioClip _correctStepSound;
    [SerializeField, Range(0f, 1f)] private float _correctStepVolume = 1f;

    [Tooltip("If assigned, audio feedback will only play when this item is APPLIED to the dial.")]
    [SerializeField] private ItemData _requiredItemForAudio;



    [Tooltip("Looping clip played as an additional background layer while the puzzle is active (requires Required Item For Audio to be applied).")]
    [SerializeField] private AudioClip _puzzleBackgroundLayer;

    [Tooltip("Volume for the background layer clip.")]
    [SerializeField, Range(0f, 1f)] private float _puzzleBackgroundLayerVolume = 1f;

    [Header("Events")]
    [Tooltip("Fired when the entire combination is correctly entered.")]
    [SerializeField] private UnityEvent _onUnlocked;

    [Tooltip("Fired on every discrete step rotation.")]
    [SerializeField] private UnityEvent _onRotated;

    [Header("Save")]
    [SerializeField] private string _saveId;

    // ── State ──────────────────────────────────────────────────────────────────

    private Collider _colliderLock;
    private int  _currentStep;
    private bool _isUnlocked;
    private int  _stepsPerRevolution;

    private Quaternion _targetRotation;
    private bool       _isDragging;
    private float      _previousMouseAngle;
    private float      _angleAccumulator;
    private float      _dragRotationDelta;
    
    private int               _comboProgressIndex; 
    private RotationDirection _lastDragDirection;

    private Camera               _mainCamera;
    private bool                 _isRequiredItemApplied;

    // ── Public API ─────────────────────────────────────────────────────────────

    public int CurrentStep => _currentStep;
    public bool IsUnlocked => _isUnlocked;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() => JsonUtility.ToJson(new SaveData
    {
        currentStep = _currentStep,
        isUnlocked  = _isUnlocked,
        isRequiredItemApplied = _isRequiredItemApplied,
        comboProgressIndex = _comboProgressIndex
    });

    public void LoadSaveData(string json)
    {
        var data     = JsonUtility.FromJson<SaveData>(json);
        _currentStep = data.currentStep;
        _isUnlocked  = data.isUnlocked;
        _isRequiredItemApplied = data.isRequiredItemApplied;
        _comboProgressIndex = data.comboProgressIndex;
        SnapToStep(_currentStep);
    }

    [Serializable]
    private struct SaveData
    {
        public int  currentStep;
        public bool isUnlocked;
        public bool isRequiredItemApplied;
        public int  comboProgressIndex;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _stepsPerRevolution = Mathf.RoundToInt(FullRevolutionDegrees / _stepAngle);
        _targetRotation     = transform.localRotation;
        _colliderLock = GetComponent<Collider>();
        _mainCamera         = Camera.main;
        if (_puzzleMode == null) {
            _puzzleMode = GetComponentInParent<PuzzleModeController>();
        }

        if (_randomizeOnStart && !_isUnlocked)
        {
            RandomizeCombination();
        }

        if (_puzzleMode != null)
        {
            _puzzleMode.OnExited  += ResetUI;
            _puzzleMode.OnExited  += OnPuzzleExited;
            _puzzleMode.OnEntered += OnPuzzleEntered;
        }
        else
        {
            Debug.LogWarning($"[{nameof(LockDial)}] PuzzleModeController not found for {gameObject.name}.", this);
        }

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnExited  -= ResetUI;
            _puzzleMode.OnExited  -= OnPuzzleExited;
            _puzzleMode.OnEntered -= OnPuzzleEntered;
        }

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        ApplySmoothRotation();

        if (_puzzleMode != null && _puzzleMode.IsActive && !_isUnlocked)
        {
            ProcessInput();
        }
    }

    // ── Logic ──────────────────────────────────────────────────────────────────

    private void ProcessInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (_isDragging)
        {
            if (!mouse.leftButton.isPressed)
            {
                EndDrag();
            }
            else
            {
                HandleDrag(mouse);
            }
        }
        else if (mouse.leftButton.wasPressedThisFrame && IsMouseOverDial(mouse))
        {
            HandleDrag(mouse);
        }
    }

    private bool IsMouseOverDial(Mouse mouse)
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return false;

        Ray ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());

        // Use a layer mask that only includes PuzzleInteractable so the Safe's MeshCollider
        // (on Default layer) and other geometry don't block the raycast to the dial.
        int puzzleLayer = LayerMask.NameToLayer("PuzzleInteractable");
        if (puzzleLayer == -1)
        {
            Debug.LogError($"[{nameof(LockDial)}] Layer 'PuzzleInteractable' not found in project settings. Dial interaction will not work.", this);
            return false;
        }
        int mask = 1 << puzzleLayer;

        if (Physics.Raycast(ray, out RaycastHit hit, DialRaycastMaxDistance, mask))
        {
            return hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform);
        }
        return false;
    }

    private void HandleDrag(Mouse mouse)
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Vector2 screenCenter = _mainCamera.WorldToScreenPoint(transform.position);
        Vector2 toMouse = mouse.position.ReadValue() - screenCenter;

        if (toMouse.sqrMagnitude < MinCursorDistanceSqr) return;

        float currentAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        if (!_isDragging)
        {
            StartDrag(currentAngle);
            return;
        }

        float delta = Mathf.DeltaAngle(_previousMouseAngle, currentAngle);
        
        // Limit the rotation delta based on max input speed
        float maxDelta = _maxInputRotationSpeed * Time.deltaTime;
        delta = Mathf.Clamp(delta, -maxDelta, maxDelta);

        _previousMouseAngle = currentAngle;
        _dragRotationDelta += delta;
        _angleAccumulator += delta;

        ProcessStepAccumulator();
    }

    private void StartDrag(float angle)
    {
        _previousMouseAngle = angle;
        _isDragging = true;
        _dragRotationDelta = 0f;
    }

    private void EndDrag()
    {
        if (!_isDragging) return;

        if (Mathf.Abs(_dragRotationDelta) > MinDragDeltaThreshold)
        {
            _lastDragDirection = _dragRotationDelta > 0 
                ? RotationDirection.Clockwise 
                : RotationDirection.CounterClockwise;
            
            CheckCombination();
        }
        
        ResetDragState();
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
        _currentStep = WrapStep(_currentStep + direction);
        _targetRotation = CalculateStepRotation(_currentStep);
        
        // Determine rotation direction for audio feedback
        RotationDirection currentDir = direction > 0 
            ? RotationDirection.Clockwise 
            : RotationDirection.CounterClockwise;

        // Play audio feedback
        PlayRotationAudio(currentDir);
        
        _onRotated?.Invoke();
    }

    private void PlayRotationAudio(RotationDirection currentDirection)
    {
        if (AudioManager.Instance == null) return;

        bool hasStethoscope = HasRequiredItem();
        
        // Tick sound is always played regardless of stethoscope
        if (_tickSound != null)
            AudioManager.Instance.PlaySFX(_tickSound, _tickVolume);

        // Correct step sound is only played if the player has the stethoscope 
        // AND reaches the target value with the correct rotation direction.
        if (hasStethoscope && _comboProgressIndex < _combination.Length)
        {
            ComboStep target = _combination[_comboProgressIndex];
            bool isCorrectMovement = _currentStep == target.TargetValue 
                                  && currentDirection == target.RequiredDirection;

            if (isCorrectMovement && _correctStepSound != null)
                AudioManager.Instance.PlaySFX(_correctStepSound, _correctStepVolume);
        }
    }

    private void CheckCombination()
    {
        if (_combination == null || _combination.Length == 0) return;
        if (_comboProgressIndex >= _combination.Length) return;

        ComboStep target = _combination[_comboProgressIndex];

        // Validate current step and direction
        if (_currentStep == target.TargetValue && _lastDragDirection == target.RequiredDirection)
        {
            _comboProgressIndex++;
            Debug.Log($"[{nameof(LockDial)}] Correct step! Progress: {_comboProgressIndex}/{_combination.Length}");

            if (_comboProgressIndex >= _combination.Length)
            {
                Unlock();
            }
        }
        else if (Mathf.Abs(_dragRotationDelta) > _stepAngle * ComboResetStepMultiplier)
        {
            // Reset if significant movement in wrong way
            _comboProgressIndex = 0;
            Debug.Log($"[{nameof(LockDial)}] Wrong step. Resetting sequence.");
        }
    }

    private void Unlock()
    {
        _isUnlocked = true;
        ResetDragState();
        ResetUI();
        _onUnlocked?.Invoke();
        _puzzleMode?.SetSolved(); // SetSolved fires OnSolved, ExitPuzzleMode, and Save internally
    }

    // ── Stethoscope audio isolation ────────────────────────────────────────────

    private bool HasRequiredItem()
        => _requiredItemForAudio == null || _isRequiredItemApplied;

    /// <summary>
    /// Handles item application from the PuzzleInventoryBar.
    /// If the required item (stethoscope) is dropped, it enables advanced audio feedback.
    /// When _colliderLock is assigned, the drop is only accepted when the release raycast hits that exact collider.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        if (item != _requiredItemForAudio || _isRequiredItemApplied) return false;

        if (_colliderLock != null)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit) || hit.collider != _colliderLock)
                return false;
        }

        _isRequiredItemApplied = true;
        Debug.Log($"[{nameof(LockDial)}] {_requiredItemForAudio.itemName} applied to safe. Advanced audio feedback enabled.");

        if (_puzzleMode != null && _puzzleMode.IsActive)
        {
            AudioManager.Instance?.MuteBackground();

            if (_puzzleBackgroundLayer != null)
                AudioManager.Instance?.PlayBackgroundLayer(_puzzleBackgroundLayer, _puzzleBackgroundLayerVolume);
        }

        return true;
    }

    // ── IPuzzleDropTarget ──────────────────────────────────────────────────────

    /// <summary>Returns the hint text to display when hovering with a dragged item.</summary>
    public string GetDropHint() => "на сейф";

    /// <summary>Returns true when the target is compatible with the given item.</summary>
    public bool CanAccept(ItemData item)
    {
        // Only accept the stethoscope if it hasn't been applied yet.
        return item == _requiredItemForAudio && !_isRequiredItemApplied;
    }

    private void OnPuzzleEntered()
    {
        // Ensure the dial collider is enabled so IsMouseOverDial can detect it.
        // It may have been disabled by RoomController.Lock() or left disabled in the prefab.
        if (_colliderLock != null) _colliderLock.enabled = true;

        if (_isRequiredItemApplied) {
            AudioManager.Instance?.MuteBackground();

            if (_puzzleBackgroundLayer != null)
                AudioManager.Instance?.PlayBackgroundLayer(_puzzleBackgroundLayer, _puzzleBackgroundLayerVolume);
        }
    }

    private void OnPuzzleExited()
    {
        // Disable the dial collider so it doesn't block PuzzleInteract's collider
        // (they overlap on the door) when not in puzzle mode.
        if (_colliderLock != null) _colliderLock.enabled = false;

        if (!HasRequiredItem()) return;

        AudioManager.Instance?.StopBackgroundLayer();
        AudioManager.Instance?.UnmuteBackground();
        AudioManager.Instance?.PlayGameMusic();
    }

    // ── UI & Visuals ───────────────────────────────────────────────────────────

    private void ResetUI()
    {
        InteractionUI.Instance?.SetHint(false);
        InteractionUI.Instance?.SetCrosshair(CrosshairMode.Default);
    }

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
        _targetRotation = CalculateStepRotation(step);
        transform.localRotation = _targetRotation;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Quaternion CalculateStepRotation(int step)
        => Quaternion.AngleAxis(step * _stepAngle, _rotationAxis.normalized);

    private int WrapStep(int step)
        => ((step % _stepsPerRevolution) + _stepsPerRevolution) % _stepsPerRevolution;

    private void ResetDragState()
    {
        _isDragging = false;
        _angleAccumulator = 0f;
        _dragRotationDelta = 0f;
    }

    private void RandomizeCombination()
    {
        if (_combination == null || _combination.Length == 0) return;

        RotationDirection currentDir = (UnityEngine.Random.value > 0.5f) 
            ? RotationDirection.Clockwise 
            : RotationDirection.CounterClockwise;

        for (int i = 0; i < _combination.Length; i++)
        {
            _combination[i].TargetValue = UnityEngine.Random.Range(0, _stepsPerRevolution);
            _combination[i].RequiredDirection = currentDir;
            currentDir = (currentDir == RotationDirection.Clockwise) 
                ? RotationDirection.CounterClockwise 
                : RotationDirection.Clockwise;
        }
    }

    // ── Editor ─────────────────────────────────────────────────────────────────

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Snap to Current Step")]
    private void EditorSnapToCurrentStep() => SnapToStep(WrapStep(_currentStep));
}
