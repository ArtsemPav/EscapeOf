using UnityEngine;

/// <summary>
/// Continuously spins the object around an axis (Y by default).
/// All parameters are exposed in the Inspector for per-prefab tuning.
/// </summary>
public class ObjectSpinner : MonoBehaviour
{
    [Header("Rotation")]
    [Tooltip("Rotation speed in degrees per second.")]
    [SerializeField] private float rotationSpeed = 45f;

    [Tooltip("Rotation axis in local space. Y by default.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Tooltip("If true, rotates in local space; otherwise in world space.")]
    [SerializeField] private bool useLocalSpace = true;

    [Tooltip("If true, rotates in the direction of rotationSpeed; if false, inverts it.")]
    [SerializeField] private bool clockwise = true;

    /// <summary>Effective signed speed applied every frame, in degrees per second.</summary>
    public float EffectiveSpeed => clockwise ? rotationSpeed : -rotationSpeed;

    private void Update()
    {
        float delta = EffectiveSpeed * Time.deltaTime;
        Quaternion step = Quaternion.AngleAxis(delta, rotationAxis.normalized);
        transform.rotation = useLocalSpace
            ? transform.rotation * step
            : step * transform.rotation;
    }
}
