using System;
using System.Collections;
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
public class FlashlightController : MonoBehaviour, ISaveable
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

    /// <summary>Overrides the ray origin used for proximity dimming (e.g. the cinematic camera).</summary>
    public void SetProximityRayOrigin(Transform origin) => proximityRayOrigin = origin;

    /// <summary>The current proximity ray origin transform.</summary>
    public Transform ProximityRayOrigin => proximityRayOrigin;

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

    [Tooltip("Layers the proximity probes ignore. Put Draggable here so props carried in front " +
             "of the camera are never mistaken for walls.")]
    [SerializeField] private LayerMask probeIgnoreMask;

    [Header("Close Range Compensation")]
    [Tooltip("Caps the surface brightness when the beam hits a wall up close. Illuminance on a surface " +
             "grows like 1/d², so point-blank walls blow out (and Bloom flares). When enabled, intensity is " +
             "additionally scaled down by (distance / reference)² for hit distances below the reference.")]
    [SerializeField] private bool closeRangeCompensation = true;

    [Tooltip("Distance at which the flashlight brightness is considered 'correct'. Surfaces hit closer than " +
             "this receive quadratically less intensity so their illumination never exceeds the reference level.")]
    [SerializeField] [Min(0.3f)] private float closeRangeReferenceDistance = 1.5f;

    /// <summary>Lower bound for the close-range compensation so the light never fully blacks out
    /// at zero distance. Raised from 0.05: a point-blank wall was nearly invisible —
    /// 0.15 keeps it dimly lit without a Bloom flare.</summary>
    private const float MinCompensation = 0.15f;

    /// <summary>How much faster the flashlight dims than brightens (see Update()).</summary>
    private const float DimSpeedMultiplier = 4f;

    /// <summary>Small vertical spread (degrees) of the outer fan probes so the floor is sampled
    /// without a separate probe set.</summary>
    private const float ProbeVerticalSpreadDegrees = 5f;

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

    private float _webDimScale = 1f; // external dimming (e.g. spider web touch), multiplies final intensity
    private Coroutine _webDimRoutine;

    [Header("Hard Off Restore")]
    [Tooltip("Seconds between the hard-off source disappearing (e.g. the horror mannequin) " +
             "and the flashlight flickering back on.")]
    [SerializeField] private float _hardOffRestoreDelay = 2f;

    [Tooltip("Total duration of the flicker sequence after the restore delay.")]
    [SerializeField] private float _hardOffFlickerDuration = 1.2f;

    [Tooltip("Number of on/off blinks during the flicker sequence.")]
    [SerializeField] [Min(1)] private int _hardOffFlickerCount = 4;

    [Tooltip("Fraction of full brightness during the blinks (0-1).")]
    [SerializeField] [Range(0.05f, 1f)] private float _hardOffFlickerBrightness = 0.6f;

    private bool _flickering;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "flashlight";

    /// <summary>Serializes whether the flashlight is currently on.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new FlashlightSaveData { isOn = _isOn });

    /// <summary>Stores pending state. Applied in Start() after Awake initialization.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<FlashlightSaveData>(json);
        _pendingIsOn = data.isOn;
    }

    [Serializable]
    private struct FlashlightSaveData
    {
        public bool isOn;
    }

    private bool? _pendingIsOn;
    // True while applying loaded state in Start() — suppresses the redundant
    // Save() that SetState() would otherwise trigger right after loading.
    private bool _restoringState;

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

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Route to the SFX bus once — deferred to Start so the AudioManager
        // singleton is guaranteed to exist (Awake order is not).
        if (_audioSource != null && _audioSource.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_audioSource);

        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;
            DetectAndApplyMode();
        }

        // Apply loaded on/off state. This is a snapshot restore, not a gameplay
        // toggle — no sound, and no re-save (state was already persisted).
        if (_pendingIsOn.HasValue)
        {
            bool on = _pendingIsOn.Value;
            _pendingIsOn = null;
            // Only restore ON if the operating condition is still met
            // (e.g. the charged battery was consumed before saving — the
            // flashlight must never force itself on without power).
            if (on && config != null && config.operatingCondition.IsMet())
            {
                _restoringState = true;
                SetState(true);
                _restoringState = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (Keyboard.current.fKey.wasPressedThisFrame)
            TryToggle();

        if (_flickering) return; // flicker coroutine owns intensity right now

        UpdateProximityScale();

        float targetIntensity = _targetIntensity * _proximityScale * _webDimScale;
        // Dimming must react as fast as the close-range compensation drop does —
        // otherwise a fast approach to a wall shows full brightness first and only
        // then fades down. Brightening keeps the normal (slower) transition speed.
        float speed = _light.intensity > targetIntensity
            ? config.transitionSpeed * DimSpeedMultiplier
            : config.transitionSpeed;
        _light.intensity = Mathf.MoveTowards(
            _light.intensity,
            targetIntensity,
            speed * Time.deltaTime
        );
    }

    private const int ProbeCount = 6;
    private const int ProbeMajority = 3;

    /// <summary>
    /// Single unified proximity system: casts one fan of thin rays from the camera and
    /// produces a combined dim factor that covers BOTH former systems — the majority-rule
    /// proximity dimming and the inverse-square close-range compensation.
    ///
    /// Fan layout: center probe + four probes at ±probeFanHalfAngle horizontally and
    /// ±ProbeVerticalSpreadDegrees vertically, so walls straight ahead AND the floor below
    /// the beam are sampled by one ray set.
    ///
    /// Dim factor per hit = majority dim curve × inverse-square compensation. The product is
    /// intentional: illuminance grows like 1/d², so point-blank surfaces need compensation ON
    /// TOP of the proximity floor (proximityMinScale) or they blow out with Bloom flare.
    /// The single SmoothDamp below (instead of the former instant compensation) is what
    /// prevents the two effects from pumping against each other.
    ///
    /// Objects on probeIgnoreMask (e.g. the Draggable layer) are never treated as walls.
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
        float dimFactor = 1f; // darkest single-hit dim across the fan

        for (int i = 0; i < ProbeCount; i++)
        {
            Vector3 dir = fwd;
            if (i == 1) dir = Quaternion.AngleAxis( probeFanHalfAngle, up) * fwd;
            if (i == 2) dir = Quaternion.AngleAxis(-probeFanHalfAngle, up) * fwd;
            if (i == 3) dir = Quaternion.AngleAxis( probeFanHalfAngle, right) * fwd;
            if (i == 4) dir = Quaternion.AngleAxis(-probeFanHalfAngle, right) * fwd;
            // Slight vertical spread so the floor and low props are caught without a full cone.
            if (i == 5) dir = Quaternion.AngleAxis(-ProbeVerticalSpreadDegrees, right) * fwd;

            if (!Physics.Raycast(origin, dir, out RaycastHit hit, closeRangeReferenceDistance,
                                 proximityMask & ~probeIgnoreMask, QueryTriggerInteraction.Ignore))
                continue;

            hits++;

            // Majority dim curve: distance normalized by the far proximity range.
            float t = Mathf.Clamp01(hit.distance / proximityRange);
            float majorityDim = Mathf.Lerp(proximityMinScale, 1f, Mathf.Pow(t, proximityFalloff));

            // Inverse-square compensation: saturate((d/ref)²) — caps blow-out on point-blank walls.
            // Applied ON TOP of the majority curve (product): illuminance grows like 1/d², so the
            // proximity floor alone (proximityMinScale) cannot keep a point-blank surface from
            // flaring. Skipped entirely when closeRangeCompensation is disabled in the Inspector.
            float closeCompDim = 1f;
            if (closeRangeCompensation)
            {
                float d = Mathf.Max(hit.distance, 0.05f);
                float rt = (d / closeRangeReferenceDistance) * (d / closeRangeReferenceDistance);
                closeCompDim = Mathf.Max(Mathf.Clamp01(rt), MinCompensation);
            }

            // Product: both curves apply — compensation must sit ON TOP of the proximity
            // floor or a point-blank wall flares (illuminance ~ 1/d²).
            dimFactor = Mathf.Min(dimFactor, majorityDim * closeCompDim);
        }

        // Majority rule still gates the dimming: fewer hits than the threshold → no dim at all,
        // which prevents false triggers from glancing hits on stairs, pillars, and side walls.
        float targetScale = hits >= ProbeMajority ? dimFactor : 1f;

        _proximityScale = Mathf.SmoothDamp(
            _proximityScale,
            targetScale,
            ref _proximityScaleVelocity,
            1f / proximitySmoothSpeed
        );
    }

    /// <summary>
    /// Temporarily dims the flashlight (e.g. when the player walks through a spider web).
    /// Intensity is multiplied by dimScale for duration seconds, then fades back to full brightness.
    /// Repeated calls restart the effect — the dimmest value wins while it lasts.
    /// </summary>
    public void DimTemporarily(float dimScale, float duration)
    {
        dimScale = Mathf.Clamp01(dimScale);
        if (dimScale >= 1f || duration <= 0f) return;

        // Take the darker of the current scale and the new one so stacked touches don't brighten the light.
        _webDimScale = Mathf.Min(_webDimScale, dimScale);

        if (_webDimRoutine != null) StopCoroutine(_webDimRoutine);
        _webDimRoutine = StartCoroutine(WebDimRoutine(dimScale, duration));
    }

    /// <summary>Holds dimScale while active, then smoothly restores full brightness.</summary>
    private IEnumerator WebDimRoutine(float dimScale, float duration)
    {
        yield return new WaitForSeconds(duration);

        // Fade the dimming factor back to 1 for a smooth recovery.
        while (_webDimScale < 1f)
        {
            _webDimScale = Mathf.MoveTowards(_webDimScale, 1f, Time.deltaTime / Mathf.Max(duration * 0.5f, 0.1f));
            yield return null;
        }

        _webDimScale = 1f;
        _webDimRoutine = null;
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

    private bool _wasOnBeforeForcedOff;

    /// <summary>
    /// Forces the flashlight off and remembers whether it was on, so that
    /// RestoreAfterForcedOff() can bring back the previous state. Used by horror events.
    /// The light fades out over transitionSpeed — use ForceOffHard() for an instant blackout.
    /// </summary>
    public void ForceOffTemporarily()
    {
        _wasOnBeforeForcedOff = _isOn;
        SetState(false);
    }

    /// <summary>
    /// Hard blackout: the light cuts to zero instantly (no smooth fade), remembering
    /// whether it was on for RestoreAfterForcedOff(). Used by horror events.
    /// </summary>
    public void ForceOffHard()
    {
        _wasOnBeforeForcedOff = _isOn;
        SetState(false);
        _light.intensity = 0f;
    }

    /// <summary>
    /// Restores the flashlight to the state it had before ForceOffTemporarily()/ForceOffHard().
    /// Waits a configured delay, then flickers a few times before coming on for real.
    /// Does nothing if the flashlight was off back then.
    /// </summary>
    public void RestoreAfterForcedOff()
    {
        if (!_wasOnBeforeForcedOff || _flickering) return;
        StartCoroutine(FlickerRestoreRoutine());
    }

    /// <summary>Delay → series of blinks → smooth power-on. Flicker holds Update() off so it can drive intensity directly.</summary>
    private IEnumerator FlickerRestoreRoutine()
    {
        _flickering = true;

        yield return new WaitForSeconds(_hardOffRestoreDelay);

        float blinkTime = _hardOffFlickerDuration / (_hardOffFlickerCount * 2f);
        for (int i = 0; i < _hardOffFlickerCount; i++)
        {
            SetFlickerLight(true);
            yield return new WaitForSeconds(blinkTime);
            SetFlickerLight(false);
            yield return new WaitForSeconds(blinkTime);
        }

        _flickering = false;
        ForceOn();
    }

    /// <summary>Directly sets light intensity for one blink (Update() is paused while _flickering).</summary>
    private void SetFlickerLight(bool on)
    {
        if (on && GetCurrentModeConfig() is { } modeConfig)
            _light.intensity = modeConfig.onState.intensity * _hardOffFlickerBrightness;
        else
            _light.intensity = 0f;
    }

    private void SetState(bool on)
    {
        bool wasOn = _isOn;
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

        // Checkpoint-only saving: flashlight state is minor — captured by the next
        // checkpoint (puzzle/item/key), pause save or quiet autosave.
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
