using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Abstract base class for all laptop content windows with Header management.</summary>
    public abstract class LaptopWindow : MonoBehaviour
    {
        [Header("Header References")]
        [SerializeField] private TMP_Text _windowTitle;
        [SerializeField] private Button _closeButton;

        /// <summary>The file currently displayed in this window.</summary>
        public LaptopFileData FileData { get; private set; }

        private bool _isClosing;

        protected virtual void Awake()
        {
            if (_closeButton != null)
                _closeButton.onClick.AddListener(Close);
        }

        /// <summary>Opens the window and loads the given file.</summary>
        public void Open(LaptopFileData file)
        {
            _isClosing = false;
            FileData = file;
            if (_windowTitle != null)
                _windowTitle.text = file.fileName;

            gameObject.SetActive(true);
            OnOpen(file);
        }

        /// <summary>Hides this window without destroying it.</summary>
        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>Closes and notifies manager to cleanup references.</summary>
        public void Close()
        {
            if (_isClosing || this == null || gameObject == null) return;
            _isClosing = true;

            // Find manager in parent to handle cleanup if necessary
            var manager = GetComponentInParent<LaptopWindowManager>();
            if (manager != null)
                manager.NotifyWindowClosed(this);

            OnClose();
            Destroy(gameObject);
        }

        /// <summary>Called when the player exits puzzle mode. Override to pause media playback.</summary>
        public virtual void OnPuzzleExited() { }

        protected abstract void OnOpen(LaptopFileData file);
        protected virtual  void OnClose() { }
    }
}
