using UnityEngine;

/// <summary>
/// Requests a music track while the player is inside the trigger collider
/// and restores the previous track when the player leaves.
/// With Play Once enabled the zone triggers a single time and is ignored afterwards.
/// Requires a trigger collider on this GameObject.
/// </summary>
public class MusicZone : MonoBehaviour {
    private const string PLAYER_TAG = "Player";

    [Tooltip("Track to play while the player is inside the zone.")]
    [SerializeField] private MusicTrack _zoneTrack;

    [Tooltip("If enabled, the zone triggers only once and further entries are ignored.")]
    [SerializeField] private bool _playOnce = false;

    private bool _hasTriggered;

    private void Awake() {
        // Make the track resolvable by name for save data before the director's Start applies it.
        MusicDirector.RegisterTrack(_zoneTrack);
    }

    private void OnTriggerEnter(Collider other) {
        if (_playOnce && _hasTriggered) return;
        if (!other.CompareTag(PLAYER_TAG)) return;

        // Only an accepted request makes this zone the dominant track — and
        // only then it is persisted, so already-fired zones never re-trigger
        // in the next session.
        if (MusicDirector.RequestTrack(_zoneTrack, playOnce: _playOnce)) {
            _hasTriggered = true;
            SaveManager.Instance?.Save();
        }
    }

    private void OnTriggerExit(Collider other) {
        if (!other.CompareTag(PLAYER_TAG)) return;
        // A play-once track plays through to its end — leaving the zone does
        // not stop it. Only looping zone tracks restore the previous music.
        if (_playOnce && _hasTriggered) return;
        MusicDirector.ClearTrack(_zoneTrack);
    }
}
