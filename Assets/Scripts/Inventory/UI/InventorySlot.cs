using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents one slot in the inventory UI.
/// Accepts dropped items and initiates crafting via InventorySystem.
/// </summary>
public class InventorySlot : MonoBehaviour, IDropHandler
{
    [SerializeField] private Image iconImage;

    private ItemData _item;

    public ItemData Item => _item;

    /// <summary>Sets the item this slot displays.</summary>
    public void Setup(ItemData item)
    {
        _item = item;
        iconImage.sprite = item.icon;
        iconImage.color = item.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);
    }

    /// <summary>
    /// Called when another slot's DraggableItem is dropped onto this slot.
    /// Tries to combine the two items.
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null || dragged.SourceSlot == this) return;

        InventorySystem.Instance.TryCombine(dragged.SourceSlot.Item, _item, out _);
    }
}
