using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Finale layer of the portal: a blazing HDR core with god-rays and an event-horizon ring,
/// a gravitational lens that bends the room around the throat, a shockwave ring, a warm
/// heartbeat light, and the cinematic activation sequence (charge, flicker, flash, unfold).
/// Lives on the same object as <see cref="PortalFunnelController"/> (the funnel mesh) and is
/// triggered by <see cref="PortalFunnelController.Activate"/>.
/// Other finale components (<see cref="PortalEnergyParticles"/>, <see cref="PortalPlayerImpact"/>)
/// read <see cref="Power"/> and subscribe to <see cref="ImpactOccurred"/>.
/// </summary>
public class PortalFinaleEffects : MonoBehaviour
{
    private const string GlowIntensityProp = "_GlowIntensity";
    private const string RayIntensityProp = "_RayIntensity";
    private const string RingIntensityProp = "_RingIntensity";
    private const string RingRadiusProp = "_RingRadius";
    private const string RingWidthProp = "_RingWidth";
    private const string OpacityProp = "_Opacity";
    private const string StrengthProp = "_Strength";
    private const string HorizonProp = "_Horizon";
    private const string HoleRadiusProp = "_HoleRadius";

    private const float HeartbeatRate = 0.85f;           // beats per second
    private const float LightFlickerMax = 0.35f;        // flicker noise amplitude while active
    private const float CoreLightRange = 9f;
    private const float PowerRiseSpeed = 0.6f;
    private const float PowerFallSpeed = 0.5f;
    private const float ShockwaveSize = 26f;            // meters, quad edge length
    private const float QuadHalfExtent = 0.5f;

    [Header("References")]
    [SerializeField] private Material _coreMaterial;
    [SerializeField] private Material _lensMaterial;

    [Header("Core")]
    [Tooltip("Position of the throat core along the funnel axis, in funnel-local units (funnel height is 7).")]
    [SerializeField] private float _coreLocalHeight = 6.8f;
    [Tooltip("World size of the core quad (meters).")]
    [SerializeField] private float _coreSize = 4.5f;
    [SerializeField] private float _idleGlow = 1.0f;
    [SerializeField] private float _idleRays = 1.1f;
    [SerializeField] private float _ringIntensity = 1.6f;
    [SerializeField, Range(0.05f, 0.5f)] private float _ringRadius = 0.16f;
    [Tooltip("Strength of the glow/ray/ring heartbeat pulse (0 = none).")]
    [SerializeField, Range(0f, 1f)] private float _heartbeatAmount = 0.35f;

    [Header("Accretion Disk")]
    [SerializeField] private Material _diskMaterial;
    [Tooltip("World size of the accretion disk quad (meters).")]
    [SerializeField] private float _diskSize = 8f;
    [Tooltip("Inner edge of the glowing disk (meters from the center). Must be larger than the horizon radius.")]
    [SerializeField] private float _diskInnerRadius = 0.42f;
    [Tooltip("Outer edge of the glowing disk (meters from the center).")]
    [SerializeField] private float _diskOuterRadius = 2.6f;
    [SerializeField] private float _diskIntensity = 2f;

    [Header("Black Hole Lens")]
    [Tooltip("World size of the gravitational lens quad (meters). Larger = wider distortion field.")]
    [SerializeField] private float _lensSize = 9f;
    [Tooltip("Deflection strength multiplier (Einstein-ring profile).")]
    [SerializeField, Range(0f, 3f)] private float _lensStrength = 1f;
    [Tooltip("World radius of the pitch-black event horizon in the center (meters).")]
    [SerializeField] private float _horizonRadius = 0.26f;

    [Header("Warm Core Light")]
    [SerializeField] private Color _coreLightColor = new Color(1f, 0.72f, 0.4f);
    [SerializeField] private float _coreLightIntensity = 6f;

    [Header("Activation Sequence (sec)")]
    [Tooltip("Dark build-up: flickering lights and a growing spark before the flash.")]
    [SerializeField] private float _chargeDuration = 2.2f;
    [Tooltip("Time for the funnel to unfold from the throat to the rim after the flash.")]
    [SerializeField] private float _revealDuration = 3.2f;
    [SerializeField] private float _flashDuration = 0.9f;
    [SerializeField] private float _flashPeakGlow = 14f;
    [SerializeField] private float _shockwaveDuration = 1.8f;

