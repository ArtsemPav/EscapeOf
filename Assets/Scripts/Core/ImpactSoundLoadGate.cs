using UnityEngine;

/// <summary>
/// Mutes impact/scrape sounds during the first moments after a scene load, while
/// the physics scene settles and save-restored rigidbodies depenetrate. Without the
/// gate, props that spawn slightly overlapped or off-balance play collision sounds
/// the player never caused.
/// </summary>
public static class ImpactSoundLoadGate
{
    /// <summary>Seconds after a scene load during which collision sounds are suppressed.</summary>
    private const float SettleGraceSeconds = 3f;

    /// <summary>Scaled time (Time.time) of the most recent scene-load registration.</summary>
    private static float _sceneLoadTime;

    /// <summary>True while collision sounds should be suppressed after a scene load.
    /// Uses scaled time, so a paused/loading screen (timeScale = 0) extends the grace.</summary>
    public static bool ShouldSuppress => Time.time - _sceneLoadTime < SettleGraceSeconds;

    /// <summary>Records the current moment as the scene-load time. Call from Awake of
    /// components that own collision audio; every scene load re-registers through them.</summary>
    public static void RegisterSceneLoad()
    {
        _sceneLoadTime = Time.time;
    }
}
