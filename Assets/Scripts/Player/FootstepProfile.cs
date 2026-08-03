using UnityEngine;

/// <summary>
/// Sound profile for a footstep surface (e.g. clean floor, dirt, water, gravel).
/// Clips are split into left/right arrays so each foot plays a distinct sound.
/// Create via right-click → Create → Game → Footstep Profile.
/// </summary>
[CreateAssetMenu(fileName = "FootstepProfile", menuName = "Game/Footstep Profile")]
public class FootstepProfile : ScriptableObject
{
    public enum BlendMode
    {
        /// <summary>Zone sounds completely replace the default footsteps.</summary>
        Replace,
        /// <summary>Zone sounds are played on top of the default footsteps.</summary>
        Additive
    }

    [Header("Blend")]
    [Tooltip("Replace: zone sounds completely replace the default footsteps.\n" +
             "Additive: zone sounds are played on top of the default footsteps.")]
    [SerializeField] private BlendMode blendMode = BlendMode.Replace;

    [Header("Clips")]
    [Tooltip("Sounds played when the left foot lands.")]
    [SerializeField] private AudioClip[] leftClips;

    [Tooltip("Sounds played when the right foot lands.")]
    [SerializeField] private AudioClip[] rightClips;

    [Header("Volume")]
    [Tooltip("Multiplier applied on top of the controller's walk/run/crouch volume.")]
    [SerializeField] [Range(0f, 2f)] private float volumeMultiplier = 1f;

    [Header("Timing")]
    [Tooltip("Seconds to skip at the start of each clip. Use to trim silence so the impact hits exactly when the foot lands.")]
    [SerializeField] [Range(0f, 0.5f)] private float startOffset;

    public BlendMode Mode           => blendMode;
    public AudioClip[] LeftClips    => leftClips;
    public AudioClip[] RightClips   => rightClips;
    public float VolumeMultiplier   => volumeMultiplier;
    public float StartOffset        => startOffset;

    /// <summary>Returns a random clip for the requested foot, or null if the array is empty.</summary>
    public AudioClip GetRandomClip(bool isLeft)
    {
        AudioClip[] pool = isLeft ? leftClips : rightClips;
        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    /// <summary>True when at least one clip exists for the requested foot.</summary>
    public bool HasClipsForFoot(bool isLeft)
    {
        AudioClip[] pool = isLeft ? leftClips : rightClips;
        return pool != null && pool.Length > 0;
    }
}
