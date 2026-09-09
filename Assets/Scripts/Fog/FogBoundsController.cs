using UnityEngine;

/// <summary>
/// Automatically updates the FloorFog shader's world-space boundary properties
/// from this GameObject's Transform. The fog fills the rectangular area defined
/// by the Transform's position and scale, with smooth edge falloff.
/// Disable <see cref="_autoBounds"/> to set bounds manually in the material.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class FogBoundsController : MonoBehaviour
{
    private const float DEFAULT_FALLOFF = 2.0f;

    [Tooltip("When enabled, bounds are recalculated from the Transform every frame.")]
    [SerializeField] private bool _autoBounds = true;

    [Tooltip("How far from the edge the fog starts fading, in world units.")]
    [SerializeField] private float _falloff = DEFAULT_FALLOFF;

    // Shader property IDs
    private static readonly int ID_BOUNDS_ENABLED = Shader.PropertyToID("_BoundsEnabled");
    private static readonly int ID_BOUNDS_MIN = Shader.PropertyToID("_BoundsMin");
    private static readonly int ID_BOUNDS_MAX = Shader.PropertyToID("_BoundsMax");
    private static readonly int ID_BOUNDS_FALLOFF = Shader.PropertyToID("_BoundsFalloff");

    // The runtime quad mesh spans -0.5..0.5 in local X and Z,
    // so world half-extents are scale.x * 0.5 and scale.z * 0.5.
    private const float HALF_EXTENT_FACTOR = 0.5f;

    private Material _material;

    /// <summary>
    /// Sets whether bounds are recalculated from the Transform every frame.
    /// </summary>
    public bool AutoBounds
    {
        get => _autoBounds;
        set => _autoBounds = value;
    }

    /// <summary>
    /// Sets the edge falloff distance in world units.
    /// </summary>
    public float Falloff
    {
        get => _falloff;
        set => _falloff = value;
    }

    private void Start()
    {
        _material = GetComponent<MeshRenderer>().material;
        UpdateBounds();
    }

    private void LateUpdate()
    {
        if (_material == null || !_autoBounds)
            return;

        UpdateBounds();
    }

    /// <summary>
    /// Recalculates bounds from the Transform and writes them to the material.
    /// </summary>
    private void UpdateBounds()
    {
        if (_material == null)
            return;

        if (!_autoBounds)
        {
            _material.SetFloat(ID_BOUNDS_ENABLED, 0f);
            return;
        }

        Vector3 pos = transform.position;
        Vector3 scale = transform.localScale;

        float halfX = scale.x * HALF_EXTENT_FACTOR;
        float halfZ = scale.z * HALF_EXTENT_FACTOR;

        _material.SetFloat(ID_BOUNDS_ENABLED, 1f);
        _material.SetVector(ID_BOUNDS_MIN, new Vector4(pos.x - halfX, 0f, pos.z - halfZ, 0f));
        _material.SetVector(ID_BOUNDS_MAX, new Vector4(pos.x + halfX, 0f, pos.z + halfZ, 0f));
        _material.SetFloat(ID_BOUNDS_FALLOFF, _falloff);
    }
}
