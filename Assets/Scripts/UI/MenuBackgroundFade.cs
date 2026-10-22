using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Covers the screen with a black overlay at scene start and fades it out
/// shortly after the menu appears, so the scene reveals gradually from black.
/// The fade starts in parallel with the menu pop-up animation — no need to
/// wait for it to finish. Attach to a full-screen Image child of the menu
/// canvas and assign the overlay reference.
/// </summary>
public class MenuBackgroundFade : MonoBehaviour
{
    private const float MinAlphaStep = 0.001f;
    // A startup hitch (scene deserialization, shader warmup) must not jump the fade forward.
    private const float MaxFrameDeltaSeconds = 0.05f;

    [Tooltip("Overlay Image that covers the screen; its color alpha is animated.")]
    [SerializeField] private Image _overlay;

    [Tooltip("Pause before the fade begins, letting the menu pop-up animation get underway.")]
    [SerializeField] private float _delayBeforeFadeSeconds = 0.5f;

    [Tooltip("Overlay fade-out duration in seconds.")]
    [SerializeField] private float _fadeDurationSeconds = 2.5f;

    private Coroutine _fadeRoutine;

    private void Start()
    {
        if (_overlay == null)
        {
            Debug.LogWarning("MenuBackgroundFade: Overlay image is not assigned.", this);
            enabled = false;
            return;
        }

        SetAlpha(1f);
        _fadeRoutine = StartCoroutine(FadeOut());
    }

    private void OnDisable()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    /// <summary>Waits for the start delay, then eases the overlay alpha from 1 to 0.</summary>
    private IEnumerator FadeOut()
    {
        yield return new WaitForSecondsRealtime(_delayBeforeFadeSeconds);

        float elapsed = 0f;
        while (elapsed < _fadeDurationSeconds)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSeconds);
            float linear = Mathf.Clamp01(elapsed / _fadeDurationSeconds);
            // Smooth-step easing: slow start, steady middle, gentle landing.
            float eased = linear * linear * (3f - 2f * linear);
            SetAlpha(1f - eased);
            yield return null;
        }

        SetAlpha(0f);
        _overlay.gameObject.SetActive(false);
        _fadeRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (alpha < MinAlphaStep) alpha = 0f;
        Color color = _overlay.color;
        color.a = alpha;
        _overlay.color = color;
    }
}
