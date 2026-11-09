using UnityEngine;
using Escape.Core;

/// <summary>
/// Shared raycast helper for audio occlusion checks: determines whether the
/// direct path between two points is blocked by geometry (floor slab, wall,
/// furniture). The player's own colliders and open door panels do not occlude,
/// so sounds stay clear along a real line of sight.
/// </summary>
public static class AudioOcclusionUtility
{
    private const int HitCapacity = 16;

    private static readonly RaycastHit[] Hits = new RaycastHit[HitCapacity];

    // Layers that occlude the direct path: everything except Ignore Raycast.
    // The player's own colliders are skipped by comparing hits against the
    // player component below.
    private static int _occluderMask = -1;

    /// <summary>
    /// True when a raycast from <paramref name="from"/> to <paramref name="to"/>
    /// hits geometry before reaching the destination: a floor slab, a wall or
    /// furniture. The player's own colliders are ignored, and open door panels
    /// do not occlude — you can hear through them. Geometry closer than
    /// <paramref name="ignoreRadiusNearStart"/> to the source is skipped, so a
    /// source sitting inside a recess (e.g. a duck inside a toilet bowl) is not
    /// permanently "occluded" by the object it rests in.
    /// </summary>
    public static bool IsPathOccluded(Vector3 from, Vector3 to, float ignoreRadiusNearStart = 0f)
    {
        if (_occluderMask == -1)
        {
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            _occluderMask = ignoreRaycastLayer >= 0 ? ~(1 << ignoreRaycastLayer) : ~0;
        }

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.01f) return false;

        Vector3 direction = delta / distance;

        // A source inside a recess (duck in the toilet bowl, radio on a shelf)
        // would always be "occluded" by the object it rests in. Skip geometry
        // near the start of the ray: shrink the ray to start past the radius.
        float rayLength = distance;
        Vector3 rayStart = from;
        if (ignoreRadiusNearStart > 0f && ignoreRadiusNearStart < distance)
        {
            rayStart = from + direction * ignoreRadiusNearStart;
            rayLength = distance - ignoreRadiusNearStart;
        }

        int hitCount = Physics.RaycastNonAlloc(
            rayStart, direction, Hits, rayLength, _occluderMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = Hits[i].collider;

            // Skip the player's own colliders (capsule, etc.).
            if (hit.GetComponentInParent<FPSController>() != null) continue;

            // Open door panels do not occlude — you can see/hear through them.
            DoorInteraction door = hit.GetComponentInParent<DoorInteraction>();
            if (door != null && door.IsOpen) continue;

            return true;
        }

        return false;
    }
}
