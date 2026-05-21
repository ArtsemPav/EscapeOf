using UnityEngine;

/// <summary>
/// Marker component for decorative cylinders. 
/// Caches the original parent and provides a reset method.
/// </summary>
public class MetamorfDecorCylinder : MonoBehaviour
{
    public Transform OriginalParent { get; private set; }
    public Quaternion OriginalLocalRotation { get; private set; }
    public Vector3 OriginalLocalPosition { get; private set; }

    private void Awake()
    {
        OriginalParent = transform.parent;
        OriginalLocalRotation = transform.localRotation;
        OriginalLocalPosition = transform.localPosition;
    }

    /// <summary>Instantly restores the object to its starting state.</summary>
    public void ResetToOriginal()
    {
        transform.SetParent(OriginalParent, true);
        transform.localRotation = OriginalLocalRotation;
        transform.localPosition = OriginalLocalPosition;
    }
}
