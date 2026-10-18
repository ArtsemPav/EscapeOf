using UnityEngine;

/// <summary>
/// Applies the player's saved video settings (resolution, frame rate, quality)
/// at startup in every scene via <see cref="SystemSettings.ApplyAtStartup"/>.
/// Settings themselves live in <see cref="SystemSettings"/>; the settings UI
/// (<see cref="SettingsPanelController"/>) applies and persists them immediately.
/// </summary>
[DefaultExecutionOrder(-50)] // apply saved video settings early, before gameplay scripts read them
public class ResolutionManager : MonoBehaviour
{
    public static ResolutionManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SystemSettings.ApplyAtStartup();
    }
}
