using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the player's flashlight based on a FlashlightConfig asset.
/// Toggle with F. Turns on only when the operating condition is met (e.g. FlashLightCharged in inventory).
/// Automatically switches off if the condition stops being met at runtime.
/// Intensity transitions smoothly; range, angle, and color apply instantly on state change.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlashlightController : MonoBehaviour
{
    [SerializeField] private FlashlightConfig config;

    private Light _light;
    private bool _isOn;
    private float _targetIntensity;

    private void Awake()
    {
        _light = GetComponent<Light>();
        ApplyStateImmediate(config.offState);
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

        // Smoothly interpolate intensity toward the target state
        _light.intensity = Mathf.MoveTowards(
            _light.intensity,
            _targetIntensity,
            config.transitionSpeed * Time.deltaTime
        );
    }

    /// <summary>Attempts to toggle the flashlight. Requires the operating condition to be met.</summary>
    public void TryToggle()
    {
        if (!_isOn && !config.operatingCondition.IsMet())
            return;

        SetState(!_isOn);
    }

    private void SetState(bool on)
    {
        _isOn = on;
        FlashlightState state = on ? config.onState : config.offState;

        _targetIntensity  = state.intensity;
        _light.range      = state.range;
        _light.spotAngle  = state.spotAngle;
        _light.color      = state.color;
    }

    private void ApplyStateImmediate(FlashlightState state)
    {
        _light.intensity = state.intensity;
        _light.range     = state.range;
        _light.spotAngle = state.spotAngle;
        _light.color     = state.color;
        _targetIntensity = state.intensity;
    }

    // Automatically turns off the flashlight if the inventory condition is no longer met
    private void OnInventoryChanged()
    {
        if (_isOn && !config.operatingCondition.IsMet())
            SetState(false);
    }
}
