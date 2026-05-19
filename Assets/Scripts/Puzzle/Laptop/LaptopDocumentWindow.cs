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
        [SerializeField] private Image _pageDisplay;
        [SerializeField] private TMP_Text _pageCounter;
        [SerializeField] private ScrollRect _scrollRect;

        [Header("Navigation")]
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;

        private int _currentPageIndex;
        private LaptopDocumentFile _docFile;
        private AspectRatioFitter _aspectRatioFitter;

        protected override void Awake()
        {
            base.Awake();
            if (_prevButton != null) _prevButton.onClick.AddListener(PrevPage);
            if (_nextButton != null) _nextButton.onClick.AddListener(NextPage);
            
            if (_pageDisplay != null)
                _aspectRatioFitter = _pageDisplay.GetComponent<AspectRatioFitter>();
        }

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopDocumentFile doc)) return;

            _docFile = doc;
            _currentPageIndex = 0;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_docFile == null || _docFile.pages == null || _docFile.pages.Length == 0)
            {
                if (_pageCounter != null) _pageCounter.text = "0 / 0";
                if (_prevButton != null) _prevButton.interactable = false;
                if (_nextButton != null) _nextButton.interactable = false;
                return;
            }

            if (_pageDisplay != null)
            {
                Sprite currentSprite = _docFile.pages[_currentPageIndex];
                _pageDisplay.sprite = currentSprite;

                if (_aspectRatioFitter != null && currentSprite != null)
                {
                    _aspectRatioFitter.aspectRatio = currentSprite.rect.width / currentSprite.rect.height;
                }
            }

            if (_pageCounter != null)
                _pageCounter.text = $"{_currentPageIndex + 1} / {_docFile.pages.Length}";

            if (_prevButton != null) _prevButton.interactable = _currentPageIndex > 0;
            if (_nextButton != null) _nextButton.interactable = _currentPageIndex < _docFile.pages.Length - 1;

            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
        }

        public void NextPage()
        {
            if (_docFile == null || _currentPageIndex >= _docFile.pages.Length - 1) return;
            _currentPageIndex++;
            UpdateDisplay();
        }

        public void PrevPage()
        {
            if (_currentPageIndex <= 0) return;
            _currentPageIndex--;
            UpdateDisplay();
        }
    }
}
