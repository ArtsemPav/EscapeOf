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

    private void OnTriggerEnter(Collider other) {
        if (_playOnce && _hasTriggered) return;
        if (!other.CompareTag(PLAYER_TAG)) return;

        _hasTriggered = true;
        MusicDirector.RequestTrack(_zoneTrack, playOnce: _playOnce);
    }

    private void OnTriggerExit(Collider other) {
        if (!other.CompareTag(PLAYER_TAG)) return;
        MusicDirector.ClearTrack(_zoneTrack);
    }
}
