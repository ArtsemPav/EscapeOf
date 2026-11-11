using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Window for viewing multi-page documents.
    /// Supports page navigation and page counter display.
    /// </summary>
    public class LaptopDocumentWindow : LaptopWindow
    {
        [Header("Display")]
        [SerializeField] private Image _pageTemplate;
        [SerializeField] private Image _separatorTemplate;
        [SerializeField] private TMP_Text _pageCounter;
        [SerializeField] private ScrollRect _scrollRect;

        private const float SeparatorHeight = 8f;
        private const string PageCounterFormat = "{0} pages";
        private const int PageLayoutPriority = 1;

        private LaptopDocumentFile _docFile;
        private readonly List<Image> _instantiatedPages = new List<Image>();
        private readonly List<Image> _instantiatedSeparators = new List<Image>();
        private Coroutine _pageSizingRoutine;

        protected override void Awake()
        {
            base.Awake();
            if (_pageTemplate != null)
                _pageTemplate.gameObject.SetActive(false);
            if (_separatorTemplate != null)
                _separatorTemplate.gameObject.SetActive(false);
        }

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopDocumentFile doc)) return;

            _docFile = doc;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            ClearContent();

            if (_docFile == null || _docFile.pages == null || _docFile.pages.Length == 0)
            {
                if (_pageCounter != null) _pageCounter.text = string.Format(PageCounterFormat, 0);
                return;
            }

            if (_pageTemplate == null || _scrollRect == null || _scrollRect.content == null)
            {
                Debug.LogError("LaptopDocumentWindow: Missing required references for display.");
                return;
            }

            // Collect valid pages first so a null sprite never leaves a trailing separator.
            List<Sprite> validPages = new List<Sprite>();
            for (int i = 0; i < _docFile.pages.Length; i++)
            {
                Sprite sprite = _docFile.GetLocalizedPage(i);
                if (sprite != null) validPages.Add(sprite);
            }

            for (int i = 0; i < validPages.Count; i++)
            {
                CreatePage(validPages[i]);

                if (i < validPages.Count - 1)
                    CreateSeparator();
            }

            if (_pageCounter != null)
                _pageCounter.text = string.Format(PageCounterFormat, _instantiatedPages.Count);

            if (_pageSizingRoutine != null) StopCoroutine(_pageSizingRoutine);
            _pageSizingRoutine = StartCoroutine(SizePagesWhenLaidOut());
        }

        /// <summary>Instantiates a page for the sprite and prepares it for width-driven sizing.</summary>
        private void CreatePage(Sprite sprite)
        {
            Image newPage = Instantiate(_pageTemplate, _scrollRect.content);
            newPage.gameObject.SetActive(true);
            newPage.name = $"Page_{_instantiatedPages.Count + 1}";
            newPage.sprite = sprite;

            // The layout group controls the width; the height is derived from the
            // sprite aspect ratio once the laid-out content width is known.
            // The LayoutElement (with raised priority) overrides the Image's own
            // preferred height, otherwise it would equal the sprite's pixel height.
            var layoutElement = newPage.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = newPage.gameObject.AddComponent<LayoutElement>();
            layoutElement.layoutPriority = PageLayoutPriority;
            layoutElement.preferredHeight = 0f;

            _instantiatedPages.Add(newPage);
        }

        /// <summary>Creates a black separator bar after a page.</summary>
        private void CreateSeparator()
        {
            Image separatorImage;
            if (_separatorTemplate != null)
            {
                separatorImage = Instantiate(_separatorTemplate, _scrollRect.content);
                separatorImage.gameObject.SetActive(true);
            }
            else
            {
                separatorImage = CreateProceduralSeparator();
            }

            separatorImage.name = "Page_Separator";

            // LayoutElement keeps the bar's height; the bare Image would report
            // a preferred height of 0 and be collapsed by the layout group.
            var layoutElement = separatorImage.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = separatorImage.gameObject.AddComponent<LayoutElement>();
            layoutElement.layoutPriority = PageLayoutPriority;
            layoutElement.preferredHeight = SeparatorHeight;

            _instantiatedSeparators.Add(separatorImage);
        }

        /// <summary>Fallback separator built from code when no template is assigned.</summary>
        private Image CreateProceduralSeparator()
        {
            var separatorObj = new GameObject("Page_Separator", typeof(Image));
            separatorObj.transform.SetParent(_scrollRect.content, false);
            separatorObj.GetComponent<Image>().color = Color.black;
            return separatorObj.GetComponent<Image>();
        }

        /// <summary>Sizes pages from the laid-out content width, then resets scroll to top.</summary>
        private IEnumerator SizePagesWhenLaidOut()
        {
            // Wait one frame so the vertical layout pass has produced the final content width.
            yield return null;

            if (_scrollRect == null || _scrollRect.content == null) yield break;

            float contentWidth = _scrollRect.content.rect.width;
            foreach (Image page in _instantiatedPages)
            {
                if (page == null || page.sprite == null) continue;

                var layoutElement = page.GetComponent<LayoutElement>();
                if (layoutElement == null) continue;

                float spriteWidth = page.sprite.rect.width;
                float spriteHeight = page.sprite.rect.height;
                if (spriteWidth <= 0f || spriteHeight <= 0f) continue;

                layoutElement.preferredHeight = contentWidth * (spriteHeight / spriteWidth);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
            Canvas.ForceUpdateCanvases();

            _scrollRect.verticalNormalizedPosition = 1f;
        }

        /// <summary>Destroys all instantiated pages and separators.</summary>
        private void ClearContent()
        {
            if (_pageSizingRoutine != null)
            {
                StopCoroutine(_pageSizingRoutine);
                _pageSizingRoutine = null;
            }

            foreach (Image page in _instantiatedPages)
            {
                if (page != null) Destroy(page.gameObject);
            }
            _instantiatedPages.Clear();

            foreach (Image separator in _instantiatedSeparators)
            {
                if (separator != null) Destroy(separator.gameObject);
            }
            _instantiatedSeparators.Clear();
        }
    }
}
