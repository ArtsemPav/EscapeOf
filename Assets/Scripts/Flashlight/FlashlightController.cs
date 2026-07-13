using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the player's flashlight based on a FlashlightConfig asset.
/// Toggle with F. Active mode is detected automatically from inventory contents —
/// no manual switching. The flashlight reacts to crafting (e.g. swapping lenses)
/// by updating its light properties to match whichever flashlight variant is in the inventory.
/// Turns on only when the operating condition is met (e.g. FlashLightCharged or FlashLightUV in inventory).
/// Automatically switches off if the condition stops being met at runtime.
/// Intensity transitions smoothly; range, angle, and color apply instantly on mode/state change.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlashlightController : MonoBehaviour
{
    /// <summary>Singleton instance. Set in Awake, cleared in OnDestroy.</summary>
    public static FlashlightController Instance { get; private set; }

    [SerializeField] private FlashlightConfig config;

    [Header("Audio")]
    [SerializeField] private AudioClip toggleClip;
    [SerializeField] [Range(0f, 1f)] private float toggleVolume = 0.8f;
    [Tooltip("Condition to play the click sound. Should include all flashlight variants (charged and uncharged). " +
             "If not set, sound only plays when the light actually toggles.")]
    [SerializeField] private InventoryCondition soundCondition;

    /// <summary>Fired whenever the active mode changes. Passes the new mode.</summary>
    public event Action<FlashlightMode> OnModeChanged;

    /// <summary>Returns the currently active flashlight mode.</summary>
    public FlashlightMode CurrentMode { get; private set; } = FlashlightMode.Normal;

    /// <summary>Returns true when the flashlight is currently on.</summary>
    public bool IsOn => _isOn;

    private Light _light;
    private AudioSource _audioSource;
    private bool _isOn;
    private float _targetIntensity;
    private int _modeIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _light = GetComponent<Light>();
        _light.intensity = 0f;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.loop         = false;

        _modeIndex = 0;
        if (config.modes != null && config.modes.Length > 0)
            CurrentMode = config.modes[0].mode;
    }

    private void Start()
    {
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;
            DetectAndApplyMode();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;
    }

    private void Update()
    {
        if (Keyboard.current.fKey.wasPressedThisFrame)
            TryToggle();

        _light.intensity = Mathf.MoveTowards(
            _light.intensity,
            _targetIntensity,
            config.transitionSpeed * Time.deltaTime
        );
    }

    /// <summary>Attempts to toggle the flashlight on or off. Requires the operating condition to be met.</summary>
    public void TryToggle()
    {
        bool canOperate       = config.operatingCondition.IsMet();
        bool hasAnyFlashlight = soundCondition != null ? soundCondition.IsMet() : canOperate;

        if (hasAnyFlashlight)
            PlaySound(toggleClip, toggleVolume);

        if (!_isOn && !canOperate)
            return;

        SetState(!_isOn);
    }

    /// <summary>
    /// Scans the config.modes array (in reverse) and activates the first matching mode
    /// whose requiredItem condition is met. Modes with null requiredItem are always available
    /// and serve as fallback — place them first in the array so specific modes take priority.
    /// Applies the new mode's onState immediately if the flashlight is currently on.
    /// </summary>
    private void DetectAndApplyMode()
    {
        if (config.modes == null || config.modes.Length == 0)
            return;

        int newModeIndex = -1;

        for (int i = config.modes.Length - 1; i >= 0; i--)
        {
            FlashlightModeConfig modeConfig = config.modes[i];
            bool unlocked = modeConfig.requiredItem == null || modeConfig.requiredItem.IsMet();
            if (unlocked)
            {
                newModeIndex = i;
                break;
            }
        }

        if (newModeIndex == -1 || newModeIndex == _modeIndex)
            return;

        _modeIndex  = newModeIndex;
        CurrentMode = config.modes[newModeIndex].mode;

        if (_isOn)
        {
            FlashlightState onState = config.modes[newModeIndex].onState;
            _targetIntensity  = onState.intensity;
            _light.range      = onState.range;
            _light.spotAngle  = onState.spotAngle;
            _light.color      = onState.color;
        }

        OnModeChanged?.Invoke(CurrentMode);
    }

    private void SetState(bool on)
    {
        _isOn = on;

        if (!on)
        {
            _targetIntensity = 0f;
        }
        else
        {
            FlashlightModeConfig modeConfig = GetCurrentModeConfig();
            if (modeConfig != null)
            {
                _targetIntensity  = modeConfig.onState.intensity;
                _light.range      = modeConfig.onState.range;
                _light.spotAngle  = modeConfig.onState.spotAngle;
                _light.color      = modeConfig.onState.color;
            }
        }

        OnModeChanged?.Invoke(CurrentMode);
    }

    private FlashlightModeConfig GetCurrentModeConfig()
    {
        if (config.modes == null || config.modes.Length == 0)
            return null;

        return config.modes[_modeIndex];
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (clip != null)
            _audioSource.PlayOneShot(clip, volume);
    }

    private void OnInventoryChanged()
    {
        DetectAndApplyMode();

        if (_isOn && !config.operatingCondition.IsMet())
            SetState(false);
    }
}
