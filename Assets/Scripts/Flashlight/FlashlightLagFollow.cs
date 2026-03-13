using UnityEngine;

/// <summary>
/// Smoothly chases the camera's position and rotation with configurable inertia.
/// Attach to the Flashlight GameObject (must NOT be a child of the camera).
/// The rotation lag creates a realistic "weight" feel — the light lags slightly
/// behind head movement and catches up when the player stops turning.
/// </summary>
public class FlashlightLagFollow : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;

    [Header("Follow")]
    [Tooltip("How fast the flashlight rotation catches up to the camera. Lower = more lag.")]
    [SerializeField] [Range(1f, 30f)] private float rotationFollowSpeed = 6f;

    [Tooltip("How fast the flashlight position follows the camera. Should be faster than rotation.")]
    [SerializeField] [Range(1f, 30f)] private float positionFollowSpeed = 14f;

    private void Awake()
    {
        if (cameraTransform == null)
            Debug.LogWarning("FlashlightLagFollow: cameraTransform is not assigned.", this);
    }

    // LateUpdate runs after FPSController has applied camera rotation
    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Position: follows camera tightly so flashlight stays near the head
        transform.position = Vector3.Lerp(
            transform.position,
            cameraTransform.position,
            positionFollowSpeed * Time.deltaTime
        );

        // Rotation: lags behind the camera — this is the core realistic effect
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            cameraTransform.rotation,
            rotationFollowSpeed * Time.deltaTime
        );
    }
}
