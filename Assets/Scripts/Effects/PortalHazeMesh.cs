using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates an open, capless cone surface that matches the funnel
/// silhouette. Used as a translucent "haze" layer so nothing behind the portal
/// is clearly visible. UV: U wraps around the axis, V runs along the height.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[ExecuteAlways]
public class PortalHazeMesh : MonoBehaviour
{
    private const int MinRadialSegments = 8;
    private const int MaxRadialSegments = 128;
    private const int MinHeightSegments = 1;
    private const int MaxHeightSegments = 64;

    [SerializeField] private float _height = 7f;
    [SerializeField] private float _startRadius = 2.9f;
    [SerializeField] private float _endRadius = 0.15f;
    [SerializeField, Range(MinRadialSegments, MaxRadialSegments)] private int _radialSegments = 64;
    [SerializeField, Range(MinHeightSegments, MaxHeightSegments)] private int _heightSegments = 16;

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
    /// Regenerates the open cone mesh from the current parameters.
    /// </summary>
    public void GenerateMesh()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshFilter == null)
            return;

        if (_generatedMesh == null)
        {
            _generatedMesh = new Mesh { name = "PortalHaze" };
        }
        else
        {
            _generatedMesh.Clear();
        }

        int rings = _heightSegments + 1;
        int columns = _radialSegments + 1;
        var vertices = new List<Vector3>(rings * columns);
        var uvs = new List<Vector2>(rings * columns);
        var normals = new List<Vector3>(rings * columns);
        var triangles = new List<int>();

        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / _heightSegments;
            float radius = Mathf.Lerp(_startRadius, _endRadius, t);
            float y = t * _height;

            for (int j = 0; j < columns; j++)
            {
                float angle = (float)j / _radialSegments * Mathf.PI * 2f;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices.Add(new Vector3(cosA * radius, y, sinA * radius));
                uvs.Add(new Vector2((float)j / _radialSegments, t));
                normals.Add(new Vector3(cosA, 0f, sinA));
            }
        }

        for (int i = 0; i < _heightSegments; i++)
        {
            for (int j = 0; j < _radialSegments; j++)
            {
                int row = i * columns;
                int a = row + j;
                int b = row + j + 1;
                int c = row + columns + j;
                int d = row + columns + j + 1;

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);

                triangles.Add(b);
                triangles.Add(d);
                triangles.Add(c);
            }
        }

        _generatedMesh.SetVertices(vertices);
        _generatedMesh.SetUVs(0, uvs);
        _generatedMesh.SetNormals(normals);
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
