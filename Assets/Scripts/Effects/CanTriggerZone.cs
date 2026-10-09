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

    private void Update() => _elapsed += Time.deltaTime;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasFired) return;
        if (_elapsed < _startGracePeriod) return;
        if (_canRigidbody == null || _sequence == null) return;

        // The can may enter with any of its child colliders — check the rigidbody
        if (other.attachedRigidbody != _canRigidbody) return;

        _hasFired = true;
        StartCoroutine(StartSequenceAfterDelay());
    }

    private IEnumerator StartSequenceAfterDelay()
    {
        yield return new WaitForSeconds(_triggerDelay);
        _sequence.BeginFromCanTrigger();
    }
}
