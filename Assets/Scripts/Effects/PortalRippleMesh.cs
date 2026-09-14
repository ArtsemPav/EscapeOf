using UnityEngine;

/// <summary>
/// Generates a flat circular disc (fan triangulation) used as the water-ripple
/// surface at the portal throat. UVs are centered: (0.5, 0.5) at the center,
/// rim at radius 0.5 from center.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[ExecuteAlways]
public class PortalRippleMesh : MonoBehaviour
{
    private const int MinSegments = 8;
    private const int MaxSegments = 128;

    [SerializeField] private float _radius = 0.95f;
    [SerializeField, Range(MinSegments, MaxSegments)] private int _segments = 64;

    private MeshFilter _meshFilter;
    private Mesh _generatedMesh;

    private void Awake()
    {
        GenerateMesh();
    }

    private void OnValidate()
    {
        GenerateMesh();
    }

    /// <summary>
    /// Regenerates the disc mesh from the current parameters.
    /// </summary>
    public void GenerateMesh()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshFilter == null)
            return;

        _segments = Mathf.Clamp(_segments, MinSegments, MaxSegments);

        if (_generatedMesh == null)
        {
            _generatedMesh = new Mesh { name = "PortalRipple" };
        }
        else
        {
            _generatedMesh.Clear();
        }

        int count = _segments + 1;
        var vertices = new Vector3[count + 1];
        var uvs = new Vector2[count + 1];
        var normals = new Vector3[count + 1];
        var triangles = new int[_segments * 3];

        // Center vertex
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        normals[0] = Vector3.up;

        for (int i = 0; i < _segments; i++)
        {
            float angle = (float)i / _segments * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            vertices[i + 1] = new Vector3(cos * _radius, 0f, sin * _radius);
            uvs[i + 1] = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);
            normals[i + 1] = Vector3.up;

            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = ((i + 1) % _segments) + 1;
        }

        _generatedMesh.SetVertices(new System.Collections.Generic.List<Vector3>(vertices));
        _generatedMesh.SetUVs(0, new System.Collections.Generic.List<Vector2>(uvs));
        _generatedMesh.SetNormals(new System.Collections.Generic.List<Vector3>(normals));
        _generatedMesh.SetTriangles(triangles, 0);
        _generatedMesh.RecalculateBounds();

        _meshFilter.sharedMesh = _generatedMesh;
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
