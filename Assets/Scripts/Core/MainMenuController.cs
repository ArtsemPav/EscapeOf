using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu button interactions.
/// Attach to MainMenuCanvas. Wire buttons via Inspector onClick events.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    private const string GameplaySceneName = "Game";
    private const int DefaultSaveSlot = 0;

    [Header("Panels")]
    [Tooltip("Settings panel to show when the Settings button is pressed.")]
    [SerializeField] private GameObject _settingsPanel;

    [Tooltip("Root object of the main menu block, hidden while the settings panel is open.")]
    [SerializeField] private GameObject _menuRoot;

    [Header("Loading")]
    [Tooltip("Fake loading screen shown before switching to the gameplay scene.")]
    [SerializeField] private FakeLoadingScreen _loadingScreen;

    /// <summary>Starts a new game: clears the default save slot, shows the fake loading screen, then loads the gameplay scene.</summary>
    public void OnNewGame()
    {
        SaveManager.StartNewGame(DefaultSaveSlot);
        BeginGameTransition();
    }

    /// <summary>Loads the default save slot into the gameplay scene. Does nothing when no save exists.</summary>
    public void OnLoadGame()
    {
        if (!SaveManager.SlotHasSave(DefaultSaveSlot))
        {
            Debug.LogWarning("MainMenuController: Load skipped — no save file found.", this);
            return;
        }

        SaveManager.RequestLoadFromSlot(DefaultSaveSlot);
        BeginGameTransition();
    }

    private void BeginGameTransition()
    {
        if (_loadingScreen == null)
        {
            Debug.LogWarning("MainMenuController: Loading screen is not assigned, loading scene directly.", this);
            SceneManager.LoadScene(GameplaySceneName);
            return;
        }

        if (_menuRoot != null) _menuRoot.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        _loadingScreen.BeginLoading();
    }

    /// <summary>Opens the settings panel and hides the main menu block.</summary>
    public void OnSettings()
    {
        if (_settingsPanel == null)
        {
            Debug.LogWarning("MainMenuController: Settings panel is not assigned.", this);
            return;
        }

        _settingsPanel.SetActive(true);
        if (_menuRoot != null) _menuRoot.SetActive(false);
    }

    /// <summary>Closes the settings panel and shows the main menu block again.</summary>
    public void OnSettingsClosed()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_menuRoot != null) _menuRoot.SetActive(true);
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
