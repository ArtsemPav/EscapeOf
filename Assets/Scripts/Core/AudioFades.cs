using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared fade coroutines for AudioSource volume transitions. All audio systems
/// (AudioManager, MusicDirector) use these so fade behaviour stays consistent
/// and unscaled-time based (works while the game is paused).
/// </summary>
public static class AudioFades {
    /// <summary>
    /// Fades a single source's volume to the target over the duration.
    /// Completes silently when the source is destroyed mid-fade.
    /// </summary>
    public static IEnumerator Volume(AudioSource source, float to, float duration, Action onComplete = null) {
        float from = source != null ? source.volume : 0f;
        float elapsed = 0f;
        while (elapsed < duration && source != null) {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        if (source != null) source.volume = to;
        onComplete?.Invoke();
    }
}
