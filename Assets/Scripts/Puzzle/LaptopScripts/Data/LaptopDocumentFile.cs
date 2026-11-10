using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Multi-page document file data containing an array of sprites.</summary>
    [CreateAssetMenu(fileName = "NewDocumentFile", menuName = "Laptop/Document File")]
    public class LaptopDocumentFile : LaptopFileData
    {
        [Tooltip("Array of sprites representing the pages of the document.")]
        public Sprite[] pages;

        [Tooltip("Префикс локализации страниц: ключ страницы — 'laptop.<prefix>.page<n>' (n с 0). " +
                 "Когда ключ есть в таблице спрайтов, переведённая страница заменяет оригинал. Пусто — оригинал всегда.")]
        public string localizationPrefix;

        /// <summary>Returns the localized sprite for page n, or the original when missing.</summary>
        public Sprite GetLocalizedPage(int pageIndex)
        {
            Sprite original = pages != null && pageIndex < pages.Length ? pages[pageIndex] : null;
            if (string.IsNullOrEmpty(localizationPrefix)) return original;
            return Localization.GetSprite($"laptop.{localizationPrefix}.page{pageIndex}") ?? original;
        }
    }
}
