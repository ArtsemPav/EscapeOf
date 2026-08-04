using System.Collections;
using UnityEngine;

/// <summary>
/// Full-screen fade overlay. Place on a Canvas with an Image child.
/// Call FadeIn (to black) / FadeOut (to clear) from any system.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [SerializeField, Min(0f)] private float _defaultFadeDuration = 1f;

    private CanvasGroup _canvasGroup;
    private Coroutine _activeFade;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
    }

    /// <summary>Fades the screen to black over the default duration.</summary>
    public Coroutine FadeIn() => FadeIn(_defaultFadeDuration);

    /// <summary>Fades the screen to black over the given duration.</summary>
    public Coroutine FadeIn(float duration) => StartFade(1f, duration);

    /// <summary>Fades the screen to the given target alpha over the given duration.</summary>
    /// <param name="duration">Fade duration in seconds.</param>
    /// <param name="targetAlpha">Target alpha: 1 = fully black, 0 = fully clear.</param>
    public Coroutine FadeIn(float duration, float targetAlpha) => StartFade(targetAlpha, duration);

    /// <summary>Fades the screen from black to clear over the default duration.</summary>
    public Coroutine FadeOut() => FadeOut(_defaultFadeDuration);

    /// <summary>Fades the screen from black to clear over the given duration.</summary>
    public Coroutine FadeOut(float duration) => StartFade(0f, duration);

    private Coroutine StartFade(float targetAlpha, float duration)
    {
        if (_activeFade != null)
            StopCoroutine(_activeFade);

        _canvasGroup.blocksRaycasts = targetAlpha > 0f;
        _activeFade = StartCoroutine(FadeRoutine(targetAlpha, duration));
        return _activeFade;
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        _canvasGroup.blocksRaycasts = targetAlpha > 0f;
        _activeFade = null;
    }
}
