using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Bezi;

/// <summary>
/// Custom Bezi action that configures a native ParticleSystem for
/// "fast launch → decelerate → stop" dust behaviour without any runtime script.
/// </summary>
public static class DustParticleConfigurator
{
    private const float DEFAULT_SPEED_MIN = 1.5f;
    private const float DEFAULT_SPEED_MAX = 3f;
    private const float DEFAULT_DRAG = 0.15f;
    private const float DEFAULT_STOP_SPEED = 0f;
    private const float DEFAULT_LIFETIME_MIN = 5f;
    private const float DEFAULT_LIFETIME_MAX = 10f;
    private const float DEFAULT_SIZE_MIN = 0.04f;
    private const float DEFAULT_SIZE_MAX = 0.12f;
    private const float DEFAULT_EMISSION_RATE = 40f;
    private const int DEFAULT_MAX_PARTICLES = 300;

    [BeziAction(
        "Configures a native ParticleSystem on the given GameObject for fast-launch-decelerate-stop dust particles. " +
        "Removes any DustEmitter script if present, leaving a pure ParticleSystem.",
        IsReadOnly = false
    )]
    public static string ConfigureDustParticles(
        string gameObjectPath,
        float speedMin = DEFAULT_SPEED_MIN,
        float speedMax = DEFAULT_SPEED_MAX,
        float drag = DEFAULT_DRAG,
        float stopSpeed = DEFAULT_STOP_SPEED,
        float lifetimeMin = DEFAULT_LIFETIME_MIN,
        float lifetimeMax = DEFAULT_LIFETIME_MAX,
        float sizeMin = DEFAULT_SIZE_MIN,
        float sizeMax = DEFAULT_SIZE_MAX,
        float emissionRate = DEFAULT_EMISSION_RATE
    )
    {
        var go = GameObject.Find(gameObjectPath);
        if (go == null)
            return $"GameObject not found at path: {gameObjectPath}";

        // Remove DustEmitter script if present
        var dustEmitter = go.GetComponent<DustEmitter>();
        if (dustEmitter != null)
        {
            Object.DestroyImmediate(dustEmitter, true);
        }

        // Ensure a ParticleSystem exists
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null)
            ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.maxParticles = DEFAULT_MAX_PARTICLES;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = emissionRate;

        // Shape: box volume
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(4f, 2f, 4f);

        // Limit velocity over lifetime: drag-based deceleration from launch speed to stop
        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.space = ParticleSystemSimulationSpace.World;
        limitVel.limit = stopSpeed;
        limitVel.dampen = drag;
        limitVel.separateAxes = false;

        // Color over lifetime: fade in and out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var dustColor = new Color(0.95f, 0.9f, 0.8f, 0.8f);
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(dustColor, 0f),
                new GradientColorKey(dustColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Size over lifetime: slight swell
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.2f);
        sizeCurve.AddKey(0.3f, 1f);
        sizeCurve.AddKey(0.7f, 1f);
        sizeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Noise: organic turbulence
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;
        noise.damping = true;
        noise.octaveCount = 2;

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);

        return $"Configured ParticleSystem on '{gameObjectPath}': speed {speedMin}-{speedMax}, drag {drag}, stopSpeed {stopSpeed}";
    }
}
