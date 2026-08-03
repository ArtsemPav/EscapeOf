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

    public FootstepProfile Profile => profile;
    public int Priority => priority;

    private void OnTriggerEnter(Collider other)
    {
        var controller = other.GetComponentInParent<FootstepController>();
        if (controller != null)
            controller.RegisterZone(this);
    }

    private void OnTriggerExit(Collider other)
    {
        var controller = other.GetComponentInParent<FootstepController>();
        if (controller != null)
            controller.UnregisterZone(this);
    }
}
