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
        [SerializeField] private TMP_Text _pageCounter;
        [SerializeField] private ScrollRect _scrollRect;

        private LaptopDocumentFile _docFile;
        private List<Image> _instantiatedPages = new List<Image>();

        protected override void Awake()
        {
            base.Awake();
            if (_pageTemplate != null)
                _pageTemplate.gameObject.SetActive(false);
        }

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopDocumentFile doc)) return;

            _docFile = doc;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            // Clear previous pages
            foreach (var page in _instantiatedPages)
            {
                if (page != null) Destroy(page.gameObject);
            }
            _instantiatedPages.Clear();

            if (_docFile == null || _docFile.pages == null || _docFile.pages.Length == 0)
            {
                if (_pageCounter != null) _pageCounter.text = "0 pages";
                return;
            }

            if (_pageTemplate == null || _scrollRect == null || _scrollRect.content == null)
            {
                Debug.LogError("LaptopDocumentWindow: Missing required references for display.");
                return;
            }

            // Create a page for each sprite
            for (int i = 0; i < _docFile.pages.Length; i++)
            {
                Sprite sprite = _docFile.pages[i];
                if (sprite == null) continue;

                Image newPage = Instantiate(_pageTemplate, _scrollRect.content);
                newPage.gameObject.SetActive(true);
                newPage.name = $"Page_{i + 1}";
                newPage.sprite = sprite;
                
                // Update Aspect Ratio
                var arf = newPage.GetComponent<AspectRatioFitter>();
                if (arf != null)
                {
                    arf.aspectRatio = sprite.rect.width / sprite.rect.height;
                }
                
                _instantiatedPages.Add(newPage);
            }

            if (_pageCounter != null)
                _pageCounter.text = $"{_docFile.pages.Length} pages";

            // Reset scroll to top
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        // Methods kept for potential external calls, but buttons will be hidden in prefab
        public void NextPage() { }
        public void PrevPage() { }
    }
    }
