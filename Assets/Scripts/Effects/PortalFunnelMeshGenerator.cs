using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates a helical ribbon mesh for the portal funnel effect.
/// Multiple ribbon layers create a layered tornado/vortex appearance.
/// UV mapping: U = position along the spiral (0 at wide end, 1 at narrow end),
/// V = 0..1 across the ribbon width.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[ExecuteAlways]
public class PortalFunnelMeshGenerator : MonoBehaviour
{
    [System.Serializable]
    public struct RibbonLayer
    {
        [Range(1, 8)] public int turns;
        public float startRadius;
        public float endRadius;
        public float width;
        public float angleOffsetDegrees;
        [Range(0.1f, 1f)] public float heightFactor;
        [Range(0, 1f)] public float startVFade;

        public static RibbonLayer Default(int index)
        {
            return new RibbonLayer
            {
                turns = 4,
                startRadius = 3f - index * 0.25f,
                endRadius = 0.2f + index * 0.02f,
                width = 0.22f - index * 0.04f,
                angleOffsetDegrees = index * 60f,
                heightFactor = 1f,
                startVFade = 0f
            };
        }
    }

    private const int MinSegmentsPerTurn = 8;
    private const int MaxSegmentsPerTurn = 256;

    [Header("Funnel Shape")]
    [SerializeField] private float _height = 8f;
    [SerializeField] private int _segmentsPerTurn = 96;

    [Header("Ribbon Layers")]
    [SerializeField] private RibbonLayer[] _layers =
    {
        RibbonLayer.Default(0),
        RibbonLayer.Default(1),
        RibbonLayer.Default(2),
    };

    private MeshFilter _meshFilter;
    private Mesh _generatedMesh;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        GenerateMesh();
    }

    private void OnValidate()
    {
        _segmentsPerTurn = Mathf.Clamp(_segmentsPerTurn, MinSegmentsPerTurn, MaxSegmentsPerTurn);
        if (_layers == null || _layers.Length == 0)
        {
            _layers = new RibbonLayer[]
            {
                RibbonLayer.Default(0),
                RibbonLayer.Default(1),
                RibbonLayer.Default(2),
            };
        }
    }

    /// <summary>
    /// Regenerates the helical ribbon mesh from the current layer parameters.
    /// </summary>
    public void GenerateMesh()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshFilter == null)
            return;

        if (_generatedMesh == null)
        {
            _generatedMesh = new Mesh { name = "PortalFunnel" };
        }
        else
        {
            _generatedMesh.Clear();
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        for (int i = 0; i < _layers.Length; i++)
        {
            GenerateLayer(_layers[i], vertices, uvs, normals, triangles);
        }

        _generatedMesh.SetVertices(vertices);
        _generatedMesh.SetUVs(0, uvs);
        _generatedMesh.SetNormals(normals);
        _generatedMesh.SetTriangles(triangles, 0);
        _generatedMesh.RecalculateBounds();

        _meshFilter.sharedMesh = _generatedMesh;
    }

    /// <summary>
    /// Replaces the layer configuration and regenerates the mesh.
    /// Used by PortalFunnelSpawner to build transient spiral instances.
    /// </summary>
    public void SetLayers(RibbonLayer[] layers)
    {
        _layers = layers;
        GenerateMesh();
    }

    private void GenerateLayer(RibbonLayer layer, List<Vector3> vertices, List<Vector2> uvs,
                                List<Vector3> normals, List<int> triangles)
    {
        int totalSegments = Mathf.Max(1, _segmentsPerTurn * layer.turns);
        int vertexOffset = vertices.Count;
        float layerHeight = Mathf.Max(0.01f, _height * layer.heightFactor);
        float angleOffset = layer.angleOffsetDegrees * Mathf.Deg2Rad;

        for (int i = 0; i <= totalSegments; i++)
        {
            float t = (float)i / totalSegments;
            float angle = t * layer.turns * Mathf.PI * 2f + angleOffset;
            float radius = Mathf.Lerp(layer.startRadius, layer.endRadius, t);
            float y = Mathf.Lerp(0f, layerHeight, t);

            Vector3 center = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

            // Helix tangent: derivative of (cos(θ)*r, y, sin(θ)*r) w.r.t. t
            float dTheta = layer.turns * Mathf.PI * 2f;
            float dr = layer.endRadius - layer.startRadius;
            Vector3 tangent = new Vector3(
                -Mathf.Sin(angle) * radius * dTheta + Mathf.Cos(angle) * dr,
                layerHeight,
                Mathf.Cos(angle) * radius * dTheta + Mathf.Sin(angle) * dr
            ).normalized;

            // Radial direction (outward from funnel axis)
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;

            // Normal = perpendicular to ribbon surface (cross of tangent and radial)
            Vector3 normal = Vector3.Cross(tangent, radial).normalized;
            // Ensure normal points roughly outward
            if (Vector3.Dot(normal, radial) < 0f)
                normal = -normal;

            float halfWidth = layer.width * 0.5f;
            Vector3 inner = center - radial * halfWidth;
            Vector3 outer = center + radial * halfWidth;

            vertices.Add(inner);
            vertices.Add(outer);

            // UV: U = t (along spiral, 0=wide, 1=narrow), V = 0..1 across width
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));

            normals.Add(normal);
            normals.Add(normal);
        }

        // Build triangles for this layer's ribbon strip
        for (int i = 0; i < totalSegments; i++)
        {
            int vi = vertexOffset + i * 2;
            int inner0 = vi;
            int outer0 = vi + 1;
            int inner1 = vi + 2;
            int outer1 = vi + 3;

            triangles.Add(inner0);
            triangles.Add(outer0);
            triangles.Add(inner1);

            triangles.Add(outer0);
            triangles.Add(outer1);
            triangles.Add(inner1);
        }
    }

    private void OnDestroy()
    {
        if (_generatedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_generatedMesh);
            else
                DestroyImmediate(_generatedMesh);
        }
    }
}
