using UnityEngine;

/// <summary>
/// Simulates and renders a 3D wire between two world-space anchor points.
///
/// Physics: Verlet integration with distance constraints.
/// The wire has a fixed rest length (_slackFactor) that is always greater than
/// the straight-line distance between anchors, so it always sags naturally.
///
/// Rendering: LineRenderer with configurable width and material.
/// The component is created programmatically by ElectricPuzzleController.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ElectricWire : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int   ConstraintIterations = 12;

    /// <summary>
    /// Squared per-point velocity threshold below which a wire is considered at rest.
    /// (0.003 world units per frame)² — generous enough to absorb tiny gravity residuals.
    /// </summary>
    private const float SleepThresholdSq = 0.003f * 0.003f;

    /// <summary>Consecutive frames below threshold required before the wire sleeps.</summary>
    private const int SleepFramesRequired = 6;

    // ── Static wire registry (for inter-wire repulsion) ───────────────────────

    private static readonly System.Collections.Generic.List<ElectricWire> _allWires = new();

    // ── Runtime state ─────────────────────────────────────────────────────────

    // Settings are injected via Init — configure them on ElectricPuzzleController.
    private ElectricWireSettings _settings;

    private LineRenderer _lineRenderer;
    private Material     _lineMaterial;
    private Vector3[]    _positions;
    private Vector3[]    _prevPositions;
    private float        _restSegmentLength;

    // Sleep system — skips simulation once the wire reaches equilibrium
    private bool _isSleeping;
    private int  _sleepFrameCount;

    // Shorthand accessors into _settings (avoids null-checks scattered everywhere)
    private int   SegCount  => _settings?.segmentCount ?? 20;
    private float Gravity   => _settings?.gravity      ?? -9.81f;
    private float Damping   => _settings?.damping      ?? 0.04f;

    private Transform _startAnchor;
    private Transform _endAnchor;    // null while dragging
    private Vector3   _dragEndPoint; // world point followed when _endAnchor == null

    // Visual caps at both wire ends
    private Transform _capStart;
    private Transform _capEnd;
    private Transform _capStartAnchorChild; // "anchor" child — physical wire attach point
    private Transform _capEndAnchorChild;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Index of the colored terminal this wire originated from (0..5).</summary>
    public int ColoredIndex { get; private set; } = -1;

    /// <summary>True when this wire is snapped to both start and end terminals.</summary>
    public bool IsConnected => _endAnchor != null;

    /// <summary>The neutral terminal Transform this wire is connected to, or null.</summary>
    public Transform EndAnchor => _endAnchor;

    /// <summary>
    /// Initialises the wire. Call once right after the component is added.
    /// </summary>
    /// <param name="startAnchor">Transform of the colored terminal (pinned start).</param>
    /// <param name="coloredIndex">Index of the source colored terminal (0..5).</param>
    /// <param name="color">Visual tint applied to the LineRenderer and caps.</param>
    /// <param name="wireMaterial">Material for the LineRenderer (null = URP/Unlit default).</param>
    /// <param name="capPrefab">Optional prefab instantiated at both wire ends as visual connectors.</param>
    /// <param name="settings">Simulation and rendering settings (null = built-in defaults).</param>
    public void Init(Transform startAnchor, int coloredIndex, Color color,
                     Material wireMaterial, GameObject capPrefab = null,
                     ElectricWireSettings settings = null)
    {
        _settings     = settings ?? new ElectricWireSettings();
        _startAnchor  = startAnchor;
        _endAnchor    = null;
        _dragEndPoint = startAnchor.position;
        ColoredIndex  = coloredIndex;

        _allWires.Add(this);

        SetupLineRenderer(color, wireMaterial);
        SpawnCaps(capPrefab, startAnchor);

        Vector3 startPin = _capStartAnchorChild != null
            ? _capStartAnchorChild.position
            : startAnchor.position;
        InitializePoints(startPin, startPin);
    }

    /// <summary>Snaps the wire end to a neutral terminal anchor and freezes the rest length.</summary>
    public void ConnectEnd(Transform endAnchor)
    {
        Wake(); // clear any sleep state before re-settling
        _endAnchor = endAnchor;

        if (_capEnd != null)
        {
            _capEnd.rotation = endAnchor.rotation;
            _capEnd.position = endAnchor.position;
        }

        Vector3 startPin = _capStartAnchorChild != null ? _capStartAnchorChild.position : _startAnchor.position;
        Vector3 endPin   = _capEndAnchorChild   != null ? _capEndAnchorChild.position   : endAnchor.position;

        // Re-layout points along the straight line so they start near the final shape
        ReinitializeAlong(startPin, endPin);
        RecalculateRestLength(startPin, endPin);

        // Pre-simulate so the wire loads in its natural resting shape instantly
        PresettleWire(startPin, endPin);
    }

    /// <summary>Releases the end anchor — the wire end follows the drag point.</summary>
    public void DisconnectEnd()
    {
        _endAnchor = null;
        Wake(); // ensure simulation runs while wire is being dragged
    }

    /// <summary>
    /// Updates the world-space drag position when the wire end is free.
    /// Clamps to <see cref="ElectricWireSettings.maxDragDistance"/> from the start terminal.
    /// </summary>
    public void SetDragPoint(Vector3 worldPoint)
    {
        if (_settings != null && _settings.maxDragDistance > 0f && _startAnchor != null)
        {
            Vector3 dir = worldPoint - _startAnchor.position;
            if (dir.magnitude > _settings.maxDragDistance)
                worldPoint = _startAnchor.position + dir.normalized * _settings.maxDragDistance;
        }
        _dragEndPoint = worldPoint;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_startAnchor == null || _positions == null) return;

        // Sleeping wire: positions are stable — skip simulation entirely to save CPU
        // and eliminate micro-vibration. The LineRenderer keeps its last-set positions.
        if (_isSleeping) return;

        // Use the physical anchor points as Verlet pins
        Vector3 startPin = _capStartAnchorChild != null
            ? _capStartAnchorChild.position
            : _startAnchor.position;

        Vector3 endPin;
        if (_endAnchor != null)
        {
            endPin = _capEndAnchorChild != null
                ? _capEndAnchorChild.position
                : _endAnchor.position;
        }
        else
        {
            // During drag: cap center follows cursor, wire pin goes to cap's anchor child
            if (_capEnd != null)
                _capEnd.position = _dragEndPoint;

            endPin = _capEndAnchorChild != null
                ? _capEndAnchorChild.position
                : _dragEndPoint;

            RecalculateRestLength(startPin, endPin);
        }

        SimulateVerlet(startPin, endPin);
        _lineRenderer.SetPositions(_positions);

        // Only check sleep when the wire is fully connected (both endpoints pinned)
        if (_endAnchor != null)
            CheckSleep();
    }

    private void OnDestroy()
    {
        _allWires.Remove(this);
        if (_capStart    != null) Destroy(_capStart.gameObject);
        if (_capEnd      != null) Destroy(_capEnd.gameObject);
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void SetupLineRenderer(Color color, Material wireMaterial)
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.useWorldSpace     = true;
        _lineRenderer.positionCount     = SegCount;
        _lineRenderer.startWidth        = _settings.wireWidthStart;
        _lineRenderer.endWidth          = _settings.wireWidthEnd;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lineRenderer.receiveShadows    = false;
        _lineRenderer.textureMode       = LineTextureMode.Stretch;
        _lineRenderer.alignment         = LineAlignment.View;
        _lineRenderer.numCapVertices    = 4;
        _lineRenderer.numCornerVertices = 4;

        // LineRenderer wires require Unlit rendering so that _BaseColor maps directly
        // to the visible color without being affected by scene lighting or PBR metallic values.
        // Use wireMaterial only when it is already an Unlit-based material; otherwise fall back
        // to a fresh URP/Unlit instance so the assigned color is always correct.
        bool isUnlit = wireMaterial != null && wireMaterial.shader != null
                       && wireMaterial.shader.name.Contains("Unlit");

        _lineMaterial = isUnlit
            ? new Material(wireMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        _lineMaterial.SetColor("_BaseColor", color);

        _lineRenderer.sharedMaterial = _lineMaterial;
        _lineRenderer.startColor     = Color.white;
        _lineRenderer.endColor       = Color.white;
    }

    private void SpawnCaps(GameObject capPrefab, Transform startTerminal)
    {
        if (capPrefab == null) return;

        _capStart = SpawnCap(capPrefab, "WireCap_Start", out _capStartAnchorChild);
        _capStart.rotation = startTerminal.rotation * Quaternion.Euler(0f, 180f, 0f);
        _capStart.position = startTerminal.position;

        _capEnd = SpawnCap(capPrefab, "WireCap_End", out _capEndAnchorChild);
        _capEnd.rotation = startTerminal.rotation;
        PlaceCapAtPoint(_capEnd, _capEndAnchorChild, startTerminal.position);
    }

    private Transform SpawnCap(GameObject prefab, string capName, out Transform anchorChild)
    {
        var go = Instantiate(prefab);
        go.name = capName;
        go.transform.SetParent(null);
        anchorChild = go.transform.Find("anchor");
        return go.transform;
    }

    /// <summary>
    /// Positions <paramref name="cap"/> so that its <paramref name="anchorChild"/>
    /// lands exactly at <paramref name="targetWorldPos"/>.
    /// Must be called AFTER setting the cap's rotation.
    /// </summary>
    private static void PlaceCapAtPoint(Transform cap, Transform anchorChild, Vector3 targetWorldPos)
    {
        // Start at target, then shift by how much the anchor overshoots
        cap.position = targetWorldPos;
        if (anchorChild != null)
        {
            Vector3 overshoot = anchorChild.position - targetWorldPos;
            cap.position -= overshoot;
        }
    }

    private void InitializePoints(Vector3 start, Vector3 end)
    {
        _positions     = new Vector3[SegCount];
        _prevPositions = new Vector3[SegCount];

        RecalculateRestLength(start, end);

        for (int i = 0; i < SegCount; i++)
        {
            float   t = (float)i / (SegCount - 1);
            Vector3 p = Vector3.Lerp(start, end, t);
            _positions[i]     = p;
            _prevPositions[i] = p;
        }
    }

    /// <summary>
    /// Checks whether all interior points have velocity below <see cref="SleepThresholdSq"/>.
    /// After <see cref="SleepFramesRequired"/> consecutive stable frames the wire goes to sleep,
    /// pausing all simulation until <see cref="Wake"/> is called.
    /// </summary>
    private void CheckSleep()
    {
        for (int i = 1; i < SegCount - 1; i++)
        {
            if ((_positions[i] - _prevPositions[i]).sqrMagnitude > SleepThresholdSq)
            {
                _sleepFrameCount = 0;
                return;
            }
        }

        if (++_sleepFrameCount >= SleepFramesRequired)
            _isSleeping = true;
    }

    /// <summary>Wakes the wire from sleep so simulation resumes.</summary>
    private void Wake()
    {
        _isSleeping      = false;
        _sleepFrameCount = 0;
    }

    /// <summary>
    /// Redistributes all Verlet points evenly along the straight line between start and end.
    /// Used before <see cref="PresettleWire"/> so the simulation starts near the final shape.
    /// </summary>
    private void ReinitializeAlong(Vector3 start, Vector3 end)
    {
        for (int i = 0; i < SegCount; i++)
        {
            float   t = (float)i / (SegCount - 1);
            Vector3 p = Vector3.Lerp(start, end, t);
            _positions[i]     = p;
            _prevPositions[i] = p;
        }
    }

    /// <summary>
    /// Runs the Verlet simulation synchronously for <paramref name="steps"/> iterations
    /// so that the wire loads in its natural hanging shape rather than animating from a collapsed state.
    /// Includes inter-wire repulsion so the wire avoids already-settled neighbours.
    /// Zeroes velocities at the end so the wire starts perfectly still.
    /// </summary>
    private void PresettleWire(Vector3 startPin, Vector3 endPin, int steps = 250)
    {
        const float dt = 0.016f;

        for (int step = 0; step < steps; step++)
        {
            for (int i = 1; i < SegCount - 1; i++)
            {
                Vector3 vel   = (_positions[i] - _prevPositions[i]) * (1f - Damping);
                _prevPositions[i] = _positions[i];
                _positions[i]    += vel;
                _positions[i].y  += Gravity * dt * dt;
            }

            _positions[0]            = startPin;
            _positions[SegCount - 1] = endPin;
            _prevPositions[0]            = startPin;
            _prevPositions[SegCount - 1] = endPin;

            for (int iter = 0; iter < ConstraintIterations; iter++)
            {
                for (int i = 0; i < SegCount - 1; i++) ApplyConstraint(i, i + 1);
                for (int i = SegCount - 1; i > 0; i--) ApplyConstraint(i, i - 1);

                _positions[0]            = startPin;
                _positions[SegCount - 1] = endPin;
            }

            // Push away from already-settled wires in _allWires
            ApplyWireRepulsion();
        }

        // Zero out velocities so the wire sits perfectly still on the first rendered frame
        for (int i = 0; i < SegCount; i++)
            _prevPositions[i] = _positions[i];
    }

    /// <summary>
    /// Runs joint Verlet settling for ALL currently connected wires simultaneously,
    /// with inter-wire repulsion in every step.
    /// Call from <see cref="ElectricPuzzleController"/> after all wires are loaded from save
    /// to resolve any remaining overlaps that individual settling could not fix.
    /// </summary>
    public static void JointPresettle(int steps = 400)
    {
        const float dt = 0.016f;

        // Only process wires that are fully connected at both ends
        var connected = new System.Collections.Generic.List<ElectricWire>(_allWires.Count);
        foreach (var w in _allWires)
            if (w._positions != null && w._endAnchor != null)
                connected.Add(w);

        if (connected.Count == 0) return;

        for (int step = 0; step < steps; step++)
        {
            // Verlet + constraints for every wire
            foreach (var w in connected)
            {
                Vector3 sPin = w._capStartAnchorChild != null ? w._capStartAnchorChild.position : w._startAnchor.position;
                Vector3 ePin = w._capEndAnchorChild   != null ? w._capEndAnchorChild.position   : w._endAnchor.position;

                for (int i = 1; i < w.SegCount - 1; i++)
                {
                    Vector3 vel = (w._positions[i] - w._prevPositions[i]) * (1f - w.Damping);
                    w._prevPositions[i] = w._positions[i];
                    w._positions[i]    += vel;
                    w._positions[i].y  += w.Gravity * dt * dt;
                }

                w._positions[0]              = sPin;
                w._positions[w.SegCount - 1] = ePin;
                w._prevPositions[0]              = sPin;
                w._prevPositions[w.SegCount - 1] = ePin;

                for (int iter = 0; iter < ConstraintIterations; iter++)
                {
                    for (int i = 0; i < w.SegCount - 1; i++) w.ApplyConstraint(i, i + 1);
                    for (int i = w.SegCount - 1; i > 0; i--) w.ApplyConstraint(i, i - 1);
                    w._positions[0]              = sPin;
                    w._positions[w.SegCount - 1] = ePin;
                }
            }

            // Single repulsion pass after all wires are updated this step
            foreach (var w in connected)
                w.ApplyWireRepulsion();
        }

        // Zero velocities — all wires start their first rendered frame perfectly still
        foreach (var w in connected)
            for (int i = 0; i < w.SegCount; i++)
                w._prevPositions[i] = w._positions[i];
    }

    private void RecalculateRestLength(Vector3 start, Vector3 end)
    {
        float straight   = Vector3.Distance(start, end);
        float minLen     = _settings?.minWireLength ?? 0.08f;
        float slack      = _settings?.slackFactor   ?? 1.25f;
        float ropeLength = Mathf.Max(straight * slack, minLen);
        _restSegmentLength = ropeLength / (SegCount - 1);
    }

    private void SimulateVerlet(Vector3 startPos, Vector3 endPos)
    {
        float dt = Time.deltaTime;

        // Verlet integration — interior points only
        for (int i = 1; i < SegCount - 1; i++)
        {
            Vector3 velocity  = (_positions[i] - _prevPositions[i]) * (1f - Damping);
            _prevPositions[i] = _positions[i];
            _positions[i]    += velocity;
            _positions[i].y  += Gravity * dt * dt;
        }

        // Pin both anchors
        _positions[0]            = startPos;
        _positions[SegCount - 1] = endPos;
        _prevPositions[0]            = startPos;
        _prevPositions[SegCount - 1] = endPos;

        // Constraint passes — bidirectional for faster convergence
        for (int iter = 0; iter < ConstraintIterations; iter++)
        {
            for (int i = 0; i < SegCount - 1; i++)
                ApplyConstraint(i, i + 1);

            for (int i = SegCount - 1; i > 0; i--)
                ApplyConstraint(i, i - 1);

            // Re-pin every pass
            _positions[0]            = startPos;
            _positions[SegCount - 1] = endPos;
        }

        // NOTE: no runtime repulsion here — it would fight the constraints and cause permanent
        // oscillation. Separation is handled once during PresettleWire / JointPresettle.
    }

    private void ApplyConstraint(int a, int b)
    {
        Vector3 delta = _positions[b] - _positions[a];
        float   dist  = delta.magnitude;
        if (dist < Mathf.Epsilon) return;

        float   error      = (dist - _restSegmentLength) * 0.5f;
        Vector3 correction = delta.normalized * error;

        bool pinA = (a == 0 || a == SegCount - 1);
        bool pinB = (b == 0 || b == SegCount - 1);

        if (!pinA) _positions[a] += correction;
        if (!pinB) _positions[b] -= correction;
    }

    /// <summary>
    /// Pushes each interior point away from the interior points of every other active wire.
    /// Uses velocity-preserving correction: both _positions and _prevPositions are shifted
    /// by the same amount so no artificial velocity is introduced (prevents jitter).
    /// </summary>
    private void ApplyWireRepulsion()
    {
        if (_settings == null || _settings.repulsionRadius <= 0f) return;

        float radius   = _settings.repulsionRadius;
        float radiusSq = radius * radius;

        foreach (var other in _allWires)
        {
            if (other == this || other._positions == null) continue;

            int otherCount = other.SegCount;

            for (int i = 1; i < SegCount - 1; i++)
            {
                for (int j = 0; j < otherCount; j++)
                {
                    Vector3 delta  = _positions[i] - other._positions[j];
                    float   distSq = delta.sqrMagnitude;
                    if (distSq >= radiusSq || distSq < 0.000001f) continue;

                    float   dist       = Mathf.Sqrt(distSq);
                    float   overlap    = radius - dist;
                    Vector3 correction = delta / dist * overlap;

                    // Shift both arrays by the same amount — preserves implicit velocity,
                    // so this correction does not accelerate the point (no jitter).
                    _positions[i]     += correction;
                    _prevPositions[i] += correction;
                }
            }
        }
    }
}
