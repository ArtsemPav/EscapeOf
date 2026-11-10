using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Image file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Image File", fileName = "NewImageFile")]
    public class LaptopImageFile : LaptopFileData
    {
        [Tooltip("Sprite displayed in the image viewer.")]
        public Sprite image;

        [Tooltip("Префикс локализации: ключ — 'laptop.<fileId>'. Когда ключ есть в таблице спрайтов, " +
                 "переведённая картинка заменяет оригинал. Пусто — используется fileId.")]
        public string localizationPrefix;

        /// <summary>Returns the localized sprite, or the original when missing.</summary>
        public Sprite GetLocalizedImage()
        {
            string prefix = string.IsNullOrEmpty(localizationPrefix) ? fileId : localizationPrefix;
            if (string.IsNullOrEmpty(prefix)) return image;
            return Localization.GetSprite($"laptop.{prefix}") ?? image;
        }
    }
}
