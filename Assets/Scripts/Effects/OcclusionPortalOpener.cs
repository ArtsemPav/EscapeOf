using UnityEngine;

/// <summary>
/// Tiny UnityEvent helper: OcclusionPortal.open cannot be wired as a persistent
/// UnityEvent call through serialized targets, so attach this to the GameObject
/// with the OcclusionPortal and call SetOpenState from events.
/// </summary>
public class OcclusionPortalOpener : MonoBehaviour
{
    /// <summary>
    /// Opens or closes the OcclusionPortal on this GameObject.
    /// Wire from a UnityEvent with a bool argument.
    /// </summary>
    /// <param name="open">True to open the portal, false to close it.</param>
    public void SetOpenState(bool open)
    {
        OcclusionPortal portal = GetComponent<OcclusionPortal>();
        if (portal != null)
        {
            portal.open = open;
        }
    }
}
