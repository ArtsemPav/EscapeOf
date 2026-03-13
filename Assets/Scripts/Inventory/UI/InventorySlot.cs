using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One inventory slot. Always visible as a background cell.
/// Shows item icon when filled, hides icon when empty.
/// Handles drag-and-drop: moves items between slots or tries crafting.
/// Shows item tooltip on hover.
/// </summary>
public class InventorySlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;

    public int SlotIndex { get; set; }
    public ItemData Item { get; private set; }
    public bool IsEmpty => Item == null;

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
        ItemTooltip.Instance?.Show(Item, GetComponent<RectTransform>());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null) return;

        InventorySlot sourceSlot = dragged.SourceSlot;
        if (sourceSlot == this) return;

        if (!IsEmpty && !sourceSlot.IsEmpty)
        {
            if (InventorySystem.Instance.TryCombine(sourceSlot.SlotIndex, SlotIndex, out _))
                return;
        }

        InventorySystem.Instance.SwapSlots(sourceSlot.SlotIndex, SlotIndex);
    }
}
