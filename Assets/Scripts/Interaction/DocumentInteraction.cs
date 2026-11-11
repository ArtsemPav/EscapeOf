using UnityEngine;

/// <summary>
/// Attach to any in-world document object. On interact, opens the DocumentUI panel.
/// The object stays in the scene — no inventory pickup needed.
/// </summary>
public class DocumentInteraction : MonoBehaviour, IInteractable
{
    [SerializeField] private DocumentData _documentData;

    /// <summary>Opens the document reading panel and registers the journal entry (first read only).</summary>
    public void Interact()
    {
        if (_documentData == null) return;

        JournalSystem.Instance?.AddEntry(_documentData);
        DocumentUI.Instance.Open(_documentData);
    }

    /// <summary>Returns the localized document interaction hint.</summary>
    public string GetInteractText()
        => Localization.Get(LocalizationKeys.InteractRead);
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;
}
