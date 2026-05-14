using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Tab button in the LaptopWindowManager tab strip.</summary>
    public class LaptopTabButton : MonoBehaviour
    {
        [SerializeField] private TMP_Text  _label;
        [SerializeField] private Button    _selectButton;
        [SerializeField] private Button    _closeButton;
        [SerializeField] private GameObject _activeIndicator;

        private Action _onSelect;
        private Action _onClose;

        /// <summary>Initializes the tab with a file name and interaction callbacks.</summary>
        public void Setup(string name, Action onSelect, Action onClose)
        {
            _label.text = name;
            _onSelect   = onSelect;
            _onClose    = onClose;

            _selectButton.onClick.RemoveAllListeners();
            _selectButton.onClick.AddListener(() => _onSelect?.Invoke());

            _closeButton.onClick.RemoveAllListeners();
            _closeButton.onClick.AddListener(() => _onClose?.Invoke());

            SetActive(false);
        }

        /// <summary>Updates the active visual state of the tab.</summary>
        public void SetActive(bool isActive)
        {
            if (_activeIndicator != null)
                _activeIndicator.SetActive(isActive);
        }
    }
}
