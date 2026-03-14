using UnityEngine;

/// <summary>
/// Holds the content of a readable in-world note.
/// Create via Assets > Create > Escape > Note Data.
/// </summary>
[CreateAssetMenu(fileName = "NoteData", menuName = "Escape/Note Data")]
public class NoteData : ScriptableObject
{
    [Header("Content")]
    public string title = "Записка";

    [TextArea(4, 12)]
    public string content;
}
