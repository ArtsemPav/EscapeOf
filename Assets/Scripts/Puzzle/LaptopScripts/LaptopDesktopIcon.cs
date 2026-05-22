using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Desktop icon button that opens a file in LaptopWindowManager on double-click.
    /// Single click selects the icon; double-click opens the file.
    /// Place inside the icon grid on the DesktopScreen Canvas panel.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LaptopDesktopIcon : MonoBehaviour
    {
        [SerializeField] private Image               _icon;
        [SerializeField] private TMP_Text            _label;
        [SerializeField] private LaptopFileData      _file;
        [SerializeField] private LaptopWindowManager _windowManager;

        [Tooltip("Maximum seconds between two clicks to count as a double-click.")]
        [SerializeField] private float _doubleClickThreshold = 0.35f;

        private float _lastClickTime = -1f;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(OnClicked);

            if (_file == null) return;
            if (_icon  != null) _icon.sprite = _file.fileIcon;
            if (_label != null) _label.text  = _file.fileName;
        }

        private void OnClicked()
        {
            float now = Time.unscaledTime;

            if (now - _lastClickTime <= _doubleClickThreshold)
            {
                _lastClickTime = -1f;

                if (_file != null && _windowManager != null)
                    _windowManager.OpenFile(_file);
            }
            else
            {
                _lastClickTime = now;
            }
        }
    }
}
