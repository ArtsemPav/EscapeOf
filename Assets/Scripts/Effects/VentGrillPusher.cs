using UnityEngine;

/// <summary>
/// Pushes the vent grill out of its frame the moment its Rigidbody is released
/// (isKinematic turned off by BoxFallTrigger). A grill wedged tightly in the
/// vent opening may not move under gravity alone; this applies a one-shot
/// outward impulse so it visibly pops out, tumbles and falls.
///
/// The outward direction is auto-detected: the script probes both sides of the
/// grill's plate and pushes toward the side with more free space (the room).
/// Attach to the grill GameObject alongside the Rigidbody.
/// </summary>
public class VentGrillPusher : MonoBehaviour
{
    private const float ProbeDistance = 1f;
    private const float UpwardPushFraction = 0.3f;

    [Tooltip("Rigidbody to push. Defaults to the one on this object.")]
    [SerializeField] private Rigidbody _rigidbody;

    [Tooltip("Impulse strength as velocity change (m/s) along the outward normal.")]
    [SerializeField, Min(0.1f)] private float _pushStrength = 1.5f;

    [Tooltip("Random spin (rad/s) added so the grill tumbles as it falls instead of sliding flat.")]
    [SerializeField, Min(0f)] private float _spinStrength = 2f;

    private bool _applied;

    /// <summary>World-space normal of the grill plate (its thin local axis).</summary>
    private Vector3 PlateNormal => transform.right;

    private void Awake()
    {
        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_applied || _rigidbody == null || _rigidbody.isKinematic) return;

        _applied = true;

        Vector3 outward = DetectOutwardDirection();
        Vector3 push = (outward + Vector3.up * UpwardPushFraction).normalized * _pushStrength;
        _rigidbody.linearVelocity += push;

        Vector3 spin = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)) * _spinStrength;
        _rigidbody.angularVelocity += spin;
    }

    /// <summary>
    /// Raycasts from the grill center along both sides of its plate normal and
    /// returns the direction with more clearance — the open side of the vent.
    /// Falls back to the positive normal when both sides look equally free.
    /// </summary>
    private Vector3 DetectOutwardDirection()
    {
        Vector3 normal = PlateNormal;
        Vector3 center = _rigidbody.worldCenterOfMass;

        bool positiveBlocked = Physics.Raycast(center, normal, ProbeDistance,
            ~0, QueryTriggerInteraction.Ignore);
        bool negativeBlocked = Physics.Raycast(center, -normal, ProbeDistance,
            ~0, QueryTriggerInteraction.Ignore);

        if (positiveBlocked && !negativeBlocked)
            return -normal;
        return normal;
    }
}
