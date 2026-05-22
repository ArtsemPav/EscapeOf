using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Base ScriptableObject for all laptop file types.</summary>
    public abstract class LaptopFileData : ScriptableObject
    {
        [Tooltip("Display name shown on the tab and desktop icon.")]
        public string fileName = "Файл";

        [Tooltip("Icon shown on the desktop.")]
        public Sprite fileIcon;

        [Tooltip("Stable unique identifier used by the save system. Assign once via context menu, never change after.")]
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
