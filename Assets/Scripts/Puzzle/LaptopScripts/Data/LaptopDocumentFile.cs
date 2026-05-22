using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Multi-page document file data containing an array of sprites.</summary>
    [CreateAssetMenu(fileName = "NewDocumentFile", menuName = "Laptop/Document File")]
    public class LaptopDocumentFile : LaptopFileData
    {
        [Tooltip("Array of sprites representing the pages of the document.")]
        public Sprite[] pages;
    }
}
