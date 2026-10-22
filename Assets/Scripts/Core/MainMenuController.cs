using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu button interactions. Every screen change goes through the
/// shared ScreenFader black overlay: fade to black, swap the visible panel,
/// fade back in. Attach to MainMenuCanvas. Wire buttons via Inspector onClick.
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

    [Header("Menu Items")]
    [Tooltip("Load Game button, hidden while no save file exists.")]
    [SerializeField] private GameObject _loadGameButton;

    /// <summary>Hides the Load Game button when there is nothing to load.</summary>
    private void Start()
    {
        if (_loadGameButton != null)
        {
            _loadGameButton.SetActive(SaveManager.SlotHasSave(DefaultSaveSlot));
        }
    }

    /// <summary>Starts a new game: clears the default save slot, fades to the loading screen.</summary>
    /// <remarks>The wake-up intro is triggered by SaveManager itself: after the slot
    /// is cleared, SaveManager.Start() in the gameplay scene finds no save and
    /// requests the intro — a single decision point for all entry paths.</remarks>
    public void OnNewGame()
    {
        SaveManager.StartNewGame(DefaultSaveSlot);
        BeginGameTransition();
    }
    /// <summary>Loads the default save slot into the gameplay scene. Does nothing when no save exists.</summary>
    /// <remarks>Double-checks the save file: after the credits finish, the save
    /// is deleted and the start scene is shown — the finished game must not be
    /// loadable, even if the button was still visible for a frame.</remarks>
    public void OnLoadGame()
    {
        if (!SaveManager.SlotHasSave(DefaultSaveSlot))
        {
            Debug.LogWarning("MainMenuController: Load skipped — no save file found.", this);
            if (_loadGameButton != null) _loadGameButton.SetActive(false);
            return;
        }

        SaveManager.RequestLoadFromSlot(DefaultSaveSlot);
        BeginGameTransition();
    }

    /// <summary>
    /// Black-fade transition: fade to black, show the loading screen behind it,
    /// then fade back in so the player sees the bar already at zero.
    /// </summary>
    private void BeginGameTransition()
    {
        StartCoroutine(GameTransitionRoutine());
    }

    private IEnumerator GameTransitionRoutine()
    {
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn();

        if (_menuRoot != null) _menuRoot.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);

        if (_loadingScreen == null)
        {
            Debug.LogWarning("MainMenuController: Loading screen is not assigned, loading scene directly.", this);
            SceneManager.LoadScene(GameplaySceneName);
            yield break;
        }

        _loadingScreen.BeginLoading();
        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeOut();
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
