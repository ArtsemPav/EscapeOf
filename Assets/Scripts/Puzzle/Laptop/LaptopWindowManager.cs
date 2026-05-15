using System.Collections.Generic;
using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Manages open windows on the laptop desktop.
    /// Supports multiple open files with focus management.
    /// Attach inside the DesktopScreen Canvas panel.
    /// </summary>
    public class LaptopWindowManager : MonoBehaviour
    {
        [Header("Containers")]
        [SerializeField] private Transform _windowContainer;

        [Header("Prefabs")]
        [SerializeField] private LaptopTextWindow  _textWindowPrefab;
        [SerializeField] private LaptopImageWindow _imageWindowPrefab;
        [SerializeField] private LaptopAudioWindow _audioWindowPrefab;
        [SerializeField] private LaptopVideoWindow _videoWindowPrefab;

        private readonly List<LaptopWindow> _openWindows = new();
        private LaptopWindow _activeWindow;

        private bool _isClosingAll;

        /// <summary>Opens a file. If already open, brings to front.</summary>
        public void OpenFile(LaptopFileData file)
        {
            // Clean up any destroyed windows from the list
            _openWindows.RemoveAll(w => w == null);

            LaptopWindow existing = _openWindows.Find(w => w.FileData == file);
            if (existing != null)
            {
                ActivateWindow(existing);
                return;
            }

            LaptopWindow window = CreateWindow(file);
            if (window == null) return;

            _openWindows.Add(window);
            ActivateWindow(window);
        }

        /// <summary>Called by window itself when closing.</summary>
        public void NotifyWindowClosed(LaptopWindow window)
        {
            _openWindows.Remove(window);

            if (_isClosingAll) return;

            if (_activeWindow == window)
            {
                _activeWindow = _openWindows.Count > 0 ? _openWindows[_openWindows.Count - 1] : null;
                if (_activeWindow != null) _activeWindow.SetVisible(true);
            }
        }

        /// <summary>Closes all open windows. Called when exiting laptop mode.</summary>
        public void CloseAll()
        {
            _isClosingAll = true;
            try
            {
                for (int i = _openWindows.Count - 1; i >= 0; i--)
                {
                    var window = _openWindows[i];
                    if (window != null)
                    {
                        window.Close();
                    }
                }
                _openWindows.Clear();
                _activeWindow = null;
            }
            finally
            {
                _isClosingAll = false;
            }
        }

        private void ActivateWindow(LaptopWindow window)
        {
            // Clean up any destroyed windows from the list first
            _openWindows.RemoveAll(w => w == null);

            foreach (var w in _openWindows)
            {
                if (w != null)
                    w.SetVisible(w == window);
            }
            
            _activeWindow = window;
            
            if (window != null)
                window.transform.SetAsLastSibling();
        }

        private LaptopWindow CreateWindow(LaptopFileData file)
        {
            LaptopWindow prefab = null;
            
            if (file is LaptopTextFile) prefab = _textWindowPrefab;
            else if (file is LaptopImageFile) prefab = _imageWindowPrefab;
            else if (file is LaptopAudioFile) prefab = _audioWindowPrefab;
            else if (file is LaptopVideoFile) prefab = _videoWindowPrefab;

            if (prefab == null)
            {
                Debug.LogWarning($"[LaptopWindowManager] No prefab for {file.GetType().Name}");
                return null;
            }

            var window = Instantiate(prefab, _windowContainer);
            window.Open(file);
            return window;
        }
    }
}
