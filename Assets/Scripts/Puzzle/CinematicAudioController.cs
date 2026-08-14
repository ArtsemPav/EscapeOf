using System;
using UnityEngine;

/// <summary>
/// Manages audio playback for cinematic sequences.
/// Place on the same GameObject as the Animator.
/// Use PlayByIndex / PlayByName from code or Animation Events.
/// </summary>
public class CinematicAudioController : MonoBehaviour
{
    // ── Nested Types ─────────────────────────────────────────────────────────────

    [Serializable]
    public struct CinematicAudioEntry
    {
        [Tooltip("Audio clip to play.")]
        public AudioClip Clip;

        [Tooltip("Volume in 0..1 range.")]
        [Range(0f, 1f)]
        public float Volume;
    }

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Clips")]
    [Tooltip(
        "Audio clips available to this cinematic. " +
        "Trigger via PlayByIndex or PlayByName from code or Animation Events.")]
    [SerializeField] private CinematicAudioEntry[] _clips;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the clip at the given array index.
    /// Can be called from code or from an Animation Event with an int parameter.
    /// </summary>
    public void PlayByIndex(int index)
    {
        if (_clips == null || index < 0 || index >= _clips.Length)
            return;

        var entry = _clips[index];
        if (entry.Clip == null)
            return;

        AudioManager.Instance?.PlaySFX(entry.Clip, entry.Volume);
    }

    /// <summary>
    /// Plays the first clip whose name matches.
    /// Can be called from code or from an Animation Event with a string parameter.
    /// </summary>
    public void PlayByName(string clipName)
    {
        if (_clips == null || string.IsNullOrEmpty(clipName))
            return;

        foreach (var entry in _clips)
        {
            if (entry.Clip != null && entry.Clip.name == clipName)
            {
                AudioManager.Instance?.PlaySFX(entry.Clip, entry.Volume);
                return;
            }
        }
    }
}
