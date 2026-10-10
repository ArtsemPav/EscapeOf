using UnityEngine;

/// <summary>
/// Extra energy layers for the portal finale, all scaled by <see cref="PortalFinaleEffects.Power"/>:
/// stretched spiral sparks riding the funnel, room dust and floating debris that get sucked into
/// the throat, and flickering lightning arcs from the funnel surface to the core.
/// Lives on the same object as <see cref="PortalFinaleEffects"/> (the funnel mesh).
/// </summary>
[RequireComponent(typeof(PortalFinaleEffects))]
public class PortalEnergyParticles : MonoBehaviour
{
    private const float FunnelHeight = 7f;
    private const float FunnelMouthRadius = 2.9f;
    private const float FunnelThroatRadius = 0.15f;
    private const float PullKillDistance = 0.35f;
    private const int ArcPointCount = 14;
    private const float ArcWidth = 0.05f;
    private const float ArcRegenInterval = 0.045f;

    [Header("Materials")]
    [Tooltip("Additive soft-glow particle material (same as the funnel particles).")]
    [SerializeField] private Material _glowParticleMaterial;
    [SerializeField] private Material _debrisMaterial;
    [SerializeField] private Material _arcMaterial;

    [Header("Sparks (spiral up the funnel)")]
    [SerializeField] private int _maxSparks = 500;
    [SerializeField] private float _sparkRate = 120f;
    [SerializeField] private float _sparkMouthRadius = 2.5f;
    [SerializeField] private float _sparkTravelSpeed = 2.3f;
    [SerializeField] private float _sparkOrbitSpeed = 3.2f;
    [SerializeField] private float _sparkRadialSpeed = -0.7f;
    [SerializeField] private Vector2 _sparkSize = new Vector2(0.05f, 0.13f);
    [SerializeField, ColorUsage(true, true)] private Color _sparkColorA = new Color(0.5f, 1.2f, 3f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color _sparkColorB = new Color(3f, 1.6f, 0.8f, 1f);

    [Header("Dust (pulled in from the room)")]
    [SerializeField] private int _maxDust = 300;
    [SerializeField] private float _dustRate = 60f;
    [SerializeField] private float _dustSpawnRadius = 6f;
    [SerializeField] private float _dustPullSpeed = 1.1f;
    [SerializeField] private float _dustSwirl = 1.4f;

    [Header("Debris (floating, tumbling)")]
    [SerializeField] private int _maxDebris = 40;
    [SerializeField] private float _debrisRate = 5f;
    [SerializeField] private float _debrisPullSpeed = 0.6f;

    [Header("Lightning Arcs")]
    [SerializeField] private int _arcCount = 6;
    [SerializeField] private float _arcJaggedness = 0.4f;
    [SerializeField] private Vector2 _arcIdleTime = new Vector2(0.15f, 1.1f);
    [SerializeField] private Vector2 _arcLifeTime = new Vector2(0.12f, 0.32f);

    private PortalFinaleEffects _finale;
    private ParticleSystem _sparks;
    private ParticleSystem _dust;
    private ParticleSystem _debris;
    private ParticleSystem.Particle[] _dustBuffer;
    private ParticleSystem.Particle[] _debrisBuffer;
    private ArcState[] _arcs;

    private class ArcState
    {
        public LineRenderer Line;
        public float TimeToToggle;
        public float RegenTimer;
        public bool Visible;
    }

    private void Start()
    {
        _finale = GetComponent<PortalFinaleEffects>();

        BuildSparks();
        _dust = BuildWorldSystem("PortalDust", _maxDust, _glowParticleMaterial, new Vector2(0.03f, 0.07f), new Vector2(4f, 6f), false);
        _debris = BuildWorldSystem("PortalDebris", _maxDebris, _debrisMaterial, new Vector2(0.08f, 0.2f), new Vector2(5f, 8f), true);
        _dustBuffer = new ParticleSystem.Particle[_maxDust];
        _debrisBuffer = new ParticleSystem.Particle[_maxDebris];
        BuildArcs();
    }

    private void Update()
    {
        float power = _finale.Power;

        SetEmission(_sparks, _sparkRate * power);
        SetEmission(_dust, _dustRate * power);
        SetEmission(_debris, _debrisRate * power);

        PositionWorldEmitters();
        PullParticles(_dust, _dustBuffer, _dustPullSpeed, _dustSwirl, 1f);
        PullParticles(_debris, _debrisBuffer, _debrisPullSpeed, _dustSwirl * 0.6f, 0.5f);
        UpdateArcs(power);
    }

    private static void SetEmission(ParticleSystem system, float rate)
    {
        if (system == null)
            return;
        var emission = system.emission;
        emission.rateOverTime = rate;
    }

    // ------------------------------------------------------------------ sparks

    private void BuildSparks()
    {
        var obj = new GameObject("PortalSparks");
        obj.transform.SetParent(transform, false);
        _sparks = obj.AddComponent<ParticleSystem>();
        _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _sparks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(_sparkSize.x, _sparkSize.y);
        main.startColor = new ParticleSystem.MinMaxGradient(_sparkColorA, _sparkColorB);
        main.maxParticles = _maxSparks;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var shape = _sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = _sparkMouthRadius;
        shape.radiusThickness = 0.15f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        // Spiral up the funnel: travel along +Y, orbit around Y, converge radially
        var velocity = _sparks.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = 0f;
        velocity.y = _sparkTravelSpeed;
        velocity.z = 0f;
        velocity.orbitalX = 0f;
        velocity.orbitalY = _sparkOrbitSpeed;
        velocity.orbitalZ = 0f;
        velocity.radial = _sparkRadialSpeed;

        var color = _sparks.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var size = _sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.25f)));

        var renderer = _sparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 4f;
        renderer.velocityScale = 0.04f;
        renderer.cameraVelocityScale = 0f;
        renderer.sharedMaterial = _glowParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _sparks.Play();
    }

    // ------------------------------------------------------- dust and debris

    private ParticleSystem BuildWorldSystem(string objectName, int maxParticles, Material material,
        Vector2 sizeRange, Vector2 lifetimeRange, bool useMesh)
    {
        var obj = new GameObject(objectName);
        obj.transform.SetParent(transform, false);
        var system = obj.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = _dustSpawnRadius;
        shape.radiusThickness = 1f;

        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (useMesh)
        {
            // Tumbling paper-like shards
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            rotation.y = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = BuildDoubleSidedQuad();
        }

        system.Play();
        return system;
    }

    private static Mesh BuildDoubleSidedQuad()
    {
        var mesh = new Mesh { name = "PortalDebrisShard" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        // Front and back faces so shards are visible from both sides
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void PositionWorldEmitters()
    {
        // Spawn volume sits around the middle of the funnel, so material streams in from the room
        Vector3 mouth = transform.position;
        Vector3 core = _finale.CoreWorldPosition;
        Vector3 center = Vector3.Lerp(mouth, core, 0.35f);
        _dust.transform.position = center;
        _debris.transform.position = center;
    }

    /// <summary>Steers every live particle toward the throat core along a tightening spiral.</summary>
    private void PullParticles(ParticleSystem system, ParticleSystem.Particle[] buffer,
        float pullSpeed, float swirl, float lifetimeKillScale)
    {
        int count = system.GetParticles(buffer);
        if (count == 0)
            return;

        Vector3 target = _finale.CoreWorldPosition;
        Vector3 axis = _finale.AxisWorld;

        for (int i = 0; i < count; i++)
        {
            Vector3 toTarget = target - buffer[i].position;
            float distance = toTarget.magnitude;

            if (distance < PullKillDistance * lifetimeKillScale + PullKillDistance * 0.5f)
            {
                // Swallowed by the core
                buffer[i].remainingLifetime = 0f;
                continue;
            }

            Vector3 direction = toTarget / distance;
            Vector3 tangent = Vector3.Cross(axis, direction);
            // Accelerates as it approaches, spirals tighter near the throat
            float speed = pullSpeed * (1f + 4f / (distance + 1f));
            buffer[i].velocity = direction * speed + tangent * swirl * Mathf.Clamp01(1.5f / (distance + 0.5f));
        }

        system.SetParticles(buffer, count);
    }

    // ------------------------------------------------------------- lightning

    private void BuildArcs()
    {
        _arcs = new ArcState[_arcCount];
        for (int i = 0; i < _arcCount; i++)
        {
            var obj = new GameObject("PortalArc_" + i);
            obj.transform.SetParent(transform, false);
            var line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = ArcPointCount;
            line.widthMultiplier = ArcWidth;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.3f));
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = _arcMaterial;
            line.alignment = LineAlignment.View;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.8f, 1f), 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = gradient;
            line.enabled = false;

            _arcs[i] = new ArcState
            {
                Line = line,
                TimeToToggle = Random.Range(_arcIdleTime.x, _arcIdleTime.y)
            };
        }
    }

    private void UpdateArcs(float power)
    {
        if (_arcs == null)
            return;

        // More power = more frequent bursts
        float activity = Mathf.Clamp01(power);
        for (int i = 0; i < _arcs.Length; i++)
        {
            var arc = _arcs[i];

            if (activity < 0.05f)
            {
                arc.Line.enabled = false;
                arc.Visible = false;
                continue;
            }

            arc.TimeToToggle -= Time.deltaTime;
            if (arc.TimeToToggle <= 0f)
            {
                arc.Visible = !arc.Visible;
                arc.TimeToToggle = arc.Visible
                    ? Random.Range(_arcLifeTime.x, _arcLifeTime.y)
                    : Random.Range(_arcIdleTime.x, _arcIdleTime.y) / (0.25f + activity);
                arc.RegenTimer = 0f;
                arc.Line.enabled = arc.Visible;
                if (arc.Visible)
                    RegenerateArc(arc.Line);
            }
            else if (arc.Visible)
            {
                // Crackle: reshape the bolt a few times while it is alive
                arc.RegenTimer += Time.deltaTime;
                if (arc.RegenTimer >= ArcRegenInterval)
                {
                    arc.RegenTimer = 0f;
                    RegenerateArc(arc.Line);
                }
            }
        }
    }

    private void RegenerateArc(LineRenderer line)
    {
        // Start on the funnel surface (random angle/height), end near the core
        float heightLocal = Random.Range(0.3f, FunnelHeight * 0.7f);
        float radius = Mathf.Lerp(FunnelMouthRadius, FunnelThroatRadius, heightLocal / FunnelHeight) * 0.9f;
        float angle = Random.value * Mathf.PI * 2f;
        Vector3 start = transform.TransformPoint(new Vector3(Mathf.Cos(angle) * radius, heightLocal, Mathf.Sin(angle) * radius));
        Vector3 end = _finale.CoreWorldPosition + Random.insideUnitSphere * 0.08f;

        for (int i = 0; i < ArcPointCount; i++)
        {
            float t = (float)i / (ArcPointCount - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            // Jitter is zero at both ends and strongest in the middle
            float envelope = Mathf.Sin(t * Mathf.PI);
            point += Random.insideUnitSphere * _arcJaggedness * envelope;
            line.SetPosition(i, point);
        }
    }

    private void OnDestroy()
    {
        // Meshes created at runtime are owned by this component
        foreach (var system in new[] { _debris })
        {
            if (system == null)
                continue;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.mesh != null)
                Destroy(renderer.mesh);
        }
    }
}
