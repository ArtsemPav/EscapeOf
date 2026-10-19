using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One inventory slot. Always visible as a background cell.
/// Shows item icon when filled, hides icon when empty.
/// Handles drag-and-drop: moves items between slots or tries crafting.
/// Left-click shows the item's 3D preview in the embedded InventoryItemPreview panel.
/// On hover the icon smoothly scales up via IconHoverScale (no tooltip here —
/// the puzzle inventory bar keeps its own hover tooltip).
/// </summary>
public class InventorySlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerDownHandler
{
    [SerializeField] private Image iconImage;

    private IconHoverScale _iconHoverScale;

    public int SlotIndex { get; set; }
    public ItemData Item { get; private set; }
    public bool IsEmpty => Item == null;

    private void Awake()
    {
        _iconHoverScale = GetComponentInChildren<IconHoverScale>(true);
    }

    /// <summary>Fills the slot with an item and shows its icon.</summary>
    public void Setup(ItemData item)
    {
        Item = item;

        if (item != null)
        {
            iconImage.sprite = item.icon;
            iconImage.color = item.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            iconImage.enabled = true;
        }
        else
        {
            Clear();
        }
    }

    /// <summary>Clears the slot. Keeps Icon GameObject active so DraggableItem stays functional.</summary>
    public void Clear()
    {
        Item = null;
        iconImage.enabled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsEmpty) return;
        _iconHoverScale?.SetHovered(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _iconHoverScale?.SetHovered(false);
    }

    /// <summary>Plays the press sound when the player presses an item icon.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsEmpty) return;
        _iconHoverScale?.PlayPressSound();
    }

    /// <summary>Left-click shows the item's 3D preview in the embedded preview panel.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (IsEmpty) return;

        InventoryItemPreview.Instance?.Show(Item);
    }

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null) return;

        InventorySlot sourceSlot = dragged.SourceSlot;
        if (sourceSlot == this) return;

        if (!IsEmpty && !sourceSlot.IsEmpty)
        {
            if (InventorySystem.Instance.TryCombine(sourceSlot.SlotIndex, SlotIndex, out ItemData craftedItem))
            {
                // Immediately show the crafted item in the embedded preview panel.
                InventoryItemPreview.Instance?.Show(craftedItem);
                return;
            }
        }

        InventorySystem.Instance.SwapSlots(sourceSlot.SlotIndex, SlotIndex);
    }
}
