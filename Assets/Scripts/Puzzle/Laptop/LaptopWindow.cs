using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Abstract base class for all laptop content windows.</summary>
    public abstract class LaptopWindow : MonoBehaviour
    {
        /// <summary>The file currently displayed in this window.</summary>
        public LaptopFileData FileData { get; private set; }

        /// <summary>Opens the window and loads the given file.</summary>
        public void Open(LaptopFileData file)
        {
            FileData = file;
            gameObject.SetActive(true);
            OnOpen(file);
        }

        /// <summary>Hides this window without destroying it.</summary>
        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>Closes and destroys this window instance.</summary>
        public void Close()
        {
            OnClose();
            Destroy(gameObject);
        }

        protected abstract void OnOpen(LaptopFileData file);
        protected virtual  void OnClose() { }
    }
}
