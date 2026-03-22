using UnityEngine;

/// <summary>
/// Handles main menu button interactions.
/// Attach to MainMenuCanvas. Wire buttons via Inspector onClick events.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("Settings panel to show when the Settings button is pressed.")]
    [SerializeField] private GameObject _settingsPanel;

    /// <summary>Resumes the game and hides the main menu.</summary>
    public void OnNewGame()
    {
        GameManager.Instance?.SetPause(false);
    }

    /// <summary>Opens the settings sub-panel.</summary>
    public void OnSettings()
    {
        if (_settingsPanel == null)
        {
            Debug.LogWarning("MainMenuController: Settings panel is not assigned.", this);
            return;
        }

        UIManager.Instance?.OpenPanel(_settingsPanel);
    }

    /// <summary>Quits the application. In Editor exits Play mode instead.</summary>
    public void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
