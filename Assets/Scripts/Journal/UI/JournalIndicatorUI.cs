using System.Collections;
using UnityEngine;

/// <summary>
/// Shows a "Записка добавлена в журнал (J)" label on the screen for a few seconds then fades it out.
/// Attach to a Canvas UI element that has a CanvasGroup component.
/// Subscribes to JournalSystem.OnEntryAdded automatically via Start — lights up every time a new
/// document is added to the journal (replaces the old popup notification).
///
/// SETUP: Create a UI Image or Panel anchored to a corner, add a TextMeshProUGUI child with the
/// localized label, add a CanvasGroup to the root, then attach this script to the root.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class JournalIndicatorUI : MonoBehaviour
{
    [Tooltip("How long the label stays fully visible before fading out (seconds).")]
    [SerializeField] private float _displayDuration = 3f;

    [Tooltip("Fade-out duration in seconds.")]
    [SerializeField] private float _fadeDuration = 1f;

    private CanvasGroup _canvasGroup;
    private Coroutine _activeRoutine;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
    }

    private void Start()
    {
        // JournalSystem.Awake always runs before any Start, so Instance is set here if present.
        if (JournalSystem.Instance != null)
            JournalSystem.Instance.OnEntryAdded += HandleEntryAdded;
    }

    private void OnDestroy()
    {
        if (JournalSystem.Instance != null)
            JournalSystem.Instance.OnEntryAdded -= HandleEntryAdded;
    }

    /// <summary>Displays the journal indicator. Fired when a new document is added to the journal.</summary>
    public void Show()
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(ShowRoutine());
    }

    private void HandleEntryAdded(DocumentData data) => Show();

    private IEnumerator ShowRoutine()
    {
        _canvasGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(_displayDuration);

        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / _fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _activeRoutine = null;
    }
}
