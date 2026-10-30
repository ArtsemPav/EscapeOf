using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Handles main menu button interactions. Every screen change goes through the
/// shared ScreenFader black overlay: fade to black, swap the visible panel,
/// fade back in. Attach to MainMenuCanvas. Wire buttons via Inspector onClick.
/// Also wires hover and click sounds to every Button under the canvas
/// (main menu, settings submenu and any future panels) through AudioManager.
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

    [Tooltip("Fade-to-black duration of the new game / load game transition. Should cover the camera loading push (MenuCameraDolly._loadingPushDurationSeconds).")]
    [SerializeField, Min(0.1f)] private float _transitionFadeSeconds = 2.2f;

    [Header("Menu Items")]
    [Tooltip("Load Game button, hidden while no save file exists.")]
    [SerializeField] private GameObject _loadGameButton;

    [Tooltip("Menu camera dolly that plays the push-forward beat on this transition.")]
    [SerializeField] private MenuCameraDolly _cameraDolly;

    [Header("Menu Sounds")]
    [Tooltip("Played once when the cursor enters any menu button (including submenus).")]
    [SerializeField] private AudioClip _hoverClip;

    [Tooltip("Played when any menu button is clicked.")]
    [SerializeField] private AudioClip _clickClip;

    [Tooltip("Hover sound volume on the SFX bus.")]
    [SerializeField] [Range(0f, 1f)] private float _hoverVolume = 0.7f;

    [Tooltip("Click sound volume on the SFX bus.")]
    [SerializeField] [Range(0f, 1f)] private float _clickVolume = 0.7f;

    private readonly List<Button> _soundWiredButtons = new List<Button>();

    private GraphicRaycaster _canvasRaycaster;
    private bool _inputLocked;

    private void Awake()
    {
        _canvasRaycaster = GetComponent<GraphicRaycaster>();
        WireMenuSounds();
    }

    private void OnDestroy()
    {
        foreach (Button wiredButton in _soundWiredButtons)
        {
            if (wiredButton != null)
                wiredButton.onClick.RemoveListener(PlayClickSound);
        }
    }

    /// <summary>
    /// Wires hover and click sounds to every Button under this canvas,
    /// including buttons on currently inactive panels (settings submenu).
    /// </summary>
    private void WireMenuSounds()
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            button.onClick.AddListener(PlayClickSound);

            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry onEnter = new EventTrigger.Entry();
            onEnter.eventID = EventTriggerType.PointerEnter;
            onEnter.callback.AddListener((_) => PlayHoverSound());
            trigger.triggers.Add(onEnter);

            _soundWiredButtons.Add(button);
        }
    }

    /// <summary>Plays the hover sound through the AudioManager SFX service.</summary>
    private void PlayHoverSound()
    {
        if (_hoverClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_hoverClip, _hoverVolume);
    }

    /// <summary>Plays the click sound through the AudioManager SFX service.</summary>
    private void PlayClickSound()
    {
        if (_clickClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_clickClip, _clickVolume);
    }

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
        if (_inputLocked) return;

        SaveManager.StartNewGame(DefaultSaveSlot);
        BeginGameTransition();
    }
    /// <summary>Loads the default save slot into the gameplay scene. Does nothing when no save exists.</summary>
    /// <remarks>Double-checks the save file: after the credits finish, the save
    /// is deleted and the start scene is shown — the finished game must not be
    /// loadable, even if the button was still visible for a frame.</remarks>
    public void OnLoadGame()
    {
        if (_inputLocked) return;

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
        if (_inputLocked) return;
        _inputLocked = true;

        // Block all pointer input on the canvas for the whole transition
        // (camera push + fades): no more hovers or clicks on menu buttons.
        if (_canvasRaycaster != null) _canvasRaycaster.enabled = false;

        // The loading screen takes over: menu music fades to silence while the
        // bar fills, so the gameplay scene starts from a clean soundscape
        // (GameManager.SetPause → PlayGameMusic starts Explore_Main from zero).
        MusicDirector.FadeOutForLoadingScreen(caller: "MainMenuController.BeginGameTransition");

        // The camera leans forward into the scene; the black fade covers the end of the push.
        if (_cameraDolly != null) _cameraDolly.BeginLoadingPush();

        StartCoroutine(GameTransitionRoutine());
    }

    private IEnumerator GameTransitionRoutine()
    {
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_transitionFadeSeconds);

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
        if (_inputLocked) return;

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
        if (_inputLocked) return;

        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_menuRoot != null) _menuRoot.SetActive(true);
    }

    /// <summary>Quits the application. In Editor exits Play mode instead.</summary>
    public void OnExit()
    {
        if (_inputLocked) return;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
