using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Image file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Image File", fileName = "NewImageFile")]
    public class LaptopImageFile : LaptopFileData
    {
        [Tooltip("Sprite displayed in the image viewer.")]
        public Sprite image;
    }
}
