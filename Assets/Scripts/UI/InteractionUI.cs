using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// Handles the display of interaction hints.
    /// </summary>
    public class InteractionUI : MonoBehaviour
    {
        public static InteractionUI Instance { get; private set; }

        [Header("UI Elements")]
        [SerializeField] private GameObject hintPanel;
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private Image interactionIcon;

        [Header("Icons")]
        [SerializeField] private Sprite handIcon;
        [SerializeField] private Sprite defaultIcon;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            if (hintPanel != null)
                hintPanel.SetActive(false);
        }

        /// <summary>
        /// Updates the interaction hint UI.
        /// </summary>
        /// <param name="visible">Whether the hint should be visible.</param>
        /// <param name="text">The hint text to display.</param>
        /// <param name="isPickable">Whether the object is pickable (to show hand icon).</param>
        public void SetHint(bool visible, string text = "", bool isPickable = false)
        {
            if (hintPanel == null) return;

            hintPanel.SetActive(visible);
            if (!visible) return;

            if (hintText != null)
                hintText.text = text;

            if (interactionIcon != null)
            {
                interactionIcon.sprite = isPickable ? handIcon : defaultIcon;
                interactionIcon.gameObject.SetActive(interactionIcon.sprite != null);
            }
        }
    }
}
