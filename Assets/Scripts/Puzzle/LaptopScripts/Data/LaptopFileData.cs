using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Base ScriptableObject for all laptop file types.</summary>
    public abstract class LaptopFileData : ScriptableObject
    {
        /// <summary>Localized display name: fileNameKey from the table, or the baked fileName fallback.</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(fileNameKey)) return fileName;
                string value;
                return Localization.TryGet(fileNameKey, out value) ? value : fileName;
            }
        }

        [Tooltip("Display name shown on the tab and desktop icon. Fallback used when fileNameKey is empty or missing from the localization tables.")]
        public string fileName = "Файл";

        [Tooltip("Localization key for the display name (laptop.file.<id>). Empty value — use the baked fileName. Each file asset has its own key.")]
        public string fileNameKey = "";

        [Tooltip("Icon shown on the desktop.")]
        public Sprite fileIcon;

        [Tooltip("Stable unique identifier used by the save system. Use the button in the Inspector to assign once, never change after.")]
        [FileId]
        public string fileId = "";

#if UNITY_EDITOR
        [ContextMenu("Generate File ID")]
        private void GenerateFileId()
        {
            if (!string.IsNullOrEmpty(fileId)) return;
            fileId = System.Guid.NewGuid().ToString();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
