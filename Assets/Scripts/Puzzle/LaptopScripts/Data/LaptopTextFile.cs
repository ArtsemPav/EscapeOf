using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Text document file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Text File", fileName = "NewTextFile")]
    public class LaptopTextFile : LaptopFileData
    {
        [TextArea(5, 30)]
        [Tooltip("Full text content of the document.")]
        public string content;
    }
}
