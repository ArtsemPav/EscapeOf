using System.Collections.Generic;
using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Manages open windows and the tab strip on the laptop desktop.
    /// Supports multiple open files with tab switching and closing.
    /// Attach inside the DesktopScreen Canvas panel.
    /// </summary>
    public class LaptopWindowManager : MonoBehaviour
    {
        [Header("Containers")]
        [SerializeField] private Transform _tabContainer;
        [SerializeField] private Transform _windowContainer;

        [Header("Prefabs")]
        [SerializeField] private LaptopTabButton   _tabPrefab;
        [SerializeField] private LaptopTextWindow  _textWindowPrefab;
        [SerializeField] private LaptopImageWindow _imageWindowPrefab;
        [SerializeField] private LaptopAudioWindow _audioWindowPrefab;
        [SerializeField] private LaptopVideoWindow _videoWindowPrefab;

        private readonly List<OpenEntry> _openWindows = new();
        private LaptopWindow _activeWindow;

        private struct OpenEntry
        {
            public LaptopTabButton Tab;
            public LaptopWindow    Window;
        }

        /// <summary>Opens a file. If already open, switches to existing tab.</summary>
        public void OpenFile(LaptopFileData file)
        {
            int existing = _openWindows.FindIndex(e => e.Window.FileData == file);
            if (existing >= 0)
            {
                ActivateWindow(_openWindows[existing].Window);
                return;
            }

            LaptopWindow window = CreateWindow(file);
            if (window == null) return;

            LaptopTabButton tab = Instantiate(_tabPrefab, _tabContainer);
            tab.Setup(file.fileName,
                () => ActivateWindow(window),
                () => CloseWindow(window));

            _openWindows.Add(new OpenEntry { Tab = tab, Window = window });
            ActivateWindow(window);
        }

        /// <summary>Closes all open windows. Called when exiting laptop mode.</summary>
        public void CloseAll()
        {
            for (int i = _openWindows.Count - 1; i >= 0; i--)
                CloseWindow(_openWindows[i].Window);
        }

        private void ActivateWindow(LaptopWindow window)
        {
            foreach (var entry in _openWindows)
            {
                bool isActive = entry.Window == window;
                entry.Window.SetVisible(isActive);
                entry.Tab.SetActive(isActive);
            }
            _activeWindow = window;
        }

        private void CloseWindow(LaptopWindow window)
        {
            int index = _openWindows.FindIndex(e => e.Window == window);
            if (index < 0) return;

            var entry = _openWindows[index];
            Destroy(entry.Tab.gameObject);
            entry.Window.Close();
            _openWindows.RemoveAt(index);

            if (_openWindows.Count > 0)
                ActivateWindow(_openWindows[Mathf.Min(index, _openWindows.Count - 1)].Window);
            else
                _activeWindow = null;
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
