using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Desktop icon button that opens a file in LaptopWindowManager on click.
    /// Place inside the icon grid on the DesktopScreen Canvas panel.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LaptopDesktopIcon : MonoBehaviour
    {
        [SerializeField] private Image            _icon;
        [SerializeField] private TMP_Text         _label;
        [SerializeField] private LaptopFileData   _file;
        [SerializeField] private LaptopWindowManager _windowManager;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(OnClicked);

            if (_file == null) return;
            if (_icon  != null) _icon.sprite  = _file.fileIcon;
            if (_label != null) _label.text   = _file.fileName;
        }

        private void OnClicked()
        {
            if (_file != null && _windowManager != null)
                _windowManager.OpenFile(_file);
        }
    }
}
