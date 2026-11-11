using UnityEngine;

/// <summary>
/// Raises a GameEvent when the player falls through a hole in the floor.
/// Attach to a trigger volume placed under the floor hole (e.g. the Jump
/// trigger in a room prefab). On player entry raises <see cref="_fallEvent"/>
/// and optionally spawns a dust particle burst at the trigger position.
/// Particles play for a limited time (<see cref="_dustDuration"/>) and then stop.
/// </summary>
public class FallThroughFloorEvent : MonoBehaviour
{
    [Header("Event")]
    [Tooltip("GameEvent raised when the player falls through the floor.")]
    [SerializeField] private GameEvent _fallEvent;

    [Header("Dust Particles")]
    [Tooltip("Prefab with a ParticleSystem dust burst, spawned at the trigger position. Optional.")]
    [SerializeField] private ParticleSystem _dustParticlesPrefab;
    [Tooltip("How long the dust particles play before being stopped, in seconds.")]
    [SerializeField] private float _dustDuration = 2f;

    [Header("Filter")]
    [Tooltip("Only objects on these layers can trigger the fall event.")]
    [SerializeField] private LayerMask _playerMask = ~0;

    private Coroutine _dustRoutine;

    private void Reset()
    {
        // Trigger by default: the volume should not block the falling player.
        if (TryGetComponent(out Collider col)) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & _playerMask.value) == 0)
            return;

        if (!_fallEvent && _dustParticlesPrefab == null)
            return;

        _fallEvent?.Raise();
        PlayDust();
    }

    /// <summary>
    /// Spawns the dust burst at the trigger position and plays it for a
    /// limited time. Also usable from GameEventListener / UnityEvent reactions.
    /// </summary>
    public void PlayDust()
    {
        if (_dustParticlesPrefab == null)
            return;

        if (_dustRoutine != null)
            StopCoroutine(_dustRoutine);
        _dustRoutine = StartCoroutine(PlayDustRoutine());
    }

    private System.Collections.IEnumerator PlayDustRoutine()
    {
        ParticleSystem dust = Instantiate(_dustParticlesPrefab, transform.position, Quaternion.identity);
        dust.Play();
        yield return new WaitForSeconds(_dustDuration);
        dust.Stop();
        // Wait for the remaining particles to die out before destroying.
        yield return new WaitWhile(() => dust != null && dust.IsAlive(true));
        if (dust != null)
            Destroy(dust.gameObject);
        _dustRoutine = null;
    }
}
