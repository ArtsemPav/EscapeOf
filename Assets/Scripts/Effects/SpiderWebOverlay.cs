using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen spider web overlay shown when the player touches a spider web.
/// Fades out and hides itself after the configured duration.
/// </summary>
public class SpiderWebOverlay : MonoBehaviour
{
    public static SpiderWebOverlay Instance { get; private set; }

    [Header("Overlay Settings")]
    [Tooltip("Full-screen Image with the spider web texture.")]
    [SerializeField] private Image webImage;

    [Tooltip("Fade out duration (seconds) after the main display time ends.")]
    [SerializeField] private float fadeOutDuration = 1.5f;

    [Tooltip("Начальный масштаб спрайта при появлении (1 = размер оверлея).")]
    [SerializeField, Range(0.1f, 1f)] private float startScale = 0.3f;

    [Tooltip("Сколько секунд длится «набрасывание» паутины на экран.")]
    [SerializeField] private float scaleInDuration = 0.4f;

    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        Instance = this;
        if (webImage != null) Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Shows the overlay: the web "throws" onto the screen, scaling up from
    /// startScale to full size, holds for the duration, then fades out.
    /// </summary>
    public void Show(float duration)
    {
        if (webImage == null) return;
        webImage.enabled = true;
        webImage.color = new Color(1f, 1f, 1f, 1f);

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeOutAndHide(duration, scaleInDuration));

        StartCoroutine(ScaleIn());
    }

    private void Hide()
    {
        webImage.color = new Color(1f, 1f, 1f, 0f);
        webImage.enabled = false;
        webImage.rectTransform.localScale = Vector3.one;
    }

    private IEnumerator ScaleIn()
    {
        RectTransform rect = webImage.rectTransform;
        float t = 0f;
        while (t < scaleInDuration)
        {
            t += Time.deltaTime;
            // EaseOutCubic — быстрый старт, плавное прибытие
            float progress = 1f - Mathf.Pow(1f - t / scaleInDuration, 3f);
            float s = Mathf.Lerp(startScale, 1f, progress);
            rect.localScale = new Vector3(s, s, s);
            yield return null;
        }
        rect.localScale = Vector3.one;
    }

    private IEnumerator FadeOutAndHide(float duration, float scaleInTime)
    {
        yield return new WaitForSeconds(duration + scaleInTime);

        float t = 0f;
        Color start = webImage.color;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            webImage.color = new Color(start.r, start.g, start.b, 1f - t / fadeOutDuration);
            yield return null;
        }

        Hide();
        _fadeCoroutine = null;
    }
}
