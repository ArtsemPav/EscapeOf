using UnityEngine;

/// <summary>
/// Fires a music track when the player enters the trigger collider.
/// Each zone triggers exactly once — re-entering a visited zone never
/// changes the music. Leaving the zone does nothing: the music either
/// plays through to its end (a non-looping track falls back to the
/// previous/default track) or is replaced by another unvisited trigger.
/// Requires a trigger collider on this GameObject.
/// </summary>
public class MusicZone : MonoBehaviour {
    private const string PLAYER_TAG = "Player";
    private const string LOG_PREFIX = "[MusicZone]";

    [Tooltip("Track to play when the player enters the zone.")]
    [SerializeField] private MusicTrack _zoneTrack;

    [Tooltip("If enabled, the track is marked played (never restored by saves and never re-requested by the director).")]
    [SerializeField] private bool _playOnce = false;

    private bool _hasTriggered;

    private void Awake() {
        // Make the track resolvable by name for save data before the director's Start applies it.
        MusicDirector.RegisterTrack(_zoneTrack);
    }

    private void OnTriggerEnter(Collider other) {
        Debug.Log($"{LOG_PREFIX} '{name}' ENTER by '{other.name}' (already triggered: {_hasTriggered}, playOnce: {_playOnce})");
        if (_hasTriggered) return;
        if (!other.CompareTag(PLAYER_TAG)) return;

        // Only an accepted request makes this zone the dominant track — and
        // only then it is persisted, so already-fired zones never re-trigger
        // in the next session.
        bool accepted = MusicDirector.RequestTrack(_zoneTrack, playOnce: _playOnce);
        Debug.Log($"{LOG_PREFIX} '{name}' request for '{_zoneTrack.name}' -> {(accepted ? "ACCEPTED" : "REJECTED by director")}");
        if (accepted) {
            _hasTriggered = true;
            SaveManager.Instance?.Save();
        }
    }

    private void OnTriggerExit(Collider other) {
        // Diagnostics only: leaving a zone must never touch the music. This log
        // proves whether a reported "silence on exit" really comes from the zone.
        if (!other.CompareTag(PLAYER_TAG)) return;
        Debug.Log($"{LOG_PREFIX} '{name}' EXIT by '{other.name}' — no music changes on exit");
    }
}
