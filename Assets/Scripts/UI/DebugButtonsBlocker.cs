using UnityEngine;

/// <summary>
/// Hides its own GameObject (and optionally extra debug objects) unless the
/// game runs in the Editor or as a Development build. Apply to debug-only
/// pause-menu buttons (Save / Load Save / Delete Debug Save / Reset Progress)
/// so they never appear for players.
/// </summary>
public class DebugButtonsBlocker : MonoBehaviour
{
    private void Awake()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        enabled = false; // keep visible in dev builds, no further work needed
#else
        gameObject.SetActive(false);
#endif
    }
}
