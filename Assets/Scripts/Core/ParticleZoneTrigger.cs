using UnityEngine;

/// <summary>
/// Toggles a ParticleSystem on when the player enters the trigger collider
/// and off when the player exits. Requires a trigger collider on this GameObject.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class ParticleZoneTrigger : MonoBehaviour
{
    private const string PLAYER_TAG = "Player";

    private ParticleSystem _particles;
    private ParticleSystemRenderer _renderer;

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        _renderer = GetComponent<ParticleSystemRenderer>();

        // Start hidden — particles only appear when the player enters the zone
        _renderer.enabled = false;
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(PLAYER_TAG)) return;

        _renderer.enabled = true;
        _particles.Play(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(PLAYER_TAG)) return;

        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _renderer.enabled = false;
    }
}
