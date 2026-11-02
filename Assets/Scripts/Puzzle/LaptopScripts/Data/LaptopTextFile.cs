using UnityEngine;
using UnityEngine.Serialization;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Text document file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Text File", fileName = "NewTextFile")]
    public class LaptopTextFile : LaptopFileData
    {
        [Tooltip("Text asset with the document content.")]
        public TextAsset textFile;

        [FormerlySerializedAs("content")]
        [TextArea(5, 30)]
        [Tooltip("Fallback content used when no text file is assigned.")]
        public string fallbackContent;

        /// <summary>Full text content of the document.</summary>
        public string Content => textFile != null ? textFile.text : fallbackContent;
    }
}
