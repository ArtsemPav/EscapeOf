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

    [Header("Proximity Dimming")]
    [Tooltip("Transform used as the ray origin (should be the camera, not the lagging flashlight). " +
             "If not assigned, falls back to Camera.main.")]
    [SerializeField] private Transform proximityRayOrigin;

    [Tooltip("How far ahead to cast for proximity detection (metres). " +
             "When a surface is closer than this, intensity is scaled down.")]
    [SerializeField] [Min(0.3f)] private float proximityRange = 1.2f;

    [Tooltip("Minimum intensity scale when the flashlight is point-blank against a surface. " +
             "0.3 = 30% of full brightness at zero distance.")]
    [SerializeField] [Range(0.01f, 1f)] private float proximityMinScale = 0.3f;

    [Tooltip("Falloff curve power. 1 = linear, 2 = quadratic (stays dim longer, then ramps up).")]
    [SerializeField] [Range(0.5f, 4f)] private float proximityFalloff = 2f;

    [Tooltip("How quickly the proximity scale catches up to the target value. " +
             "Higher = snappier, lower = smoother. Prevents flickering from brief ray hits.")]
    [SerializeField] [Range(1f, 30f)] private float proximitySmoothSpeed = 12f;

    [Tooltip("Half-angle of the probe fan in degrees. Multiple rays are cast within this cone " +
             "and a majority must hit to trigger dimming — prevents false triggers from " +
             "glancing hits on stairs, pillars, and side walls.")]
    [SerializeField] [Range(0f, 30f)] private float probeFanHalfAngle = 10f;

    [Tooltip("Layers checked by the proximity cast. Default = everything.")]
    [SerializeField] private LayerMask proximityMask = ~0;

    /// <summary>Fired whenever the active mode changes. Passes the new mode.</summary>
    public event Action<FlashlightMode> OnModeChanged;

    /// <summary>Returns the currently active flashlight mode.</summary>
    public FlashlightMode CurrentMode { get; private set; } = FlashlightMode.Normal;

    /// <summary>Returns true when the flashlight is currently on.</summary>
    public bool IsOn => _isOn;

    /// <summary>
    /// When true, prevents the flashlight from being toggled on.
    /// Set by external systems (e.g. PuzzleModeController) to disable flashlight during puzzle mode.
    /// </summary>
    public bool IsLocked { get; set; }

    private Light _light;
    private AudioSource _audioSource;
    private bool _isOn;
    private float _targetIntensity;
    private float _proximityScale = 1f;
    private float _proximityScaleVelocity;
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

        if (proximityRayOrigin == null && Camera.main != null)
            proximityRayOrigin = Camera.main.transform;

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

        UpdateProximityScale();

        _light.intensity = Mathf.MoveTowards(
            _light.intensity,
            _targetIntensity * _proximityScale,
            config.transitionSpeed * Time.deltaTime
        );
    }

    private const int ProbeCount = 5;
    private const int ProbeMajority = 3;

    /// <summary>
    /// Casts a fan of thin rays from the camera and scales down the target intensity
    /// only when a majority of probes hit a surface within proximityRange.
    /// This prevents false triggers from glancing hits on stairs, pillars, and side walls.
    /// Uses SmoothDamp to prevent flickering from brief hits.
    /// </summary>
    private void UpdateProximityScale()
    {
        if (!_isOn)
        {
            _proximityScale = 1f;
            _proximityScaleVelocity = 0f;
            return;
        }

        Vector3 origin = proximityRayOrigin != null ? proximityRayOrigin.position : transform.position;
        Vector3 fwd    = proximityRayOrigin != null ? proximityRayOrigin.forward  : transform.forward;
        Vector3 right  = proximityRayOrigin != null ? proximityRayOrigin.right    : transform.right;
        Vector3 up     = proximityRayOrigin != null ? proximityRayOrigin.up       : transform.up;

        int hits = 0;
        float closestDist = proximityRange;

        for (int i = 0; i < ProbeCount; i++)
        {
            Vector3 dir = fwd;
            if (i == 1) dir = Quaternion.AngleAxis( probeFanHalfAngle, up) * fwd;
            if (i == 2) dir = Quaternion.AngleAxis(-probeFanHalfAngle, up) * fwd;
            if (i == 3) dir = Quaternion.AngleAxis( probeFanHalfAngle, right) * fwd;
            if (i == 4) dir = Quaternion.AngleAxis(-probeFanHalfAngle, right) * fwd;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, proximityRange, proximityMask, QueryTriggerInteraction.Ignore))
            {
                hits++;
                if (hit.distance < closestDist)
                    closestDist = hit.distance;
            }
        }

        float targetScale = 1f;
        if (hits >= ProbeMajority)
        {
            float t = Mathf.Clamp01(closestDist / proximityRange);
            targetScale = Mathf.Lerp(proximityMinScale, 1f, Mathf.Pow(t, proximityFalloff));
        }

        _proximityScale = Mathf.SmoothDamp(
            _proximityScale,
            targetScale,
            ref _proximityScaleVelocity,
            1f / proximitySmoothSpeed
        );
    }

    /// <summary>Attempts to toggle the flashlight on or off. Requires the operating condition to be met.</summary>
    public void TryToggle()
    {
        if (IsLocked) return;

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

    /// <summary>Forces the flashlight off without playing sound. Used by PuzzleModeController.</summary>
    public void ForceOff()
    {
        SetState(false);
    }

    /// <summary>Forces the flashlight on without sound. Used by PuzzleModeController to restore state.</summary>
    public void ForceOn()
    {
        if (config == null || !config.operatingCondition.IsMet()) return;
        SetState(true);
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
