using UnityEngine;

/// <summary>
/// Drops the target box when the player enters this trigger zone: releases the
/// box's Rigidbody (isKinematic off) so it falls and lands with an impact sound
/// played by PhysicsDraggable. Fires once.
/// </summary>
public class BoxFallTrigger : MonoBehaviour
{
    private const float DefaultStartGracePeriod = 2f;

    [Tooltip("Rigidbody of the box that falls when the player enters the zone.")]
    [SerializeField] private Rigidbody _boxRigidbody;

    [Tooltip("Ignore trigger events for this many seconds after scene load — protects " +
             "against the player spawning inside the zone and instantly firing it.")]
    [SerializeField, Min(0f)] private float _startGracePeriod = DefaultStartGracePeriod;

    private bool _hasFired;
    private float _elapsed;

    private void Update() => _elapsed += Time.deltaTime;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasFired || _elapsed < _startGracePeriod) return;
        if (_boxRigidbody == null || !other.CompareTag("Player")) return;

        _hasFired = true;
        _boxRigidbody.isKinematic = false;
    }
}
