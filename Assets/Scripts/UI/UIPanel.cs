using UnityEngine;

namespace Escape.UI
{
    public enum PanelType
    {
        None,
        PauseMenu,
        Inventory,
        Note,
        CodeLock,
        Inspection,
        Message
    }

    /// <summary>
    /// Component to be placed on the root of any UI panel.
    /// Automatically registers itself with the UIManager.
    /// </summary>
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] private PanelType _type;
        public PanelType Type => _type;

        private void Awake()
        {
            // Register with UIManager when the object is initialized
            if (UIManager.Instance != null)
            {
             //   UIManager.Instance.RegisterPanel(this);
            }
        }

        private void OnDestroy()
        {
            if (UIManager.Instance != null)
            {
          //      UIManager.Instance.UnregisterPanel(this);
            }
        }
    }
}
