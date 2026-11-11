using UnityEngine;

/// <summary>
/// Attach to any in-world document object. On interact, opens the DocumentUI panel.
/// The object stays in the scene — no inventory pickup needed.
/// </summary>
public class DocumentInteraction : MonoBehaviour, IInteractable
{
    [SerializeField] private DocumentData _documentData;

    [Tooltip("Текст подсказки при наведении на объект.")]
    [SerializeField] private string _interactText = "Прочитать";

    /// <summary>Opens the document reading panel and registers the journal entry (first read only).</summary>
    public void Interact()
    {
        if (_documentData == null) return;

        JournalSystem.Instance?.AddEntry(_documentData);
        DocumentUI.Instance.Open(_documentData);
    }

    /// <summary>Returns the localized document interaction hint, or the Inspector fallback.</summary>
    public string GetInteractText()
    {
        // Keep the Inspector fallback when this localization key is missing.
        if (Localization.TryGet(LocalizationKeys.InteractRead, out string localizedHint))
            return localizedHint;
        return _interactText;
    }
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;
}
