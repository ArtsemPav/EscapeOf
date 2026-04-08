using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI slot in CoinsBar that holds one medallion from the player's inventory.
/// Initiates drag-and-drop and relays events to the parent MedallionBoxUI.
/// Shows item description via ItemTooltip on hover — same behaviour as InventorySlot.
/// </summary>
[RequireComponent(typeof(Image))]
public class MedallionSlot : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    private Image _icon;
    private ItemData _item;

    // Lazy reference — resolved on first use, safe regardless of Awake ordering.
    private MedallionBoxUI _boxUI;
    private MedallionBoxUI BoxUI => _boxUI ??= GetComponentInParent<MedallionBoxUI>(includeInactive: true);

    public ItemData Item => _item;
    public bool HasItem => _item != null;

    private void Awake()
    {
        _icon = transform.Find("Icon")?.GetComponent<Image>();
    }

    /// <summary>Assigns an item to this slot and refreshes the icon display.</summary>
    public void SetItem(ItemData item)
    {
        _item = item;
        RefreshIcon(dimmed: false);

        // If tooltip is open for this slot and item changed, hide it
        if (item == null)
            ItemTooltip.Instance?.Hide();
    }

    /// <summary>Dims the icon while dragging without clearing the item reference.</summary>
    public void SetDragVisual(bool dimmed)
    {
        RefreshIcon(dimmed);
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!HasItem) return;
        ItemTooltip.Instance?.Show(_item, GetComponent<RectTransform>());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }

    // ── Drag ─────────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!HasItem) return;
        ItemTooltip.Instance?.Hide();
        BoxUI?.OnBeginDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!HasItem) return;
        BoxUI?.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        BoxUI?.OnEndDrag(this, eventData);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RefreshIcon(bool dimmed)
    {
        if (_icon == null) return;

        if (_item != null && _item.icon != null)
        {
            _icon.sprite = _item.icon;
            _icon.color = dimmed ? new Color(1f, 1f, 1f, 0.25f) : Color.white;
        }
        else
        {
            _icon.sprite = null;
            _icon.color = new Color(1f, 1f, 1f, 0f);
        }
    }
}
