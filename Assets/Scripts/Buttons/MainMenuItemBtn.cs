using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pause-menu button that returns to the main menu (Start scene) with the
/// same full transition as the start scene's New Game: fade to black,
/// reveal the FakeLoadingScreen, let it run its progress-bar cycle while
/// the scene streams in, then fade to black again before activation.
/// </summary>
public class MainMenuItemBtn : BaseButton
{
    private const string MainMenuSceneName = "Start";

    [Tooltip("Root object of the loading screen overlay (FakeLoadingScreen).")]
    [SerializeField] private GameObject _loadingScreen;

    protected override void OnClick()
    {
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private IEnumerator ReturnToMainMenuRoutine()
    {
        // Fade to black first, then hide the pause menu behind the black screen.
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn();

        // The start scene boots with its own MusicDirector state (Menu_Main from
        // Start()) — the gameplay soundscape must be silent before the transition,
        // not killed mid-fade by the AudioManager destruction below.
        MusicDirector.FadeOutForLoadingScreen(caller: "MainMenuItemBtn.ReturnToMainMenu");

        // Same cleanup as EndGameCreditsView and LoadSaveBtn: the start scene
        // must boot fresh. A surviving GameManager would keep the cursor
        // locked and keep ESC-pausing the menu animations; the persistent
        // InputManager would keep routing ESC into TogglePause.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        if (SaveManager.Instance != null) Destroy(SaveManager.Instance.gameObject);
        if (AudioManager.Instance != null) Destroy(AudioManager.Instance.gameObject);
        if (InputManager.Instance != null) Destroy(InputManager.Instance.gameObject);
        if (ResolutionManager.Instance != null) Destroy(ResolutionManager.Instance.gameObject);
        if (PopupMessageSystem.Instance != null) Destroy(PopupMessageSystem.Instance.gameObject);

        // Reset pause side effects without going through GameManager —
        // it is already scheduled for destruction, and closing the menuUI
        // panel here would deactivate this button's GameObject and kill
        // this very coroutine.
        // Time scale must return to 1 — the Start scene has no GameManager
        // to unpause its scaled-time UI.
        Time.timeScale = 1f;

        // AudioListener.pause deliberately STAYS true here. The gameplay scene
        // keeps existing underneath the loading screen until the Start scene
        // activates, and unpausing now would resurrect every ambience loop,
        // one-shot and stinger of the dying scene for the whole fake load.
        // The freshly booted AudioManager in the Start scene clears the pause
        // in Awake (fresh audio stack = fresh listener state).

        // The interaction panel renders above the loading screen — hide the
        // crosshair and hint HUD for the duration of the transition.
        InteractionUI.Instance?.SetVisible(false);

        if (_loadingScreen == null)
        {
            Debug.LogWarning("MainMenuItemBtn: Loading screen is not assigned, loading scene directly.", this);
            SceneManager.LoadScene(MainMenuSceneName);
            yield break;
        }

        // The loading screen art matches the live building state: the powered
        // art once the electric puzzle has been solved, the dark one before.
        var loadingScreen = _loadingScreen.GetComponent<FakeLoadingScreen>();
        if (loadingScreen != null)
        {
            BuildingProgressStage stage = LightingSystem.Instance != null
                ? LightingSystem.Instance.GetProgressStage()
                : BuildingProgressStage.NoPower;
            loadingScreen.SetBackgroundStage(stage);
        }

        // FakeLoadingScreen streams the Start scene while the bar fills and
        // fades to black itself right before the scene activates — same
        // beats as the start scene's New Game transition.
        _loadingScreen.SetActive(true);
        loadingScreen.BeginLoading();

        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeOut();
    }
}
