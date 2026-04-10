using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Rotary dial on a Lock object.
///
/// Flow:
///   1. Player presses E while looking at the Lock — the SafeCamera activates via Cinemachine
///      priority, player input is disabled, and cursor becomes visible.
///   2. While in inspection mode:
///      • LMB  — rotate the dial one step counter-clockwise.
///      • RMB  — rotate the dial one step clockwise.
///      • Esc  — exit inspection mode and restore player camera and input.
///   3. Optionally checks a target step and fires _onUnlocked when reached.
///
/// Requires a <see cref="CinemachineCamera"/> assigned as the inspection camera (e.g. SafeCamera).
/// Implements ISaveable to persist current step and unlock state across sessions.
/// </summary>
public class LockDial : MonoBehaviour, IInteractable, ISaveable
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Rotation Settings")]
    [Tooltip("Degrees per interaction step. 22.5° gives 16 discrete positions per revolution.")]
    [SerializeField] private float _stepAngle = 22.5f;

    [Tooltip("Local axis to rotate around (e.g. Y for a front-facing combination dial).")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.up;

    [Tooltip("Speed of the smooth rotation animation in degrees per second.")]
    [SerializeField] private float _rotationSpeed = 360f;

    [Header("Unlock Condition (optional)")]
    [Tooltip("Enable to fire OnUnlocked when the dial reaches the Target Step.")]
    [SerializeField] private bool _checkTargetStep = false;

    [Tooltip("Step index (0-based) that unlocks the dial. Total steps = 360 / Step Angle.")]
    [SerializeField] private int _targetStep = 0;

    [Header("Camera")]
    [Tooltip("The CinemachineCamera that focuses on the Lock during inspection.")]
    [SerializeField] private CinemachineCamera _inspectionCamera;

    [Header("Interaction Text")]
    [SerializeField] private string _interactText = "Осмотреть замок";
    [SerializeField] private string _activeInteractText = "Крутить: ЛКМ / ПКМ  •  Выход: Esc";
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
    private bool _isInspecting;

    private Quaternion _targetRotation;
    private bool _isAnimating;

    private int _stepsPerRevolution;

    private bool _menuSubscribed;

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

        // Ensure inspection camera starts inactive.
        if (_inspectionCamera != null)
            _inspectionCamera.gameObject.SetActive(false);

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Defer subscription to Start so InputManager.Instance is guaranteed to be initialized.
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnMenuPerformed += OnMenuPerformed;
            _menuSubscribed = true;
        }
        else
        {
            Debug.LogWarning("LockDial: InputManager.Instance is null in Start. Esc will not exit inspection mode.", this);
        }
    }

    private void OnDisable()
    {
        if (_menuSubscribed && InputManager.Instance != null)
        {
            InputManager.Instance.OnMenuPerformed -= OnMenuPerformed;
            _menuSubscribed = false;
        }
    }

    private void OnDestroy()
    {
        // Guarantee player is restored if the object is destroyed mid-inspection.
        if (_isInspecting)
            ExitInspectionMode();

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_isAnimating)
            AnimateRotation();

        if (_isInspecting)
            HandleInspectionInput();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => !_isUnlocked && !_isInspecting;

    /// <summary>First interaction: enters inspection mode and activates the SafeCamera.</summary>
    public void Interact()
    {
        if (_isUnlocked || _isInspecting) return;
        EnterInspectionMode();
    }

    public string GetInteractText()
    {
        if (_isUnlocked)   return _unlockedInteractText;
        if (_isInspecting) return _activeInteractText;
        return _interactText;
    }

    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Inspection Mode ────────────────────────────────────────────────────────

    private void EnterInspectionMode()
    {
        _isInspecting = true;
        Debug.Log($"[LockDial] EnterInspectionMode. InputManager={InputManager.Instance != null}, subscribed={_menuSubscribed}", this);

        // Activate inspection camera.
        if (_inspectionCamera != null)
            _inspectionCamera.gameObject.SetActive(true);

        // Block player input and increment panel counter so GameManager skips pause on Esc.
        UIManager.Instance?.PushModalState();

        // Show cursor for click input.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        InteractionUI.Instance?.SetHint(true, _activeInteractText, false, CrosshairMode.Hand);
    }

    /// <summary>Exits inspection mode and restores the player camera and input.</summary>
    private void ExitInspectionMode()
    {
        _isInspecting = false;

        // Deactivate inspection camera — Brain falls back to PlayerCamera.
        if (_inspectionCamera != null)
            _inspectionCamera.gameObject.SetActive(false);

        // Decrement panel counter and restore player input.
        UIManager.Instance?.PopModalState();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        InteractionUI.Instance?.SetHint(false);
    }

    // ── Input Handling ─────────────────────────────────────────────────────────

    /// <summary>Handles LMB / RMB rotation while in inspection mode.</summary>
    private void HandleInspectionInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            RotateStep(-1); // counter-clockwise
        else if (mouse.rightButton.wasPressedThisFrame)
            RotateStep(1);  // clockwise
    }

    /// <summary>
    /// Called by InputManager when the Menu action fires (Esc).
    /// Exits inspection mode on the next frame so that GameManager.OnToggleMenu still sees
    /// IsAnyPanelOpen == true when it runs in the same event dispatch and skips the pause.
    /// </summary>
    private void OnMenuPerformed()
    {
        Debug.Log($"[LockDial] OnMenuPerformed called. _isInspecting={_isInspecting}", this);
        if (!_isInspecting) return;
        StartCoroutine(ExitNextFrame());
    }

    private IEnumerator ExitNextFrame()
    {
        yield return null;
        Debug.Log("[LockDial] ExitNextFrame — calling ExitInspectionMode", this);
        ExitInspectionMode();
    }

    // ── Rotation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rotates the dial by one step.
    /// <paramref name="direction"/> +1 for clockwise, -1 for counter-clockwise.
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
        _isUnlocked = true;
        ExitInspectionMode();
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
