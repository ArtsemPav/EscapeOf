using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A single slot in the PuzzleInventoryBar.
/// Displays an item icon, supports drag-and-drop onto the 3D scene,
/// and shows tooltips on hover. Delegates all drag logic to PuzzleInventoryBar.
/// </summary>
[RequireComponent(typeof(Image))]
public class PuzzleInventorySlot : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;

    private PuzzleInventoryBar _bar;

    /// <summary>The item currently assigned to this slot, or null if empty.</summary>
    public ItemData Item { get; private set; }

    /// <summary>True when the slot has an item assigned.</summary>
    public bool HasItem => Item != null;

    /// <summary>Injects the parent bar reference. Called once during pool creation.</summary>
    public void Init(PuzzleInventoryBar bar)
    {
        _bar = bar;
    }

    /// <summary>
    /// Sets the padding between the slot edge and the icon image.
    /// Pass 0 to make the icon fill the slot completely.
    /// Called by PuzzleInventoryBar during pool creation.
    /// </summary>
    public void ApplyIconPadding(float padding)
    {
        if (iconImage == null) return;
        var rt = iconImage.GetComponent<RectTransform>();
        if (rt != null)
            rt.sizeDelta = new Vector2(-padding * 2f, -padding * 2f);
    }

    /// <summary>Assigns an item to this slot and shows its icon.</summary>
    public void SetItem(ItemData item)
    {
        Item = item;
        RefreshIcon(dimmed: false);
    }

    /// <summary>Clears the slot — hides the icon and removes the item reference.</summary>
    public void Clear()
    {
        Item = null;
        RefreshIcon(dimmed: false);
    }

    /// <summary>Dims or restores the icon during drag without clearing the item reference.</summary>
    public void SetDragVisual(bool dimmed)
    {
        RefreshIcon(dimmed);
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!HasItem) return;
        ItemTooltip.Instance?.Show(Item, GetComponent<RectTransform>());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }

    // ── Drag ──────────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!HasItem) return;
        ItemTooltip.Instance?.Hide();
        _bar?.OnSlotBeginDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!HasItem) return;
        _bar?.OnSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _bar?.OnSlotEndDrag(this, eventData);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RefreshIcon(bool dimmed)
    {
        if (iconImage == null) return;

        if (Item != null && Item.icon != null)
        {
            iconImage.sprite = Item.icon;
            iconImage.color = dimmed ? new Color(1f, 1f, 1f, 0.25f) : Color.white;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.color = new Color(1f, 1f, 1f, 0f);
            iconImage.enabled = false;
        }
    }
}
