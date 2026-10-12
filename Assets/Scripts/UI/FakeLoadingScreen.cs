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

    /// <summary>
    /// Shows the loading screen, animates the bar from 0 to 100% while the scene
    /// loads in the background, then activates the gameplay scene.
    /// </summary>
    public void BeginLoading()
    {
        // Activate before starting the coroutine: coroutines cannot run on an inactive GameObject.
        gameObject.SetActive(true);
        _fillRoutine = StartCoroutine(FillAndLoad());
    }

    private IEnumerator FillAndLoad()
    {
        // Block clicks through the overlay.
        _progressSlider.interactable = false;

        // Start fully empty and let the overlay render before any heavy work.
        _progressSlider.value = 0f;
        UpdateProgressLabel(0f);
        yield return null;

        // Lower loading-thread priority so background loading barely touches the main thread.
        Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
        _sceneLoadOperation = SceneManager.LoadSceneAsync(_sceneToLoad);
        _sceneLoadOperation.allowSceneActivation = false;

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

        // Scene assets are already streamed in the background — activation is instant.
        _sceneLoadOperation.allowSceneActivation = true;
        while (!_sceneLoadOperation.isDone)
        {
            yield return null;
        }
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
