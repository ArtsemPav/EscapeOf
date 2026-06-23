using UnityEngine;

/// <summary>
/// Configures a ParticleSystem to simulate ambient floating dust particles.
/// Randomly picks one of the provided sprites per particle via TextureSheetAnimation.
/// All particle system modules are set up procedurally on Awake.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class DustEmitter : MonoBehaviour
{
    [Header("Sprites")]
    [Tooltip("List of dust sprites to randomly assign to each particle.")]
    public Sprite[] _dustSprites;

    [Header("Emission")]
    [Tooltip("Number of dust particles emitted per second.")]
    public float _emissionRate = 40f;

    [Header("Particle Lifetime & Size")]
    [Tooltip("Minimum lifetime of a single particle in seconds.")]
    public float _lifetimeMin = 5f;
    [Tooltip("Maximum lifetime of a single particle in seconds.")]
    public float _lifetimeMax = 10f;
    [Tooltip("Minimum particle size.")]
    public float _sizeMin = 0.04f;
    [Tooltip("Maximum particle size.")]
    public float _sizeMax = 0.12f;

    [Header("Speed")]
    [Tooltip("Minimum initial particle speed.")]
    public float _speedMin = 0.01f;
    [Tooltip("Maximum initial particle speed.")]
    public float _speedMax = 0.05f;

    [Header("Spawn Volume")]
    [Tooltip("Half-extents of the box volume in which particles spawn.")]
    public Vector3 _boxSize = new Vector3(4f, 2f, 4f);

    [Header("Color")]
    [Tooltip("Base dust tint color.")]
    public Color _dustColor = new Color(0.95f, 0.9f, 0.8f, 0.8f);

    [Header("Turbulence")]
    [Tooltip("Strength of noise-based turbulence applied to particles.")]
    public float _noiseStrength = 0.08f;
    [Tooltip("Frequency of the noise field.")]
    public float _noiseFrequency = 0.3f;

    [Header("Rendering")]
    [Tooltip("URP Rendering Layer Mask to match room lighting.")]
    public uint _renderingLayerMask = 1;

    private ParticleSystem _ps;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        ConfigureParticleSystem();
    }

    public void ConfigureParticleSystem()
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps == null) return;

        // --- Main module ---
        var main = _ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetimeMin, _lifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(_speedMin, _speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(_sizeMin, _sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(_dustColor);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        // --- Emission ---
        var emission = _ps.emission;
        emission.enabled = true;
        emission.rateOverTime = _emissionRate;

        // --- Shape: box volume ---
        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = _boxSize;

        // --- Velocity over lifetime: subtle upward drift ---
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        vel.y = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

        // --- Color over lifetime: fade in and out ---
        var colorOverLifetime = _ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(_dustColor, 0f),
                new GradientColorKey(_dustColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // --- Size over lifetime: slight swell ---
        var sizeOverLifetime = _ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.2f);
        sizeCurve.AddKey(0.3f, 1f);
        sizeCurve.AddKey(0.7f, 1f);
        sizeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // --- Noise: organic turbulence ---
        var noise = _ps.noise;
        noise.enabled = true;
        noise.strength = _noiseStrength;
        noise.frequency = _noiseFrequency;
        noise.scrollSpeed = 0.2f;
        noise.damping = true;
        noise.octaveCount = 2;

        // --- Texture Sheet Animation: random sprite per particle ---
        ConfigureTextureSheetAnimation();

        // --- Renderer ---
        var psRenderer = _ps.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        psRenderer.sortingOrder = 0;
        psRenderer.renderingLayerMask = _renderingLayerMask;
    }

    private void ConfigureTextureSheetAnimation()
    {
        if (_dustSprites == null || _dustSprites.Length == 0)
            return;

        var tsa = _ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Sprites;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
        tsa.animation = ParticleSystemAnimationType.SingleRow;
        tsa.rowMode = ParticleSystemAnimationRowMode.Random;
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f);
        tsa.cycleCount = 1;

        // Clear all existing sprites before adding new ones
        for (int i = tsa.spriteCount - 1; i >= 0; i--)
            tsa.RemoveSprite(i);

        foreach (Sprite sprite in _dustSprites)
        {
            if (sprite != null)
                tsa.AddSprite(sprite);
        }
    }
}
