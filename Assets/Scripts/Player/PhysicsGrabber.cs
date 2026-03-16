using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player drag physics objects by holding LMB.
/// Uses a spring-damper force: heavier Rigidbodies (higher mass) accelerate slower,
/// giving the sensation of weight. Assign draggable objects to the "Draggable" layer.
/// </summary>
public class PhysicsGrabber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Detection")]
    [Tooltip("Max raycast distance to start grabbing an object.")]
    [SerializeField] private float grabDistance = 3f;
    [Tooltip("Radius of the detection sphere cast. Larger values make hovering more forgiving.")]
    [SerializeField] private float detectionRadius = 0.08f;
    [SerializeField] private LayerMask draggableLayer;

    [Header("Hold")]
    [Tooltip("Distance in front of the camera where the object is pulled toward.")]
    [SerializeField] private float holdDistance = 2f;

    [Header("Spring Physics")]
    [Tooltip("Spring constant pulling object toward hold point. F = k * distance.")]
    [SerializeField] private float springStrength = 200f;
    [Tooltip("Velocity damping to prevent oscillation. Higher = less bounce.")]
    [SerializeField] private float springDamping = 20f;
    [Tooltip("How quickly angular velocity is zeroed while holding. Prevents wild spinning.")]
    [SerializeField] private float angularDampingFactor = 8f;
    [Tooltip("Velocity cap for the grabbed object (m/s).")]
    [SerializeField] private float maxVelocity = 8f;
    [Tooltip("Linear drag applied while grabbed. Restored on release.")]
    [SerializeField] private float grabLinearDrag = 5f;

    [Header("Player Speed While Dragging")]
    [Tooltip("Object mass at which the player moves at minimum speed.")]
    [SerializeField] private float referenceHeavyMass = 20f;
    [Tooltip("Minimum speed multiplier (0..1) when holding the heaviest object.")]
    [SerializeField] private float minSpeedMultiplier = 0.4f;
    [Tooltip("Gap between object and hold point below which no extra slowdown is applied.")]
    [SerializeField] private float acceptableGap = 0.6f;
    [Tooltip("Gap at which the player is fully stopped to let the object catch up.")]
    [SerializeField] private float maxGap = 1.5f;

    private FPSController _fpsController;
    private PhysicsDraggable _hoveredDraggable;
    private PhysicsDraggable _grabbedDraggable;
    private float _originalLinearDrag;
    private RigidbodyConstraints _originalConstraints;
    private float _massSpeedMultiplier = 1f;

    private void Awake()
    {
        _fpsController = GetComponent<FPSController>();
    }

    private void OnDisable()
    {
        ReleaseObject();
    }

    private void Update()
    {
        // Suppress grabbing when cursor is free (inventory/menu open)
        if (Cursor.lockState == CursorLockMode.None)
        {
            ReleaseObject();
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool isRunning = _fpsController != null && _fpsController.IsRunning;
        bool lmbHeld = mouse.leftButton.isPressed;

        if (_grabbedDraggable != null)
        {
            // Release if LMB released or player started running
            if (!lmbHeld || isRunning)
            {
                ReleaseObject();
            }
            else
            {
                // Dynamically reduce speed based on how far the object lags behind the hold point.
                // When gap <= acceptableGap → base mass multiplier.
                // When gap >= maxGap → player is fully stopped so the object can catch up.
                Vector3 holdPoint = cameraTransform.position + cameraTransform.forward * holdDistance;
                float gap = Vector3.Distance(_grabbedDraggable.Body.position, holdPoint);
                float gapFactor = 1f - Mathf.Clamp01((gap - acceptableGap) / (maxGap - acceptableGap));
                _fpsController?.SetSpeedMultiplier(_massSpeedMultiplier * gapFactor);
            }
        }
        else
        {
            // Detection always runs so hint stays visible while running
            DetectHoveredDraggable();

            // Grabbing only allowed when not running and LMB just pressed
            if (!isRunning && mouse.leftButton.wasPressedThisFrame && _hoveredDraggable != null)
                GrabObject(_hoveredDraggable);
        }
    }

    private void FixedUpdate()
    {
        if (_grabbedDraggable == null) return;

        Rigidbody rb = _grabbedDraggable.Body;
        Vector3 holdPoint = cameraTransform.position + cameraTransform.forward * holdDistance;

        // Spring-damper: F = k*(target - pos) - d*velocity
        // ForceMode.Force divides by mass → heavier objects accelerate slower
        Vector3 force = (holdPoint - rb.position) * springStrength
                      - rb.linearVelocity * springDamping;

        rb.AddForce(force, ForceMode.Force);

        // Clamp velocity to avoid runaway speed on light objects
        if (rb.linearVelocity.magnitude > maxVelocity)
            rb.linearVelocity = rb.linearVelocity.normalized * maxVelocity;

        // Damp angular velocity — prevents the object from tumbling while dragged
        rb.angularVelocity = Vector3.Lerp(
            rb.angularVelocity,
            Vector3.zero,
            angularDampingFactor * Time.fixedDeltaTime
        );
    }

    /// <summary>Casts a sphere from the camera and highlights the nearest PhysicsDraggable.</summary>
    private void DetectHoveredDraggable()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (Physics.SphereCast(ray, detectionRadius, out RaycastHit hit, grabDistance, draggableLayer)
            && hit.collider.TryGetComponent(out PhysicsDraggable draggable))
        {
            if (_hoveredDraggable != draggable)
            {
                _hoveredDraggable = draggable;
                InteractionUI.Instance?.SetHint(true, draggable.DragHint, isPickable: false, CrosshairMode.Grab);
            }
            return;
        }

        if (_hoveredDraggable != null)
        {
            _hoveredDraggable = null;
            InteractionUI.Instance?.SetHint(false);
        }
    }

    /// <summary>Begins dragging the target object. Stores and overrides its linear drag.</summary>
    private void GrabObject(PhysicsDraggable draggable)
    {
        _grabbedDraggable = draggable;
        _hoveredDraggable = null;

        Rigidbody rb = draggable.Body;

        _originalLinearDrag = rb.linearDamping;
        _originalConstraints = rb.constraints;

        rb.linearDamping = grabLinearDrag;

        if (draggable.PreventTipping)
            rb.constraints = _originalConstraints | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Slow player proportionally to object mass
        // multiplier: 1.0 (light) → minSpeedMultiplier (heavy)
        float t = Mathf.Clamp01(rb.mass / referenceHeavyMass);
        _massSpeedMultiplier = Mathf.Lerp(1f, minSpeedMultiplier, t);

        _fpsController?.SetPhysicsGrabActive(true);
        InteractionUI.Instance?.SetHint(false);
    }

    /// <summary>Releases the grabbed object and restores its original drag, constraints, and player speed.</summary>
    private void ReleaseObject()
    {
        if (_grabbedDraggable == null) return;

        Rigidbody rb = _grabbedDraggable.Body;
        rb.linearDamping = _originalLinearDrag;
        rb.constraints = _originalConstraints;

        _fpsController?.SetSpeedMultiplier(1f);
        _fpsController?.SetPhysicsGrabActive(false);

        _grabbedDraggable = null;
    }
}
