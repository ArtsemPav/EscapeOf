using System.Collections;
using UnityEngine;

/// <summary>
/// Slows the player down when they enter this trigger and shows a spider web
/// overlay on the screen. Slow and overlay fade out after a configurable time.
/// Attach to spider web trigger objects (e.g. PRF_SpiderWeb).
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpiderWebSlowTrigger : MonoBehaviour
{
    [Header("Slow Settings")]
    [Tooltip("Movement multiplier applied while the effect is active (0..1). " +
             "0.5 means the player moves at half speed.")]
    [SerializeField, Range(0.01f, 1f)] private float speedMultiplier = 0.5f;

    [Tooltip("How long (seconds) the slow effect lasts after touching the web.")]
    [SerializeField] private float slowDuration = 3f;

    [Tooltip("Minimal time (seconds) between re-triggers to avoid spam.")]
    [SerializeField] private float reTriggerDelay = 1f;

    private float _lastTriggerTime = -Mathf.Infinity;

    private void Reset()
    {
        // When the component is added in the editor, make sure the collider is a trigger.
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - _lastTriggerTime < reTriggerDelay) return;

        var player = other.GetComponentInParent<FPSController>();
        if (player == null) return;

        _lastTriggerTime = Time.time;
        player.ApplyWebSlow(speedMultiplier);
        SpiderWebOverlay.Instance?.Show(slowDuration);
        StartCoroutine(RestoreSpeedAfterDelay(player, slowDuration));
    }

    private IEnumerator RestoreSpeedAfterDelay(FPSController player, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (player != null) player.RemoveWebSlow();
    }
}