    private MaterialPropertyBlock _coreBlock;
    private MaterialPropertyBlock _shockBlock;
    private MaterialPropertyBlock _lensBlock;
    private MaterialPropertyBlock _diskBlock;
    private Renderer _diskRenderer;
    private Renderer _coreRenderer;
    private Renderer _shockRenderer;
    private Renderer _lensRenderer;
    private Transform _coreRoot;
    private Light _coreLight;
    private PortalFunnelController _controller;
    private Coroutine _sequence;
    private bool _built;

    // Sequence-driven state
    private float _targetPower;
    private float _power;
    private float _flash;                // 0..1 flash envelope
    private float _sparkGrowth;          // 0..1 pre-flash spark size
    private float _shockProgress = -1f;  // <0 = inactive
    private float _flicker = 1f;
    private float _coreFade;             // 0..1 overall core visibility

    /// <summary>Raised at the moment of the activation flash (camera shake, FOV punch, etc).</summary>
    public event Action ImpactOccurred;

    /// <summary>Overall intensity of the finale (0 = off, 1 = fully active). Smoothed.</summary>
    public float Power => _power;

    /// <summary>Flash envelope (0..1) for screen-space effects.</summary>
    public float Flash => _flash;

    /// <summary>True while the activation sequence coroutine is running.</summary>
    public bool IsPlayingSequence => _sequence != null;

    /// <summary>Multiplier the funnel controller applies to the main portal light (heartbeat + flicker).</summary>
    public float LightMultiplier { get; private set; } = 1f;

    /// <summary>World position of the throat core.</summary>
    public Vector3 CoreWorldPosition
    {
        get
        {
            EnsureBuilt();
            return _coreRoot.position;
        }
    }

    /// <summary>Funnel axis direction in world space (from the wide mouth toward the throat).</summary>
    public Vector3 AxisWorld => transform.up;

    /// <summary>
    /// Starts the cinematic activation: flicker, spark, flash, shockwave and funnel unfold.
    /// </summary>
    public void PlayActivation(PortalFunnelController controller)
    {
        EnsureBuilt();
        _controller = controller;

        if (_sequence != null)
            StopCoroutine(_sequence);

        _sequence = StartCoroutine(ActivationRoutine());
    }

    /// <summary>
    /// Fades the finale out (portal deactivation).
    /// </summary>
    public void Stop()
    {
        if (_sequence != null)
        {
            StopCoroutine(_sequence);
            _sequence = null;
        }

        _targetPower = 0f;
        _sparkGrowth = 0f;
        _flash = 0f;
        if (_controller != null)
            _controller.SetReveal(0f);
    }

    private void Awake()
    {
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (_built)
            return;
        _built = true;

        Mesh quad = BuildQuadMesh();

        _coreRoot = new GameObject("PortalCore").transform;
        _coreRoot.SetParent(transform, false);
        _coreRoot.localPosition = new Vector3(0f, _coreLocalHeight, 0f);
        SetWorldScale(_coreRoot, _coreSize);
        _coreRenderer = AddQuadRenderer(_coreRoot.gameObject, quad, _coreMaterial);
        _coreBlock = new MaterialPropertyBlock();

        var lensObject = new GameObject("PortalLens");
        lensObject.transform.SetParent(_coreRoot, false);
        lensObject.transform.localPosition = Vector3.zero;
        SetWorldScale(lensObject.transform, _lensSize);
        _lensRenderer = AddQuadRenderer(lensObject, quad, _lensMaterial);
        _lensBlock = new MaterialPropertyBlock();

        var diskObject = new GameObject("PortalAccretionDisk");
        diskObject.transform.SetParent(_coreRoot, false);
        diskObject.transform.localPosition = Vector3.zero;
        SetWorldScale(diskObject.transform, _diskSize);
        _diskRenderer = AddQuadRenderer(diskObject, quad, _diskMaterial);
        _diskBlock = new MaterialPropertyBlock();

        var shockObject = new GameObject("PortalShockwave");
        shockObject.transform.SetParent(_coreRoot, false);
        shockObject.transform.localPosition = Vector3.zero;
        SetWorldScale(shockObject.transform, ShockwaveSize);
        _shockRenderer = AddQuadRenderer(shockObject, quad, _coreMaterial);
        _shockBlock = new MaterialPropertyBlock();

        var lightObject = new GameObject("PortalCoreLight");
        lightObject.transform.SetParent(_coreRoot, false);
        _coreLight = lightObject.AddComponent<Light>();
        _coreLight.type = LightType.Point;
        _coreLight.color = _coreLightColor;
        _coreLight.range = CoreLightRange;
        _coreLight.shadows = LightShadows.None;
        _coreLight.intensity = 0f;

        ApplyVisuals();
    }

