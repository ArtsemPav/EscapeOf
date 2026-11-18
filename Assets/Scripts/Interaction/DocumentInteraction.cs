using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to any in-world document object. Two behaviors, selected by
/// <see cref="DocumentData.pickUpToJournal"/>:
///  - normal: opens the DocumentUI panel; the object stays in the scene;
///  - pickup: the document is first read through DocumentUI; when the reading panel
///    closes, it is added to the journal (with a popup notification) and the world
///    object is destroyed — afterwards it is readable only from the journal (J).
/// </summary>
public class DocumentInteraction : MonoBehaviour, IInteractable
{
    private const float JournalPopupCooldown = 600f; // seconds — suppress the hint for 10 minutes

    // Time of the last journal popup (unscaled), shared across all document instances.
    private static float _lastJournalPopupTime = float.NegativeInfinity;

    [SerializeField, Tooltip("Иконка прицела при наведении на документ. Выбирается из списка режимов — спрайты задаются в InteractionUI (Crosshair Sprites).")]
    private CrosshairMode _crosshairMode = CrosshairMode.Read;

    [SerializeField, Tooltip("Показывать текстовую подсказку («Прочитать»/«Поднять») под иконкой прицела. По умолчанию выключено — только иконка.")]
    private bool _showInteractText = false;

    [SerializeField] private DocumentData _documentData;

    private bool _subscribed;

    private IEnumerator Start()
    {
        // SaveManager distributes loaded state in its Start(); wait until it has settled
        // (its first Update — after every Start in the scene has run).
        while (SaveManager.Instance != null && !SaveManager.InstanceHasSettled)
            yield return null;

        // A pickup document already unlocked in the journal (loaded save) must not appear again.
        if (_documentData != null &&
            _documentData.pickUpToJournal &&
            JournalSystem.Instance != null &&
            JournalSystem.Instance.IsUnlocked(_documentData))
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (_subscribed && DocumentUI.Instance != null)
            DocumentUI.Instance.OnDocumentClosed -= HandleDocumentClosed;
    }

    /// <summary>
    /// Opens the document reading panel. For pickup documents the journal entry is
    /// registered when the reading panel closes (see HandleDocumentClosed).
    /// </summary>
    public void Interact()
    {
        if (_documentData == null) return;

        if (_documentData.pickUpToJournal)
        {
            // Subscribe lazily — DocumentUI.Instance is guaranteed alive by interact time.
            if (!_subscribed && DocumentUI.Instance != null)
            {
                DocumentUI.Instance.OnDocumentClosed += HandleDocumentClosed;
                _subscribed = true;
            }
        }
        else
        {
            JournalSystem.Instance?.AddEntry(_documentData);
        }

        DocumentUI.Instance.Open(_documentData);
    }

    /// <summary>Returns the localized document interaction hint (empty — icon only by default).</summary>
    public string GetInteractText()
    {
        if (!_showInteractText || _documentData == null) return string.Empty;

        return _documentData.pickUpToJournal
            ? Localization.Get(LocalizationKeys.InteractPickUp)
            : Localization.Get(LocalizationKeys.InteractRead);
    }

    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => _crosshairMode;

    /// <summary>
    /// Called when any document reading panel closes. For this component's pickup
    /// document: registers the journal entry, shows the popup and removes the world object.
    /// </summary>
    private void HandleDocumentClosed(DocumentData data)
    {
        if (data != _documentData || !_documentData.pickUpToJournal) return;

        JournalSystem.Instance?.AddEntry(_documentData);

        string message = _documentData.isNote
            ? Localization.Get(LocalizationKeys.JournalNoteAdded)
            : Localization.Get(LocalizationKeys.JournalDocumentAdded);

        // Don't spam the hint: suppress if a journal popup was shown recently.
        if (Time.unscaledTime - _lastJournalPopupTime >= JournalPopupCooldown)
        {
            PopupMessageSystem.Instance?.Show(message, PopupMessageType.Hint);
            _lastJournalPopupTime = Time.unscaledTime;
        }

        Destroy(gameObject);
    }
}
