using UnityEngine;

/// <summary>
/// Controls the portal funnel visual: animates shader scroll speed,
/// manages opacity fade-in/out, spins the mesh, and drives particles that
/// travel exactly along the funnel silhouette (positions computed in script).
/// Wire Activate() to the final-door puzzle solved event or a trigger.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class PortalFunnelController : MonoBehaviour
{
    private const string ScrollSpeedProp = "_ScrollSpeed";
    private const string OpacityProp = "_Opacity";
    private const string TimeOffsetProp = "_TimeOffset";

    private const float FadeDuration = 3f;

    [Header("Animation")]
    [Tooltip("Activate the portal automatically on Start (for testing without puzzle integration).")]
    [SerializeField] private bool _activateOnStart = true;
    [SerializeField] private float _idleScrollSpeed = 0.3f;
    [SerializeField] private float _activeScrollSpeed = 0.9f;
    [SerializeField] private float _speedLerp = 1.5f;

    [Header("Mesh Rotation")]
    [Tooltip("Visual mesh spin around the funnel axis (degrees/sec).")]
    [SerializeField] private float _meshSpinSpeed = 45f;

    [Header("Particles - Shape")]
    [Tooltip("Number of visible particles.")]
    [SerializeField] private int _particleCount = 350;
    [Tooltip("Radius of the emission ring at the wide end of the funnel.")]
    [SerializeField] private float _particleStartRadius = 2.8f;
    [Tooltip("Radius particles converge to at the narrow end.")]
    [SerializeField] private float _particleEndRadius = 0.1f;
    [Tooltip("How far along the funnel axis particles travel (funnel height is 7).")]
    [SerializeField] private float _particleHeight = 6.5f;
    [Tooltip("How many spiral turns particles make over their full path.")]
    [SerializeField] private float _particleTurns = 4f;
    [Tooltip("Extra swirl of the whole particle pattern around the axis (revolutions/sec).")]
    [SerializeField] private float _particleOrbitSpeed = 0.2f;
    [Tooltip("Base particle size (meters).")]
    [SerializeField] private float _particleSize = 0.07f;

    [Header("Particles - Motion")]
    [Tooltip("Time for a particle to travel the full path (sec).")]
    [SerializeField] private float _particleLifetime = 3.5f;

    [Header("Particles - Look")]
    [SerializeField] private Gradient _particleColor;
    [SerializeField] private Material _particleMaterial;

    [Header("Light")]
    [SerializeField] private Light _portalLight;
    [SerializeField] private float _activeLightIntensity = 2f;

    private const float RadiusJitter = 0.08f; // random radius variation per particle

    private MaterialPropertyBlock _mpb;
    private MeshRenderer _renderer;
    private ParticleSystem _particleSystem;
    private ParticleSystem.Particle[] _particleBuffer;

    private float _currentScrollSpeed;
    private float _currentOpacity;
    private float _timeOffset;
    private float _targetOpacity;
    private float _targetScrollSpeed;
    private bool _isActive;

    public bool IsActive => _isActive;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();

        if (_particleColor == null)
            BuildDefaultGradient();

        CreateParticleSystem();

        _currentScrollSpeed = _idleScrollSpeed;
        _currentOpacity = 0f;
        _targetOpacity = 0f;
        ApplyMaterialProperties();

        if (_portalLight != null)
            _portalLight.intensity = 0f;

        if (_activateOnStart)
            Activate();
    }

    private void BuildDefaultGradient()
    {
        _particleColor = new Gradient();
        var colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.3f, 0.6f, 1f), 0f),
            new GradientColorKey(new Color(0.8f, 0.9f, 1f), 0.5f),
            new GradientColorKey(new Color(1f, 0.95f, 0.8f), 1f),
        };
        var alphaKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(1f, 0.15f),
            new GradientAlphaKey(0.8f, 0.6f),
            new GradientAlphaKey(0f, 1f),
        };
        _particleColor.SetKeys(colorKeys, alphaKeys);
    }

    /// <summary>
    /// Activates the portal: ramps up opacity, scroll speed, particles, and light.
    /// </summary>
    public void Activate()
    {
        _isActive = true;
        _targetOpacity = 1f;
        _targetScrollSpeed = _activeScrollSpeed;
    }

    /// <summary>
    /// Deactivates the portal: fades everything out.
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        _targetOpacity = 0f;
        _targetScrollSpeed = _idleScrollSpeed;
    }

    private void Update()
    {
        // Continuous spin of the funnel mesh around its local Y axis
        transform.Rotate(0f, _meshSpinSpeed * Time.deltaTime, 0f, Space.Self);

        _currentScrollSpeed = Mathf.Lerp(_currentScrollSpeed, _targetScrollSpeed, Time.deltaTime * _speedLerp);
        _currentOpacity = Mathf.Lerp(_currentOpacity, _targetOpacity, Time.deltaTime / FadeDuration);
        ApplyMaterialProperties();

        UpdateParticles();

        if (_portalLight != null)
        {
            float targetLight = _isActive ? _activeLightIntensity : 0f;
            _portalLight.intensity = Mathf.Lerp(_portalLight.intensity, targetLight,
                Time.deltaTime / FadeDuration);
        }
    }

    /// <summary>
    /// Sets the shader time offset, used by PortalFunnelSpawner to desync wave patterns.
    /// </summary>
    public void SetTimeOffset(float offset)
    {
        _timeOffset = offset;
        ApplyMaterialProperties();
    }

    private void ApplyMaterialProperties()
    {
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(ScrollSpeedProp, _currentScrollSpeed);
        _mpb.SetFloat(OpacityProp, _currentOpacity);
        _mpb.SetFloat(TimeOffsetProp, _timeOffset);
        _renderer.SetPropertyBlock(_mpb);
    }

    private void CreateParticleSystem()
    {
        var psObj = new GameObject("PortalParticles");
        psObj.transform.SetParent(transform, false);
        psObj.transform.localPosition = Vector3.zero;

        _particleSystem = psObj.AddComponent<ParticleSystem>();
        var main = _particleSystem.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = _particleLifetime;
        main.startSpeed = 0f;
        main.startSize = _particleSize;
        main.maxParticles = _particleCount;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startColor = Color.white;
        _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _particleSystem.useAutoRandomSeed = false;
        _particleSystem.randomSeed = 12345; // stable seed so per-particle spiral parameters don't jump

        // Continuous spawning along the wide rim
        var emission = _particleSystem.emission;
        emission.rateOverTime = _particleCount / _particleLifetime;

        // Emission ring at the wide end, oriented across the funnel axis
        var shape = _particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = _particleStartRadius;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.position = Vector3.zero;

        // Color and fade handled natively so freshly spawned particles are never wrong
        var colorOverLifetime = _particleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(_particleColor);

        // Shrink toward the throat
        var sizeOverLifetime = _particleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var shrinkCurve = new AnimationCurve();
        shrinkCurve.AddKey(0f, 1f);
        shrinkCurve.AddKey(1f, 0.3f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, shrinkCurve);

        _particleSystem.Play();

        var renderer = _particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = -5f;

        if (_particleMaterial != null)
        {
            renderer.material = _particleMaterial;
        }
        else
        {
            // Create a simple additive material if none assigned
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 1f); // Additive
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 30;
            renderer.material = mat;
        }

        _particleBuffer = new ParticleSystem.Particle[_particleCount];
    }

    private void UpdateParticles()
    {
        if (_particleSystem == null || _particleBuffer == null)
            return;

        int count = _particleSystem.GetParticles(_particleBuffer);
        float swirl = Time.time * _particleOrbitSpeed * Mathf.PI * 2f;

        for (int i = 0; i < count; i++)
        {
            var particle = _particleBuffer[i];

            // Progress along the funnel path derived from the particle's own age
            float t = Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);

            // Stable per-particle spiral parameters from the seed
            uint seed = particle.randomSeed;
            float phase = (seed % 1024u) / 1024f * Mathf.PI * 2f;
            float jitter = 1f + (((seed / 1024u) % 128u) / 128f * 2f - 1f) * RadiusJitter;

            float angle = phase + t * _particleTurns * Mathf.PI * 2f + swirl;
            float radius = Mathf.Lerp(_particleStartRadius, _particleEndRadius, t) * jitter;

            particle.position = new Vector3(
                Mathf.Cos(angle) * radius,
                t * _particleHeight,
                Mathf.Sin(angle) * radius);

            // Size and color are driven by ColorOverLifetime / SizeOverLifetime modules,
            // so freshly spawned particles are always correct — only the position is script-driven.
            _particleBuffer[i] = particle;
        }

        _particleSystem.SetParticles(_particleBuffer, count);
    }

    private void OnValidate()
    {
        _particleCount = Mathf.Max(1, _particleCount);

        // Live-apply particle settings when tweaked in the Inspector during Play Mode
        if (_particleSystem != null && Application.isPlaying)
        {
            var main = _particleSystem.main;
            main.maxParticles = _particleCount;

            var emission = _particleSystem.emission;
            emission.rateOverTime = _particleCount / _particleLifetime;
        }
    }
}
