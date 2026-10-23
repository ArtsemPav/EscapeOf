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
    /// True when the zone's collider is enabled and contains the world point.
    /// Used by FootstepController to drop stale zones that never received OnTriggerExit.
    /// </summary>
    public bool ContainsPoint(Vector3 point)
    {
        return _collider != null && _collider.enabled && _collider.bounds.Contains(point);
    }
}
