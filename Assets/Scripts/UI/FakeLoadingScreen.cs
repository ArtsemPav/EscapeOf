using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fake full-screen loading overlay with an animated progress bar.
/// The gameplay scene starts loading in the background while the bar fills;
/// it is activated as soon as both the bar is full and the scene is ready,
/// so the frame never freezes on a synchronous load.
/// Attach to the loading screen root and assign references in the Inspector.
/// </summary>
public class FakeLoadingScreen : MonoBehaviour
{
    private const float FillDurationSeconds = 2.2f;
    private const float DisplayedProgressCap = 0.99f;
    private const float MinSceneReadyDelaySeconds = 0.2f;
    private const float RevealHeadStartSeconds = 1.2f;
    // A long hitch (e.g. from scene deserialization) must not jump the bar forward.
    private const float MaxFrameDeltaSeconds = 0.05f;

    [Header("UI")]
    [Tooltip("Slider used as the fake progress bar (disabled for user input).")]
    [SerializeField] private Slider _progressSlider;

    [Tooltip("Optional percentage label under the bar.")]
    [SerializeField] private TMPro.TextMeshProUGUI _progressLabel;

    [Header("Scene")]
    [Tooltip("Gameplay scene loaded after the bar fills.")]
    [SerializeField] private string _sceneToLoad = "Game";

    private Coroutine _fillRoutine;
    private AsyncOperation _sceneLoadOperation;
    private bool _sceneReady;

    /// <summary>Shows the loading screen with the bar at zero, without starting the fill.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        _progressSlider.interactable = false;
        _progressSlider.value = 0f;
        UpdateProgressLabel(0f);
    }

    /// <summary>
    /// Shows the loading screen and animates the bar from 0 to 100% while the
    /// gameplay scene loads in the background. When the bar is full, the shared
    /// ScreenFader fades the screen to black before the scene activates.
    /// Call Show() first and let the reveal fade finish, so the heavy scene
    /// load never freezes the fade mid-way (see MainMenuController).
    /// </summary>
    public void BeginLoading()
    {
        Show();
        _fillRoutine = StartCoroutine(FillAndLoad());
    }

    private IEnumerator FillAndLoad()
    {
        // Give the reveal fade a head start: for the first beats the heavy
        // scene load competes with rendering, so postpone it until the screen
        // is visually back and the bar is already moving.
        yield return new WaitForSecondsRealtime(RevealHeadStartSeconds);

        Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
        _sceneLoadOperation = SceneManager.LoadSceneAsync(_sceneToLoad);
        _sceneLoadOperation.allowSceneActivation = false;

        // Block clicks through the overlay.
        _progressSlider.interactable = false;
        _progressSlider.value = 0f;
        UpdateProgressLabel(0f);
        yield return null;

        float elapsed = 0f;
        while (elapsed < FillDurationSeconds)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSeconds);
            float linear = Mathf.Clamp01(elapsed / FillDurationSeconds);
            // Ease-out so the bar feels like a real loading queue.
            float eased = 1f - (1f - linear) * (1f - linear);
            float progress = Mathf.Clamp01(eased * (1f / DisplayedProgressCap)) * DisplayedProgressCap;
            _progressSlider.value = progress;
            UpdateProgressLabel(progress);
            yield return null;
        }

        // Hold at full for a short beat so the player sees the complete bar.
        _progressSlider.value = 1f;
        UpdateProgressLabel(1f);
        yield return new WaitForSecondsRealtime(MinSceneReadyDelaySeconds);

        // Hide the scene switch: fade to black first, then activate — the
        // assets are already streamed, so activation lands on a black screen.
        if (ScreenFader.Instance != null)
        {
            yield return ScreenFader.Instance.FadeIn();
            _sceneLoadOperation.allowSceneActivation = true;
            yield break;
        }

        Debug.LogWarning("FakeLoadingScreen: ScreenFader is missing in this scene, activating scene directly.", this);
        _sceneLoadOperation.allowSceneActivation = true;
    }

    private void UpdateProgressLabel(float progress)
    {
        if (_progressLabel != null)
        {
            _progressLabel.text = $"{Mathf.RoundToInt(progress * 100f)}%";
        }
    }

    /// <summary>Stops the animation if the object is disabled mid-run.</summary>
    private void OnDisable()
    {
        if (_fillRoutine != null)
        {
            StopCoroutine(_fillRoutine);
            _fillRoutine = null;
        }
    }
}
