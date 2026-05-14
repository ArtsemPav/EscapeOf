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
    }
}
