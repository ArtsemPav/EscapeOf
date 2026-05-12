using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Maps every CrosshairMode to a sprite. Fully configurable in the Inspector.
/// </summary>
[System.Serializable]
public class CrosshairSprites
{
    public Sprite Default;
    public Sprite Hand;
    public Sprite Grab;
    public Sprite ItemDrag;
    public Sprite Locked;
    public Sprite Unlocked;
    public Sprite Point;
    public Sprite View;

    /// <summary>Returns the sprite for the given mode, falling back to Default.</summary>
    public Sprite GetSprite(CrosshairMode mode) => mode switch
    {
        CrosshairMode.Hand     => Hand     != null ? Hand     : Default,
        CrosshairMode.Grab     => Grab     != null ? Grab     : Default,
        CrosshairMode.ItemDrag => ItemDrag != null ? ItemDrag : Grab,
        CrosshairMode.Locked   => Locked   != null ? Locked   : Default,
        CrosshairMode.Unlocked => Unlocked != null ? Unlocked : Default,
        CrosshairMode.Point    => Point    != null ? Point    : Default,
        CrosshairMode.Read     => View     != null ? View     : Default,
        _                      => Default
    };
}

/// <summary>
/// Handles the display of interaction hints and crosshair state changes.
/// </summary>
public class InteractionUI : MonoBehaviour
{
    public static InteractionUI Instance { get; private set; }

    [Header("Hint")]
    [SerializeField] private GameObject hintPanel;
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("Crosshair")]
    [SerializeField] private Image crosshairImage;
    [SerializeField] private CrosshairSprites crosshairSprites;

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

        if (visible && hintText != null)
            hintText.text = text;

        SetCrosshair(visible ? crosshairMode : CrosshairMode.Default);
    }

    /// <summary>
    /// Directly switches the crosshair sprite without touching the hint panel.
    /// </summary>
    public void SetCrosshair(CrosshairMode mode)
    {
        if (crosshairImage == null) return;
        crosshairImage.sprite = crosshairSprites.GetSprite(mode);
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
