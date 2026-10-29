using UnityEditor;

/// <summary>
/// Clears Cinemachine 3's static Solo state whenever the editor enters Play
/// mode. Solo ("show this camera no matter what") is an editor preview tool,
/// but the flag survives into Play mode and hijacks every CinemachineBrain —
/// the playtest keeps showing the soloed framing instead of the player's
/// camera. With this hook, soloing stays a pure edit-mode preview tool.
/// </summary>
[InitializeOnLoad]
public static class CinemachineSoloPlayGuard
{
    static CinemachineSoloPlayGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            Unity.Cinemachine.CinemachineCore.SoloCamera = null;
    }
}
