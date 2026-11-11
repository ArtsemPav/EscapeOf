using UnityEngine;
using UnityEngine.Serialization;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Text document file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Text File", fileName = "NewTextFile")]
    public class LaptopTextFile : LaptopFileData
    {
        [Tooltip("Text asset with the document content in the default language.")]
        public TextAsset textFile;

        [Tooltip("Text asset with the document content in English. Used when the current language is En; fall back to textFile when empty.")]
        public TextAsset textFileEn;

        [FormerlySerializedAs("content")]
        [TextArea(5, 30)]
        [Tooltip("Fallback content used when no text file is assigned.")]
        public string fallbackContent;

        /// <summary>Full text content of the document, resolved per current language.</summary>
        public string Content
        {
            get
            {
                if (LocalizationSettings.Current == Language.En && textFileEn != null)
                    return textFileEn.text;
                return textFile != null ? textFile.text : fallbackContent;
            }
        }
    }
}
