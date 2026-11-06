using UnityEngine;

/// <summary>Axis around which the object spins.</summary>
public enum SpinAxis
{
    X,
    Y,
    Z,
    Custom
}

/// <summary>Direction of the spin around the chosen axis.</summary>
public enum SpinDirection
{
    Clockwise,
    CounterClockwise
}

/// <summary>
/// Continuously spins the object around a selectable axis (Y by default).
/// Axis, speed and direction are exposed in the Inspector for per-prefab tuning.
/// </summary>
public class ObjectSpinner : MonoBehaviour
{
    private const float DefaultSpeed = 45f;

    [Header("Rotation")]
    [Tooltip("Rotation speed in degrees per second.")]
    [SerializeField] private float rotationSpeed = DefaultSpeed;

    [Tooltip("Axis to spin around. Custom uses the vector below.")]
    [SerializeField] private SpinAxis rotationAxisMode = SpinAxis.Y;

    [Tooltip("Custom rotation axis, used only when Rotation Axis Mode is Custom.")]
    [SerializeField] private Vector3 customRotationAxis = Vector3.up;

    [Tooltip("Spin direction around the chosen axis.")]
    [SerializeField] private SpinDirection rotationDirection = SpinDirection.Clockwise;

    [Tooltip("If true, rotates in local space; otherwise in world space.")]
    [SerializeField] private bool useLocalSpace = true;

    /// <summary>Signed speed applied every frame, in degrees per second.</summary>
    public float EffectiveSpeed =>
        rotationDirection == SpinDirection.Clockwise ? rotationSpeed : -rotationSpeed;

    /// <summary>Rotation axis resolved from the selected axis mode.</summary>
    public Vector3 EffectiveAxis => ResolveAxis(rotationAxisMode, customRotationAxis);

    private void Update()
    {
        float delta = EffectiveSpeed * Time.deltaTime;
        Quaternion step = Quaternion.AngleAxis(delta, EffectiveAxis.normalized);
        transform.rotation = useLocalSpace
            ? transform.rotation * step
            : step * transform.rotation;
    }

    /// <summary>Maps the selected axis mode to a direction vector.</summary>
    private static Vector3 ResolveAxis(SpinAxis mode, Vector3 customAxis)
    {
        switch (mode)
        {
            case SpinAxis.X: return Vector3.right;
            case SpinAxis.Z: return Vector3.forward;
            case SpinAxis.Custom: return customAxis;
            default: return Vector3.up;
        }
    }
}
