using UnityEngine;

/// <summary>
/// Generates a flat quad mesh at runtime for the floor fog plane.
/// The mesh lies on the XZ plane facing upward.
/// Applies a rendering layer mask so the fog only reacts to specific light sources.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FogPlaneMesh : MonoBehaviour
{
    private const int VERTEX_COUNT = 4;
    private const int TRIANGLE_COUNT = 6;

    [Tooltip("Rendering layer names that control which lights affect the fog. " +
             "Must match names in TagManager > Rendering Layers.")]
    [SerializeField] private string[] _renderingLayers = { "flashLight" };

    // Rendering layer bit indices as defined in TagManager > m_RenderingLayers.
    private static readonly string[] RENDERING_LAYER_NAMES =
    {
        "Default", "Room1", "Room2", "Room3", "Room4",
        "Room5", "Room6", "Room7", "Bathroom", "flashLight",
        "NurseryRoom", "Corridor", "Laboratory", "Stairs", "Procedural",
        "MorrowOfice", "temp", "Bathroom1stFloor", "Pantry2", "elevator",
        "Electric", "GeneratorRoom"
    };

    private void Awake()
    {
        GenerateQuadMesh();
        ApplyRenderingLayerMask();
    }

    /// <summary>
    /// Sets the MeshRenderer rendering layer mask from the named rendering layers
    /// so the fog only receives light from matching light sources.
    /// </summary>
    private void ApplyRenderingLayerMask()
    {
        if (_renderingLayers == null || _renderingLayers.Length == 0)
            return;

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        uint mask = 0;

        foreach (string layerName in _renderingLayers)
        {
            int bitIndex = System.Array.IndexOf(RENDERING_LAYER_NAMES, layerName);
            if (bitIndex >= 0)
                mask |= (1u << bitIndex);
            else
                Debug.LogWarning($"[FogPlaneMesh] Rendering layer '{layerName}' not found in TagManager.", this);
        }

        renderer.renderingLayerMask = mask;
    }

    private void GenerateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "FogQuad"
        };

        Vector3[] vertices = new Vector3[VERTEX_COUNT]
        {
            new Vector3(-0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, -0.5f)
        };

        int[] triangles = new int[TRIANGLE_COUNT]
        {
            0, 1, 2,
            0, 2, 3
        };

        Vector2[] uv = new Vector2[VERTEX_COUNT]
        {
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f)
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.normals = new Vector3[VERTEX_COUNT]
        {
            Vector3.up,
            Vector3.up,
            Vector3.up,
            Vector3.up
        };
        mesh.RecalculateBounds();

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;
    }
}
