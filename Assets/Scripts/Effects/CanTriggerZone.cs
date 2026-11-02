using System.Collections;
using UnityEngine;

/// <summary>
/// Starts the UVPentagramSequence when the cat can is brought into this trigger.
/// Flow: the can (Rigidbody) enters the trigger zone → wait Trigger Delay seconds
/// → the sequence begins. Fires once per play session.
///
/// Attach to the CanTrigger GameObject (BoxCollider with Is Trigger enabled).
/// </summary>
public class CanTriggerZone : MonoBehaviour
{
    [Tooltip("The cat can's Rigidbody the player drags into the zone.")]
    [SerializeField] private Rigidbody _canRigidbody;

    [Tooltip("Seconds between the can entering the zone and the event starting.")]
    [SerializeField, Min(0f)] private float _triggerDelay = 1f;

    [Tooltip("Ignore trigger events for this many seconds after scene load — protects against " +
             "the can spawning/being saved inside the zone and auto-firing the event.")]
    [SerializeField, Min(0f)] private float _startGracePeriod = 2f;

    [Tooltip("The horror sequence to start.")]
    [SerializeField] private UVPentagramSequence _sequence;

    private bool _hasFired;
    private float _elapsed;

    private void Awake()
    {
        // The serialized references live in the room prefab and can only point at
        // scene objects via fragile prefab-instance overrides — resolve them at
        // runtime so the trigger survives prefab reverts and room re-placements.
        if (_sequence == null)
            _sequence = FindFirstObjectByType<UVPentagramSequence>();
        if (_sequence == null)
        {
            Debug.LogWarning($"[{name}] No UVPentagramSequence found in the scene — the trigger can never fire.", this);
            return;
        }

        if (_canRigidbody == null)
        {
            GameObject canObject = _sequence.CatCanObject;
            if (canObject != null)
                _canRigidbody = canObject.GetComponentInParent<Rigidbody>();
            if (_canRigidbody == null)
                Debug.LogWarning($"[{name}] Cat can Rigidbody not resolved — the trigger can never fire.", this);
        }
    }

    private void Update() => _elapsed += Time.deltaTime;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasFired) return;
        if (_elapsed < _startGracePeriod) return;
        if (_sequence == null || _sequence.HasFired) return;
        if (_canRigidbody == null || other.attachedRigidbody != _canRigidbody) return;

        _hasFired = true;
        Debug.Log($"[CanTriggerZone] Can accepted: {_canRigidbody.name}; CanPosition={_canRigidbody.position}; ZonePosition={transform.position}; Delay={_triggerDelay:F2}s", this);
        StartCoroutine(StartSequenceAfterDelay());
    }

    private IEnumerator StartSequenceAfterDelay()
    {
        yield return new WaitForSeconds(_triggerDelay);
        _sequence.BeginFromCanTrigger();
    }
}
