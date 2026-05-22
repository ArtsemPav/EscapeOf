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
        [SerializeField] private LaptopTextWindow     _textWindowPrefab;
        [SerializeField] private LaptopImageWindow    _imageWindowPrefab;
        [SerializeField] private LaptopAudioWindow    _audioWindowPrefab;
        [SerializeField] private LaptopVideoWindow    _videoWindowPrefab;
        [SerializeField] private LaptopDocumentWindow _documentWindowPrefab;

        [Header("All Files")]
        [Tooltip("All LaptopFileData assets available on this desktop. Used to restore the open window on load.")]
        [SerializeField] private LaptopFileData[] _allFiles;

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

        /// <summary>Pauses media in all open windows without closing them. Called when exiting puzzle mode.</summary>
        public void PauseMediaAll()
        {
            foreach (var window in _openWindows)
            {
                if (window != null)
                    window.OnPuzzleExited();
            }
        }

        /// <summary>Returns the fileId of the currently active window, or empty string if none.</summary>
        public string ActiveFileId =>
            _activeWindow != null && _activeWindow.FileData != null ? _activeWindow.FileData.fileId : string.Empty;

        /// <summary>Finds a file by fileId in _allFiles and opens it. Returns true if found.</summary>
        public bool TryOpenFileById(string fileId)
        {
            if (string.IsNullOrEmpty(fileId) || _allFiles == null) return false;
            foreach (var file in _allFiles)
            {
                if (file != null && file.fileId == fileId)
                {
                    OpenFile(file);
                    return true;
                }
            }
            Debug.LogWarning($"[LaptopWindowManager] TryOpenFileById: no file found for id '{fileId}'.");
            return false;
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
            else if (file is LaptopDocumentFile) prefab = _documentWindowPrefab;

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
