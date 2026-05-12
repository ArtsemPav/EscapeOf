using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

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

    [Header("Hint Icons")]
    [SerializeField] private Sprite handIcon;
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private Sprite dragIcon;

    [Header("Crosshair")]
    [SerializeField] private Image crosshairImage;
    [SerializeField] private Sprite crosshairDefault;
    [SerializeField] private Sprite crosshairGrab;
    [SerializeField] private Sprite crosshairHand;
    [SerializeField] private Sprite crosshairItemDrag;
    [SerializeField] private Sprite crosshairLocked;
    [SerializeField] private Sprite crosshairPoint;
    [SerializeField] private Sprite crosshairUnlocked;
    [SerializeField] private Sprite crosshairView;

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
                bool isDraggable = crosshairMode == CrosshairMode.Grab || crosshairMode == CrosshairMode.ItemDrag;
                Sprite icon = isPickable ? handIcon : (isDraggable ? dragIcon : defaultIcon);
                interactionIcon.sprite = icon;
                interactionIcon.gameObject.SetActive(icon != null);
            }
        }

        SetCrosshair(visible ? crosshairMode : CrosshairMode.Default);
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
            CrosshairMode.Read => crosshairView != null ? crosshairView : crosshairDefault,
            CrosshairMode.Point     => crosshairPoint     != null ? crosshairPoint     : crosshairDefault,
            CrosshairMode.ItemDrag => crosshairItemDrag != null ? crosshairItemDrag : crosshairGrab,
            _                      => crosshairDefault
        };
    }

    /// <summary>
    /// Shows a drag hint while the player hovers an item from PuzzleInventoryBar over a compatible 3D object.
    /// </summary>
    public void ShowDragHint(string itemName, string targetHint)
    {
        string text = $"Применить {itemName} {targetHint}";
        SetHint(true, text, isPickable: false, CrosshairMode.ItemDrag);
    }

    /// <summary>
    /// Hides the drag hint shown by <see cref="ShowDragHint"/>.
    /// </summary>
    public void HideDragHint()
    {
        SetHint(false);
    }
}