    private static void SetWorldScale(Transform target, float worldSize)
    {
        // Keep a fixed world size regardless of the (non-uniform) parent scale
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            worldSize / Mathf.Max(parentScale.x, 0.0001f),
            worldSize / Mathf.Max(parentScale.y, 0.0001f),
            worldSize / Mathf.Max(parentScale.z, 0.0001f));
    }

    private static Renderer AddQuadRenderer(GameObject target, Mesh quad, Material material)
    {
        target.AddComponent<MeshFilter>().sharedMesh = quad;
        var renderer = target.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "PortalFinaleQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-QuadHalfExtent, -QuadHalfExtent, 0f),
            new Vector3(QuadHalfExtent, -QuadHalfExtent, 0f),
            new Vector3(-QuadHalfExtent, QuadHalfExtent, 0f),
            new Vector3(QuadHalfExtent, QuadHalfExtent, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        // Billboard happens in the shader, so generous bounds avoid culling
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
        return mesh;
    }

    private IEnumerator ActivationRoutine()
    {
        _coreFade = 1f;
        _targetPower = 0.25f;
        _sparkGrowth = 0f;
        _flash = 0f;
        _shockProgress = -1f;
        if (_controller != null)
            _controller.SetReveal(0f);

        // Phase 1: charge — the spark grows while lights stutter
        float timer = 0f;
        while (timer < _chargeDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / _chargeDuration);
            _sparkGrowth = t * t;
            // Flicker gets denser and brighter toward the flash
            float flickerRate = Mathf.Lerp(6f, 28f, t);
            float noise = Mathf.PerlinNoise(Time.time * flickerRate, 0.37f);
            _flicker = noise > Mathf.Lerp(0.7f, 0.35f, t) ? 0.05f : Mathf.Lerp(0.2f, 1f, t);
            yield return null;
        }

        // Phase 2: flash + shockwave + impact
        _flicker = 1f;
        _targetPower = 1f;
        _shockProgress = 0f;
        ImpactOccurred?.Invoke();

        float flashTimer = 0f;
        float revealTimer = 0f;
        float total = Mathf.Max(_revealDuration, _shockwaveDuration, _flashDuration);
        float elapsed = 0f;
        while (elapsed < total)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            flashTimer += dt;
            revealTimer += dt;

            // Sharp attack, long smooth decay
            float f = Mathf.Clamp01(flashTimer / _flashDuration);
            _flash = f < 0.08f ? f / 0.08f : Mathf.Pow(1f - (f - 0.08f) / 0.92f, 2f);

            _shockProgress = Mathf.Clamp01(elapsed / _shockwaveDuration);

            // Ease-out so the visible part of the unfold starts immediately
            float r = Mathf.Clamp01(revealTimer / _revealDuration);
            float easedReveal = 1f - Mathf.Pow(1f - r, 2.2f);
            if (_controller != null)
                _controller.SetReveal(easedReveal);

            yield return null;
        }

        _flash = 0f;
        _shockProgress = -1f;
        if (_controller != null)
            _controller.SetReveal(1f);
        _sequence = null;
    }

    private void Update()
    {
        // Smooth power toward its target (sequence sets the target)
        float speed = _targetPower > _power ? PowerRiseSpeed : PowerFallSpeed;
        _power = Mathf.MoveTowards(_power, _targetPower, speed * Time.deltaTime);

        // Idle: keep the core running at full power once the sequence is done
        if (_sequence == null && _targetPower > 0f)
            _targetPower = 1f;

        UpdateHeartbeatAndLight();
        ApplyVisuals();
    }

    private float _heartbeat;

    private void UpdateHeartbeatAndLight()
    {
        // Double-thump heartbeat envelope
        float phase = Mathf.Repeat(Time.time * HeartbeatRate, 1f);
        float thump = Mathf.Exp(-9f * phase) + 0.6f * Mathf.Exp(-9f * Mathf.Abs(phase - 0.28f));
        _heartbeat = Mathf.Clamp01(thump);

        float noise = Mathf.PerlinNoise(Time.time * 3.1f, 1.7f) - 0.5f;
        float idle = 0.8f + 0.5f * _heartbeat * _heartbeatAmount * 2f + noise * LightFlickerMax * _power;

        // During the charge the stutter replaces the idle pulse
        bool charging = _sequence != null && _flash <= 0f && _shockProgress < 0f;
        LightMultiplier = charging ? _flicker : idle * Mathf.Lerp(1f, 1f + _flash * 1.5f, 1f);
    }

    private void ApplyVisuals()
    {
        if (!_built)
            return;

        bool visible = _coreFade > 0f && (_power > 0.001f || _sparkGrowth > 0.001f || _flash > 0.001f);
        _coreRenderer.enabled = visible;
        _lensRenderer.enabled = visible && _power > 0.05f;

        float pulse = 1f + _heartbeat * _heartbeatAmount;
        float charging = _sparkGrowth * 0.5f * _flicker;
        float glow = (_idleGlow * Mathf.SmoothStep(0f, 1f, _power) * pulse + charging) + _flash * _flashPeakGlow;
        float rays = _idleRays * Mathf.SmoothStep(0f, 1f, _power) * pulse + _flash * 4f + charging * 0.6f;
        float ring = _ringIntensity * Mathf.SmoothStep(0.4f, 1f, _power) * (0.8f + 0.2f * pulse);

        _coreRenderer.GetPropertyBlock(_coreBlock);
        _coreBlock.SetFloat(GlowIntensityProp, glow);
        _coreBlock.SetFloat(RayIntensityProp, rays);
        _coreBlock.SetFloat(RingIntensityProp, ring);
        _coreBlock.SetFloat(RingRadiusProp, _ringRadius * (1f + 0.04f * _heartbeat));
        _coreBlock.SetFloat(RingWidthProp, 0.028f);
        _coreBlock.SetFloat(OpacityProp, 1f);
        // The event horizon opens up with the portal and closes during the flash
        float horizonOpen = Mathf.SmoothStep(0f, 1f, _power) * (1f - Mathf.Clamp01(_flash * 1.5f));
        _coreBlock.SetFloat(HoleRadiusProp, _horizonRadius / (_coreSize * 0.5f) * horizonOpen);
        _coreRenderer.SetPropertyBlock(_coreBlock);

        // Accretion disk: ignites as the portal powers up
        _diskRenderer.enabled = visible && _power > 0.05f;
        if (_diskRenderer.enabled)
        {
            float half = _diskSize * 0.5f;
            _diskRenderer.GetPropertyBlock(_diskBlock);
            _diskBlock.SetFloat("_Horizon", _horizonRadius / half * Mathf.Max(horizonOpen, 0.2f));
            _diskBlock.SetFloat("_InnerRadius", _diskInnerRadius / half);
            _diskBlock.SetFloat("_OuterRadius", _diskOuterRadius / half);
            _diskBlock.SetFloat("_Intensity", _diskIntensity * (0.85f + 0.3f * _heartbeat * _heartbeatAmount));
            _diskBlock.SetFloat(OpacityProp, Mathf.SmoothStep(0.2f, 1f, _power) * (1f + _flash));
            _diskRenderer.SetPropertyBlock(_diskBlock);
        }

        // Shockwave: a thin ring racing outward and fading
        bool shockActive = _shockProgress >= 0f && _shockProgress < 1f;
        _shockRenderer.enabled = shockActive;
        if (shockActive)
        {
            float eased = 1f - Mathf.Pow(1f - _shockProgress, 3f);
            _shockRenderer.GetPropertyBlock(_shockBlock);
            _shockBlock.SetFloat(GlowIntensityProp, 0f);
            _shockBlock.SetFloat(RayIntensityProp, 0f);
            _shockBlock.SetFloat(RingIntensityProp, 6f * Mathf.Pow(1f - _shockProgress, 1.5f));
            _shockBlock.SetFloat(RingRadiusProp, Mathf.Lerp(0.04f, 0.92f, eased));
            _shockBlock.SetFloat(RingWidthProp, Mathf.Lerp(0.012f, 0.05f, eased));
            _shockBlock.SetFloat(OpacityProp, 1f);
            _shockBlock.SetFloat(HoleRadiusProp, 0f);
            _shockRenderer.SetPropertyBlock(_shockBlock);
        }

        if (_lensRenderer.enabled)
        {
            _lensRenderer.GetPropertyBlock(_lensBlock);
            // Lens tightens briefly on the flash for a "space bends" feel
            _lensBlock.SetFloat(StrengthProp, _lensStrength * Mathf.SmoothStep(0f, 1f, _power) * (1f + _flash * 1.2f));
            _lensBlock.SetFloat(HorizonProp, _horizonRadius / (_lensSize * 0.5f) * horizonOpen);
            _lensBlock.SetFloat(OpacityProp, Mathf.SmoothStep(0f, 1f, _power));
            _lensRenderer.SetPropertyBlock(_lensBlock);
        }

        _coreLight.intensity = _coreLightIntensity * Mathf.SmoothStep(0f, 1f, _power) * (0.7f + 0.5f * _heartbeat)
                             + _flash * _coreLightIntensity * 3f
                             + _sparkGrowth * 1.5f * _flicker;
    }
}
