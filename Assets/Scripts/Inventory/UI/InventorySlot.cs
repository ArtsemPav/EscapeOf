using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One inventory slot. Always visible as a background cell.
/// Shows item icon when filled, hides icon when empty.
/// </summary>
public class InventorySlot : MonoBehaviour, IDropHandler
{
    [SerializeField] private Image iconImage;

    private ItemData _item;

    public ItemData Item => _item;
    public bool IsEmpty => _item == null;

    /// <summary>Fills the slot with an item and shows its icon.</summary>
    public void Setup(ItemData item)
    {
        _item = item;
        iconImage.sprite = item.icon;
        iconImage.color = item.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);
        iconImage.gameObject.SetActive(true);
    }

    /// <summary>Clears the slot — hides icon, keeps background visible.</summary>
    public void Clear()
    {
        _item = null;
        iconImage.gameObject.SetActive(false);
    }

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null || dragged.SourceSlot == this || IsEmpty) return;

        InventorySystem.Instance.TryCombine(dragged.SourceSlot.Item, _item, out _);
    }
}
