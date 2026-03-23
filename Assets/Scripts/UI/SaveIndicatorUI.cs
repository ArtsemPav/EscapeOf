using System.Collections;
using UnityEngine;

/// <summary>
/// Shows a "Сохранение" label on the right side of the screen for a few seconds then fades it out.
/// Attach to a Canvas UI element that has a CanvasGroup component.
/// Subscribes to SaveManager.OnSaved automatically via OnEnable/OnDisable.
///
/// SETUP: Create a UI Image or Panel anchored to the right side, add a TextMeshProUGUI child with "Сохранение",
/// add a CanvasGroup to the root, then attach this script to the root.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SaveIndicatorUI : MonoBehaviour
{
    public static SaveIndicatorUI Instance { get; private set; }

    [Tooltip("How long the label stays fully visible before fading out (seconds).")]
    [SerializeField] private float displayDuration = 4f;
    [Tooltip("Fade-out duration in seconds.")]
    [SerializeField] private float fadeDuration = 1f;

    private CanvasGroup _canvasGroup;
    private Coroutine _activeRoutine;

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
    }

    private void OnEnable()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnSaved += Show;
    }

    private void OnDisable()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnSaved -= Show;
    }

    /// <summary>Displays the save indicator. Called automatically when SaveManager fires OnSaved.</summary>
    public void Show()
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        _canvasGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(displayDuration);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _activeRoutine = null;
    }
}
