using UnityEngine;

/// <summary>
/// Trigger volume that overrides the player's footstep sounds while inside.
/// Place on a GameObject with a Collider (isTrigger = true).
/// When multiple zones overlap, the one with the highest priority wins.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FootstepZone : MonoBehaviour
{
    [Header("Profile")]
    [Tooltip("Footstep profile used while the player is inside this zone.")]
    [SerializeField] private FootstepProfile profile;

    [Header("Priority")]
    [Tooltip("Higher value wins when multiple zones overlap. Ties resolved by most recently entered.")]
    [SerializeField] private int priority;

    private Collider _collider;
    private FootstepController _controller;

    // Capsule overlap test: sample points along the player's vertical axis and require
    // each to be within capsule radius of the zone collider. Mirrors the condition Unity
    // uses for OnTriggerEnter (CharacterController capsule overlap), so a zone that fired
    // the event always survives the staleness check, and one the player truly left is dropped.
    private const float CapsuleSampleBottom = 0.2f;
    private const float CapsuleSampleTop = 1.8f;
    private const float CapsuleSampleStep = 0.4f;
    private const float PlayerRadiusTolerance = 0.3f;

    public FootstepProfile Profile => profile;
    public int Priority => priority;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        var controller = other.GetComponentInParent<FootstepController>();
        if (controller != null)
        {
            _controller = controller;
            controller.RegisterZone(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var controller = other.GetComponentInParent<FootstepController>();
        if (controller != null)
            controller.UnregisterZone(this);
    }

    private void OnDisable()
    {
        // Trigger callbacks never fire when this object is deactivated (or its
        // Collider disabled) while the player is still inside — unregister manually
        // so the zone can't stay active forever and lock the profile.
        if (_controller != null)
        {
            _controller.UnregisterZone(this);
            _controller = null;
        }
    }

    /// <summary>
    /// True when the zone's collider is enabled and overlaps the player's capsule volume.
    /// Samples points along the player's vertical axis and measures their distance to the
    /// collider with a capsule-radius tolerance — this mirrors the condition Unity uses for
    /// OnTriggerEnter/OnTriggerExit (CharacterController capsule overlap), so the staleness
    /// check in FootstepController.ResolveCurrentProfile stays consistent with the physics
    /// events and never drops a zone the player is actually standing in.
    /// </summary>
    public bool ContainsPoint(Vector3 point)
    {
        if (_collider == null || !_collider.enabled)
            return false;

        for (float height = CapsuleSampleBottom; height <= CapsuleSampleTop; height += CapsuleSampleStep)
        {
            Vector3 sample = point + Vector3.up * height;
            if (_collider.bounds.Contains(sample))
                return true;

            // Outside the AABB — allow samples within the player capsule radius of the collider.
            Vector3 closest = _collider.ClosestPoint(sample);
            if ((closest - sample).sqrMagnitude <= PlayerRadiusTolerance * PlayerRadiusTolerance)
                return true;
        }

        return false;
    }
}
