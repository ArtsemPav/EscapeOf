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

    /// <summary>Opens the document reading panel.</summary>
    public void Interact()
    {
        if (_documentData != null)
            DocumentUI.Instance.Open(_documentData);
    }

    public string GetInteractText() => _interactText;
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;
}
