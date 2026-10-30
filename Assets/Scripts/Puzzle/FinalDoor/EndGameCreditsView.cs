using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Full-screen credits overlay for the end of the game.
/// A Canvas with a black background and a centered TextMeshPro label.
/// The whole canvas stays active all game with alpha 0; Show() fades it in,
/// plays the credits entries (one at a time, from <see cref="_entries"/>),
/// shows the final "thanks" text, then wipes the save and returns to the
/// start scene — so the finished save cannot be loaded again.
/// Input is already disabled by EndGameSequence.
/// </summary>
public class EndGameCreditsView : MonoBehaviour
{
    public static EndGameCreditsView Instance { get; private set; }

    [Header("Canvas")]
    [Tooltip("CanvasGroup on this canvas used for the fade-in.")]
    [SerializeField] private CanvasGroup _canvasGroup;

    [Tooltip("Fade-in duration of the credits overlay background.")]
    [SerializeField, Min(0.1f)] private float _fadeInDuration = 1.2f;

    [Header("Credits Entries")]
    [Tooltip("Credits lines shown one by one before the final thanks text.")]
    [SerializeField] private CreditsEntry[] _entries = new CreditsEntry[0];

    [Tooltip("Seconds each entry stays fully visible.")]
    [SerializeField, Min(0.5f)] private float _entryHoldDuration = 3f;

    [Tooltip("Fade duration between entries.")]
    [SerializeField, Min(0.1f)] private float _entryFadeDuration = 0.8f;

    [Tooltip("Text component used for the entries and the final thanks text.")]
    [SerializeField] private TMPro.TextMeshProUGUI _creditsLabel;

    [Tooltip("Text shown after all entries have played. Stays on screen until the scene switch.")]
    [SerializeField] private string _thanksText = "Спасибо за игру";

    [Tooltip("Seconds the thanks text stays visible before returning to the start scene.")]
    [SerializeField, Min(1f)] private float _thanksHoldDuration = 5f;

    [Header("Ending Transition")]
    [Tooltip("Scene loaded after the credits finish.")]
    [SerializeField] private string _startSceneName = "Start";

    [Tooltip("If true, the default save slot is deleted before leaving — " +
             "the player cannot load the finished game.")]
    [SerializeField] private bool _deleteSaveOnFinish = true;

    private const int DefaultSaveSlot = 0;

    private bool _overlayFadedIn;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Fades the overlay in, plays all credits entries one by one, shows the
    /// final thanks text, then deletes the save and returns to the start scene.
    /// </summary>
    public Coroutine Show()
    {
        return StartCoroutine(CreditsRoutine());
    }

    /// <summary>
    /// Fades the black overlay in ONCE, on unscaled time, and returns when it
    /// is fully opaque. Call this BEFORE releasing the game's cameras - under
    /// the ScreenFader's black it is invisible, and it guarantees the credits
    /// never appear over the 3D scene while cameras are still blending.
    /// </summary>
    public Coroutine FadeOverlayIn()
    {
        if (_overlayFadedIn || _canvasGroup == null) return null;
        _overlayFadedIn = true;
        return StartCoroutine(FadeOverlayRoutine());
    }

    private IEnumerator FadeOverlayRoutine()
    {
        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeInDuration);
            yield return null;
        }
        _canvasGroup.alpha = 1f;
    }

    private IEnumerator CreditsRoutine()
    {
        if (_canvasGroup == null) yield break;

        // 1. Fade the black overlay in (skip if FadeOverlayIn already did it).
        if (!_overlayFadedIn)
        {
            _overlayFadedIn = true;
            yield return FadeOverlayRoutine();
        }
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;

        // 2. Play the authored entries, one at a time.
        if (_creditsLabel != null)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];
                if (entry == null) continue;

                _creditsLabel.text = ComposeEntryText(entry);
                yield return FadeLabelRoutine(0f, 1f, _entryFadeDuration);
                yield return new WaitForSeconds(_entryHoldDuration);
                yield return FadeLabelRoutine(1f, 0f, _entryFadeDuration);
            }

            // 3. Final thanks text — stays visible for the hold duration.
            _creditsLabel.text = _thanksText;
            yield return FadeLabelRoutine(0f, 1f, _entryFadeDuration);
        }

        yield return new WaitForSeconds(_thanksHoldDuration);

        // 4. Wipe the save so the finished game cannot be loaded again.
        if (_deleteSaveOnFinish)
        {
            SaveManager.ResetActiveSlot();
            if (SaveManager.Instance != null)
                SaveManager.Instance.DeleteSave(DefaultSaveSlot);
            else
                Debug.LogWarning("[EndGameCreditsView] SaveManager instance missing — save not deleted.");
        }

        // 5. Return the cursor so the main menu is usable, and destroy ALL
        //    DontDestroyOnLoad singletons (same pattern as LoadSaveBtn) so the
        //    start scene gets fresh instances — the persistent InputManager is
        //    still holding disabled action maps from the endgame cinematic,
        //    which would leave the menu unclickable.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        if (SaveManager.Instance != null) Destroy(SaveManager.Instance.gameObject);
        if (AudioManager.Instance != null) Destroy(AudioManager.Instance.gameObject);
        if (InputManager.Instance != null) Destroy(InputManager.Instance.gameObject);
        if (ResolutionManager.Instance != null) Destroy(ResolutionManager.Instance.gameObject);
        if (PopupMessageSystem.Instance != null) Destroy(PopupMessageSystem.Instance.gameObject);

        Time.timeScale = 1f;
        SceneManager.LoadScene(_startSceneName);
    }

    /// <summary>Title above text, or the text alone when the title is empty.</summary>
    private static string ComposeEntryText(CreditsEntry entry)
    {
        string title = entry.Title;
        string text = entry.Text;
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool hasText = !string.IsNullOrWhiteSpace(text);

        if (hasTitle && hasText) return $"{title}\n\n{text}";
        if (hasTitle) return title;
        return hasText ? text : string.Empty;
    }

    /// <summary>Fades the credits label's alpha from-to over the duration (unscaled time).</summary>
    private IEnumerator FadeLabelRoutine(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _creditsLabel.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _creditsLabel.alpha = to;
    }
}
