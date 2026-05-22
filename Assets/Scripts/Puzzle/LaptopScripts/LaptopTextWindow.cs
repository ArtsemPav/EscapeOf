using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Scrollable text document viewer window.</summary>
    public class LaptopTextWindow : LaptopWindow
    {
        [SerializeField] private TMP_Text    _contentText;
        [SerializeField] private ScrollRect  _scrollRect;

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopTextFile textFile)) return;

            _contentText.text = textFile.content;

            if (_scrollRect != null)
                _scrollRect.normalizedPosition = new Vector2(0f, 1f);
        }
    }
}
