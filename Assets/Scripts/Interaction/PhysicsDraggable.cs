using UnityEngine;

/// <summary>
/// Marks a GameObject as physics-draggable by the player.
/// Requires a Rigidbody — its mass controls how slowly the object responds to force.
/// Assign the GameObject to the "Draggable" layer.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsDraggable : MonoBehaviour
{
    [Tooltip("Hint shown in the interaction UI when the player looks at this object.")]
    [SerializeField] private string dragHint = "Тянуть";

    [Tooltip("When enabled, the object cannot be tipped over while dragging. X and Z rotation axes are frozen.")]
    [SerializeField] private bool preventTipping = false;

    [Tooltip("Max distance from the player at which this object can be grabbed. " +
             "0 = use the controller's global interact distance. Only shortens the global value — " +
             "a value larger than the global distance has no effect.")]
    [SerializeField] private float _maxDragDistance = 0f;

    [Tooltip("Distance in front of the camera where this object is HELD while dragging. " +
             "Smaller = the object hangs closer to the player. 0 = use the grabber's global hold distance.")]
    [SerializeField] private float _holdDistance = 0f;

    /// <summary>Cached Rigidbody reference.</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>UI hint text displayed while the player hovers over this object.</summary>
    public string DragHint => dragHint;

    /// <summary>Whether rotation on X and Z axes should be frozen while dragging.</summary>
    public bool PreventTipping => preventTipping;

    /// <summary>True when this object defines its own max grab distance.</summary>
    public bool HasCustomDragDistance => _maxDragDistance > 0f;

    /// <summary>Custom max grab distance (only meaningful when HasCustomDragDistance).</summary>
    public float MaxDragDistance => _maxDragDistance;

    /// <summary>True when this object defines its own hold distance while dragged.</summary>
    public bool HasCustomHoldDistance => _holdDistance > 0f;

    /// <summary>Custom hold distance while dragged (only meaningful when HasCustomHoldDistance).</summary>
    public float HoldDistance => _holdDistance;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!preventTipping) return;

        // Zero X/Z angular velocity so no new tipping torque can build up.
        Vector3 av = Body.angularVelocity;
        av.x = 0f;
        av.z = 0f;
        Body.angularVelocity = av;

        // Also hard-correct the rotation every step — this handles the case
        // where a high-velocity collision tips the object before angular velocity
        // can be zeroed. Only Y (yaw) is preserved; X and Z are snapped to 0.
        float yaw = Body.rotation.eulerAngles.y;
        Body.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }
}
