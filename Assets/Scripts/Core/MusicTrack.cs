using UnityEngine;

/// <summary>
/// Describes a single music track: the clip, its target volume and fade time.
/// Create one asset per track in /Assets/sound and reference it from
/// MusicZone, MusicDirector or other systems instead of raw AudioClips.
/// </summary>
[CreateAssetMenu(fileName = "MusicTrack", menuName = "Audio/Music Track")]
public class MusicTrack : ScriptableObject {
    [Tooltip("The audio clip to play for this track.")]
    [SerializeField] private AudioClip _clip;

    [Tooltip("Target volume this track fades to when it becomes active.")]
    [SerializeField] private float _volume = 0.4f;

    [Tooltip("Whether the track loops while it is active.")]
    [SerializeField] private bool _loop = true;

    [Tooltip("Fade duration in seconds. Negative uses the MusicDirector default.")]
    [SerializeField] private float _fadeDuration = -1f;

    public AudioClip Clip => _clip;
    public float Volume => _volume;
    public bool Loop => _loop;
    public float FadeDuration => _fadeDuration;
}
