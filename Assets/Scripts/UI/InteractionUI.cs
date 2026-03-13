using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// Handles the display of interaction hints and crosshair state changes.
    /// </summary>
    public class InteractionUI : MonoBehaviour
    {
        public static InteractionUI Instance { get; private set; }

        [Header("Hint")]
        [SerializeField] private GameObject hintPanel;
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private Image interactionIcon;

        [Header("Blocked Hint")]
        [SerializeField] private TextMeshProUGUI blockedHintText;
        [SerializeField] private float blockedHintDuration = 3f;

        [Header("Hint Icons")]
        [SerializeField] private Sprite handIcon;
        [SerializeField] private Sprite defaultIcon;

        [Header("Crosshair")]
        [SerializeField] private Image crosshairImage;
        [SerializeField] private Sprite crosshairDefault;
        [SerializeField] private Sprite crosshairHand;
        [SerializeField] private Sprite crosshairLocked;
        [SerializeField] private Sprite crosshairUnlocked;
        [SerializeField] private Sprite crosshairGrab;

        private Coroutine _blockedHintCoroutine;

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

            if (blockedHintText != null)
                blockedHintText.gameObject.SetActive(false);

            SetCrosshair(CrosshairMode.Default);
        }

        /// <summary>
        /// Shows or hides the interaction hint and switches the crosshair accordingly.
        /// </summary>
        public void SetHint(bool visible, string text = "", bool isPickable = false,
                            CrosshairMode crosshairMode = CrosshairMode.Default)
        {
            if (hintPanel == null) return;

            hintPanel.SetActive(visible);

            if (visible)
            {
                if (hintText != null)
                    hintText.text = text;

                if (interactionIcon != null)
                {
                    interactionIcon.sprite = isPickable ? handIcon : defaultIcon;
                    interactionIcon.gameObject.SetActive(interactionIcon.sprite != null);
                }
            }

            SetCrosshair(visible ? crosshairMode : CrosshairMode.Default);
        }

        /// <summary>
        /// Shows a temporary hint at the bottom of the screen explaining why an interaction
        /// is blocked. Hides automatically after blockedHintDuration seconds.
        /// </summary>
        public void ShowBlockedHint(string hint)
        {
            if (blockedHintText == null || string.IsNullOrEmpty(hint)) return;

            if (_blockedHintCoroutine != null)
                StopCoroutine(_blockedHintCoroutine);

            blockedHintText.text = hint;
            blockedHintText.gameObject.SetActive(true);
            _blockedHintCoroutine = StartCoroutine(HideBlockedHintAfter(blockedHintDuration));
        }

        private IEnumerator HideBlockedHintAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (blockedHintText != null)
                blockedHintText.gameObject.SetActive(false);
            _blockedHintCoroutine = null;
        }

        /// <summary>
        /// Directly switches the crosshair sprite without touching the hint panel.
        /// </summary>
        public void SetCrosshair(CrosshairMode mode)
        {
            if (crosshairImage == null) return;

            crosshairImage.sprite = mode switch
            {
                CrosshairMode.Hand     => crosshairHand     != null ? crosshairHand     : crosshairDefault,
                CrosshairMode.Locked   => crosshairLocked   != null ? crosshairLocked   : crosshairDefault,
                CrosshairMode.Unlocked => crosshairUnlocked != null ? crosshairUnlocked : crosshairDefault,
                CrosshairMode.Grab     => crosshairGrab     != null ? crosshairGrab     : crosshairDefault,
                _                      => crosshairDefault
            };
        }
    }
}
