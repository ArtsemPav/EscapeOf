using UnityEngine;

/// <summary>
/// Caps the rendering frame rate to avoid the GPU rendering far more frames than needed.
/// VSync is disabled because it overrides Application.targetFrameRate. Runs automatically
/// at startup, so no GameObject setup is required.
/// </summary>
public static class FrameRateLimiter
{
    private const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        // VSync would otherwise clamp the frame rate to the display refresh rate and
        // ignore targetFrameRate, so disable it first.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
