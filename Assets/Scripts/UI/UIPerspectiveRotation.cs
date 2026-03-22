using UnityEngine;

/// <summary>
/// Applies a fixed Y-axis rotation to a RectTransform every LateUpdate,
/// creating a perspective foreshortening effect in ScreenSpaceCamera canvas.
/// Runs after animators so it persists over animation overrides.
/// </summary>
public class UIPerspectiveRotation : MonoBehaviour
{
    [Tooltip("Rotation around the Y axis in degrees. Negative = right side recedes.")]
    [SerializeField] private float _yRotation = -25f;

    private void LateUpdate()
    {
        transform.localRotation = Quaternion.Euler(0f, _yRotation, 0f);
    }
}
