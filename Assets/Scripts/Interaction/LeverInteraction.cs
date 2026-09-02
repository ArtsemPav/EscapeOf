using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lever interaction component placed on the parent Lever object.
/// The collider lives on a child mesh (e.g. Panel) — FPSController detects it
/// and walks up via GetComponentInParent to find this component.
/// On interact, rotates the child LeverHandle on the X axis between two fixed angles
/// and invokes a UnityEvent so other systems can react.
/// </summary>
public class LeverInteraction : MonoBehaviour, IInteractable
{
    private const float RotationThreshold = 0.05f;

    [Header("Handle Reference")]
    [Tooltip("The child GameObject whose X rotation is animated.")]
    [SerializeField] private Transform _leverHandle;

    [Header("Rotation")]
    [Tooltip("X-axis angle when the lever is in the OFF position.")]
    [SerializeField] private float _angleOff = 80f;

    [Tooltip("X-axis angle when the lever is in the ON position.")]
    [SerializeField] private float _angleOn = -80f;

    [Tooltip("Lerp speed toward the target rotation (higher = snappier).")]
    [SerializeField] private float _rotationSpeed = 5f;

    [Header("Audio")]
    [SerializeField] private AudioClip _switchClip;
    [SerializeField, Range(0f, 1f)] private float _switchVolume = 0.8f;

    [Header("Interaction")]
    [SerializeField] private string _textWhenOff = "Потянуть рычаг";
    [SerializeField] private string _textWhenOn  = "Вернуть рычаг";

    [Header("Event")]
    [Tooltip("Invoked every time the lever is toggled.")]
    [SerializeField] private UnityEvent _onToggled;

    /// <summary>True when the lever is in the ON position.</summary>
    public bool IsOn { get; private set; }

    private float _currentAngle;
    private float _targetAngle;
    private bool _animating;

    private void Awake()
    {
        if (_leverHandle == null)
        {
            Debug.LogError($"{nameof(LeverInteraction)} on '{name}': _leverHandle is not assigned.");
            return;
        }

        // Start in the OFF position.
        IsOn         = false;
        _currentAngle = _angleOff;
        _targetAngle  = _angleOff;
        ApplyRotation(_currentAngle);
    }

    private void Update()
    {
        if (!_animating) return;

        _currentAngle = Mathf.Lerp(_currentAngle, _targetAngle, _rotationSpeed * Time.deltaTime);

        if (Mathf.Abs(_currentAngle - _targetAngle) < RotationThreshold)
        {
            _currentAngle = _targetAngle;
            _animating    = false;
        }

        ApplyRotation(_currentAngle);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract() => _leverHandle != null;

    /// <summary>Toggles the lever between OFF and ON positions and fires the event.</summary>
    public void Interact()
    {
        if (!CanInteract()) return;

        IsOn        = !IsOn;
        _targetAngle = IsOn ? _angleOn : _angleOff;
        _animating   = true;

        if (_switchClip != null)
            AudioManager.Instance?.PlaySFX(_switchClip, _switchVolume);

        _onToggled?.Invoke();
    }

    public string GetInteractText() => IsOn ? _textWhenOn : _textWhenOff;

    public bool IsPickable()  => false;
    public bool UseLMBClick   => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyRotation(float angleX)
    {
        Vector3 euler = _leverHandle.localEulerAngles;
        euler.x = angleX;
        _leverHandle.localEulerAngles = euler;
    }
}
