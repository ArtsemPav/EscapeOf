using UnityEditor;

/// <summary>
/// DEBUG-ONLY menu for the wake-up intro.
///
/// ⚠ TODO: REMOVE THIS FILE BEFORE RELEASE — the user asked for a debug
/// entry point to replay the intro from the editor; it must be deleted
/// (or disabled) when the intro goes to production.
///
/// Usage: enter Play Mode in the Game scene, then use
/// Debug/Wake-Up Intro/Restart Intro In Place — the intro replays immediately,
/// no scene reload needed.
/// </summary>
public static class WakeUpIntroDebugMenu
{
    private const string MenuRoot = "Debug/Wake-Up Intro/";

    /// <summary>Restarts the wake-up intro in the running scene. Play Mode only.</summary>
    [MenuItem(MenuRoot + "Restart Intro In Place", priority = 0)]
    private static void RestartIntroInPlace()
    {
        var intro = UnityEngine.Object.FindFirstObjectByType<WakeUpIntroController>(
            UnityEngine.FindObjectsInactive.Include);
        if (intro == null)
        {
            UnityEngine.Debug.LogWarning("[DebugMenu] WakeUpIntroController not found in the scene.");
            return;
        }

        intro.enabled = true; // it may have disabled itself after a previous skip
        intro.DebugPlayNow();
        UnityEngine.Debug.Log("[DebugMenu] Wake-up intro restarted.", intro);
    }

    /// <summary>Clears a stale intro request flag (PlayerPrefs).</summary>
    [MenuItem(MenuRoot + "Clear Pending Request", priority = 1)]
    private static void ClearPendingRequest()
    {
        SaveManager.ConsumeWakeUpIntroRequest();
        UnityEngine.Debug.Log("[DebugMenu] Pending intro request cleared.");
    }

    // Grey the items out when not in Play Mode.
    [MenuItem(MenuRoot + "Restart Intro In Place", true, priority = 0)]
    private static bool IsPlaying() => EditorApplication.isPlaying;
}
