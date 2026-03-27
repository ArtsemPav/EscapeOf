using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the player's flashlight based on a FlashlightConfig asset.
/// Toggle with F. Cycle through available lens modes with R.
/// Turns on only when the operating condition is met (e.g. FlashLightCharged in inventory).
/// Automatically switches off if the condition stops being met at runtime.
/// Intensity transitions smoothly; range, angle, and color apply instantly on mode/state change.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlashlightController : MonoBehaviour
{
    [SerializeField] private FlashlightConfig config;

    [Header("Audio")]
    [SerializeField] private AudioClip toggleClip;
    [SerializeField] private AudioClip modeSwitchClip;
    [SerializeField] [Range(0f, 1f)] private float toggleVolume    = 0.8f;
    [SerializeField] [Range(0f, 1f)] private float modeSwitchVolume = 0.6f;
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
        _light = GetComponent<Light>();
        ApplyStateImmediate(config.offState);

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
        InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;
    }

    private void Update()
    {
        if (Keyboard.current.fKey.wasPressedThisFrame)
            TryToggle();

        if (Keyboard.current.rKey.wasPressedThisFrame)
            TryCycleMode();

        // Smoothly interpolate intensity toward the target state
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
    /// Cycles to the next available lens mode. Skips modes whose requiredItem condition is not met.
    /// Only works while the flashlight is on.
    /// </summary>
    public void TryCycleMode()
    {
        if (!_isOn || config.modes == null || config.modes.Length <= 1)
            return;

        int startIndex = _modeIndex;

        for (int i = 1; i < config.modes.Length; i++)
        {
            int candidate = (startIndex + i) % config.modes.Length;
            FlashlightModeConfig modeConfig = config.modes[candidate];

            bool unlocked = modeConfig.requiredItem == null || modeConfig.requiredItem.IsMet();
            if (!unlocked)
                continue;

            _modeIndex  = candidate;
            CurrentMode = modeConfig.mode;

            ApplyOnState(modeConfig.onState);
            PlaySound(modeSwitchClip, modeSwitchVolume);
            OnModeChanged?.Invoke(CurrentMode);
            return;
        }
    }

    private void SetState(bool on)
    {
        _isOn = on;

        if (!on)
        {
            ApplyStateImmediate(config.offState);
            _targetIntensity = config.offState.intensity;
        }
        else
        {
            FlashlightModeConfig modeConfig = GetCurrentModeConfig();
            if (modeConfig != null)
                ApplyOnState(modeConfig.onState);
        }

        OnModeChanged?.Invoke(CurrentMode);
    }

    // Sets target values for a smooth on-state transition (intensity animates, rest applies instantly).
    private void ApplyOnState(FlashlightState state)
    {
        _targetIntensity = state.intensity;
        _light.range     = state.range;
        _light.spotAngle = state.spotAngle;
        _light.color     = state.color;
    }

    private void ApplyStateImmediate(FlashlightState state)
    {
        _light.intensity = state.intensity;
        _light.range     = state.range;
        _light.spotAngle = state.spotAngle;
        _light.color     = state.color;
        _targetIntensity = state.intensity;
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

    // Automatically turns off the flashlight if the operating condition is no longer met.
    private void OnInventoryChanged()
    {
        if (_isOn && !config.operatingCondition.IsMet())
            SetState(false);
    }
}
