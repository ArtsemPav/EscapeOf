using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles drag-and-drop behaviour for an item icon inside the inventory.
/// Temporarily reparents the icon to the canvas root during drag.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image iconImage;

    private CanvasGroup _canvasGroup;
    private Transform _originalParent;
    private Canvas _rootCanvas;

    public InventorySlot SourceSlot { get; private set; }

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        SourceSlot = GetComponentInParent<InventorySlot>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Refresh SourceSlot in case the slot was reused with a different item
        SourceSlot = GetComponentInParent<InventorySlot>();

        if (SourceSlot == null || SourceSlot.IsEmpty)
        {
            eventData.pointerDrag = null;
            return;
        }

        _originalParent = transform.parent;
        transform.SetParent(_rootCanvas.transform, true);
        transform.SetAsLastSibling();
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = 0.7f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        transform.SetParent(_originalParent, true);
        transform.localPosition = Vector3.zero;
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.alpha = 1f;
    }
}
