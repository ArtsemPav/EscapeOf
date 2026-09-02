using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lever interaction component placed on the parent Lever object.
/// The collider lives on a child mesh (e.g. Panel) — FPSController detects it
/// and walks up via GetComponentInParent to find this component.
/// On interact, rotates the child LeverHandle on the X axis between two fixed angles
/// and invokes a UnityEvent so other systems can react.
/// Implements ISaveable: persists IsOn across sessions.
/// </summary>
public class LeverInteraction : MonoBehaviour, IInteractable, ISaveable
{
    private const float RotationThreshold = 0.05f;

    [Header("Save")]
    [Tooltip("Stable unique ID for the save system. Right-click → Generate Save ID to auto-fill.")]
    [SerializeField] private string _saveId;

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

    [Header("Events")]
    [Tooltip("Invoked when the lever is switched ON (from OFF to ON).")]
    [SerializeField] private UnityEvent _onToggleOn;

    [Tooltip("Invoked when the lever is switched OFF (from ON to OFF).")]
    [SerializeField] private UnityEvent _onToggleOff;

    /// <summary>True when the lever is in the ON position.</summary>
    public bool IsOn { get; private set; }

    public string SaveId => _saveId;

    private float _currentAngle;
    private float _targetAngle;
    private bool _animating;

    // Pending load state — applied in Start() after handle reference is ready.
    private bool  _hasPendingLoad;
    private bool  _pendingIsOn;

    private void Awake()
    {
        SaveManager.Instance?.Register(this);

        if (_leverHandle == null)
        {
            Debug.LogError($"{nameof(LeverInteraction)} on '{name}': _leverHandle is not assigned.");
            return;
        }
    }

    private void Start()
    {
        if (_hasPendingLoad)
        {
            _hasPendingLoad = false;
            SetStateQuiet(_pendingIsOn);
        }
        else
        {
            // Default OFF position.
            IsOn          = false;
            _currentAngle = _angleOff;
            _targetAngle  = _angleOff;
            ApplyRotation(_currentAngle);
        }
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
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

        if (IsOn)
            _onToggleOn?.Invoke();
        else
            _onToggleOff?.Invoke();

        SaveManager.Instance?.Save();
    }

    public string GetInteractText() => IsOn ? _textWhenOn : _textWhenOff;

    public bool IsPickable()  => false;
    public bool UseLMBClick   => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    /// <summary>Serializes lever state: IsOn.</summary>
    public string GetSaveData()
    {
        return JsonUtility.ToJson(new LeverSaveData { isOn = IsOn });
    }

    /// <summary>Stores pending state. Applied in Start() after handle is initialized.</summary>
    public void LoadSaveData(string json)
    {
        var data         = JsonUtility.FromJson<LeverSaveData>(json);
        _hasPendingLoad  = true;
        _pendingIsOn     = data.isOn;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the lever state instantly without animation, audio, events, or saving.
    /// Used when loading a saved game.
    /// </summary>
    public void SetStateQuiet(bool isOn)
    {
        IsOn          = isOn;
        _currentAngle = isOn ? _angleOn : _angleOff;
        _targetAngle  = _currentAngle;
        _animating    = false;
        ApplyRotation(_currentAngle);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyRotation(float angleX)
    {
        if (_leverHandle == null) return;
        Vector3 euler = _leverHandle.localEulerAngles;
        euler.x = angleX;
        _leverHandle.localEulerAngles = euler;
    }

    [Serializable]
    private struct LeverSaveData
    {
        public bool isOn;
    }

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
