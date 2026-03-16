using UnityEngine;

/// <summary>
/// Attach to any in-world note object. On interact, opens the NoteUI panel.
/// The object stays in the scene — no inventory pickup needed.
/// </summary>
public class NoteInteraction : MonoBehaviour, IInteractable
{
    [SerializeField] private NoteData _noteData;
    [SerializeField] private string _interactText = "Прочитать";

    /// <summary>Opens the note reading panel.</summary>
    public void Interact()
    {
        if (_noteData != null)
            NoteUI.Instance.Open(_noteData);
    }

    public string GetInteractText() => _interactText;
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;
}
