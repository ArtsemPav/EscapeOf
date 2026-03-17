using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a single popup message entry: fade-in → hold → fade-out lifecycle.
/// Instantiated and managed exclusively by PopupMessageSystem.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PopupMessageEntry : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;

    [Header("Type Colors")]
    [SerializeField] private Color hintColor    = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color eventColor   = new Color(1.00f, 0.85f, 0.20f, 1f);
    [SerializeField] private Color warningColor = new Color(0.95f, 0.25f, 0.25f, 1f);

    [Header("Animation")]
    [SerializeField] private float fadeInDuration  = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.5f;

    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Starts the full lifecycle of this popup.
    /// Calls onComplete when the entry finishes fading out.
    /// </summary>
    public void Play(PopupMessageData data, System.Action onComplete)
    {
        ApplyData(data);
        StartCoroutine(LifecycleRoutine(data.duration, onComplete));
    }

    /// <summary>Immediately begins fade-out, overriding the remaining hold time.</summary>
    public void Dismiss()
    {
        StopAllCoroutines();
        StartCoroutine(FadeRoutine(0f, fadeOutDuration, null));
    }

    private void ApplyData(PopupMessageData data)
    {
        if (messageText != null)
            messageText.text = data.text;

        Color typeColor = data.messageType switch
        {
            PopupMessageType.Event   => eventColor,
            PopupMessageType.Warning => warningColor,
            _                        => hintColor
        };

        if (messageText != null)
            messageText.color = typeColor;

        if (iconImage != null)
        {
            bool hasIcon = data.icon != null;
            iconImage.gameObject.SetActive(hasIcon);
            if (hasIcon)
            {
                iconImage.sprite = data.icon;
                iconImage.color  = typeColor;
            }
        }
    }

    private IEnumerator LifecycleRoutine(float holdDuration, System.Action onComplete)
    {
        yield return FadeRoutine(1f, fadeInDuration, null);
        yield return new WaitForSeconds(holdDuration);
        yield return FadeRoutine(0f, fadeOutDuration, null);
        onComplete?.Invoke();
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration, System.Action onComplete)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed    = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}
