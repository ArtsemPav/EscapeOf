using UnityEngine;

/// <summary>
/// Sound profile for the fall-through-floor event.
/// Holds one or more fall/impact clips — a random one is picked per fall,
/// so the sound can vary without touching any code or scene wiring.
/// Create via right-click → Create → Game → Fall Sound Profile.
/// </summary>
[CreateAssetMenu(fileName = "FallSoundProfile", menuName = "Game/Fall Sound Profile")]
public class FallSoundProfile : ScriptableObject
{
    [Header("Clips")]
    [Tooltip("Fall/impact sounds; one is picked at random each time the player falls through the floor.")]
    [SerializeField] private AudioClip[] fallClips;

    [Header("Volume")]
    [Tooltip("Playback volume for the fall sound.")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    public AudioClip[] FallClips => fallClips;
    public float Volume => volume;

    /// <summary>Returns a random fall clip, or null if the array is empty.</summary>
    public AudioClip GetRandomClip()
    {
        if (fallClips == null || fallClips.Length == 0) return null;
        return fallClips[Random.Range(0, fallClips.Length)];
    }
}
