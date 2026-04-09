using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Orchestrates the medallion box puzzle:
///   - Populates CoinsBar slots from the player's inventory.
///   - Manages drag ghost that follows the cursor.
///   - On drop: raycasts into 3D scene to find a MedallionHole (any hole accepts any medallion).
///   - On LMB click on a filled hole: retrieves the medallion back to the UI and inventory.
///   - Validates the ORDER of placed medallions — fires <see cref="OnPuzzleSolved"/> when correct.
///   - Each frame raycasts the hole layer for hover: calls <see cref="MedallionHole.Highlight"/>
///     on the hovered filled hole so the player can tell the coin is clickable.
/// Attach to MedallionBoxPanel.
/// </summary>
public class MedallionBoxUI : MonoBehaviour
{
    [Header("Holes — assign in order: 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood")]
    [SerializeField] private MedallionHole[] _holes;

    [Tooltip("Layer that contains the Hole_X SphereColliders.")]
    [SerializeField] private LayerMask _holeLayer;

    [Header("Coin")]
    [Tooltip("Shared 3D coin prefab instantiated in every hole on correct placement.")]
    [SerializeField] private GameObject _coinPrefab;

    [Tooltip("How far above the hole the coin starts its drop (metres).")]
    [SerializeField] private float _dropHeight = 0.005f;

    [Tooltip("Duration of the drop animation in seconds.")]
    [SerializeField] private float _dropDuration = 0.35f;

    [Tooltip("Screen-space size of the drag ghost image in pixels.")]
    [SerializeField] private float _ghostSize = 64f;

    public event Action OnPuzzleSolved;

    private MedallionSlot[] _slots;
    private Canvas _canvas;
    private ItemData[] _medallionOrder;

    // Drag state
    private Image _dragGhost;
    private MedallionSlot _dragSource;
    private ItemData _dragItem;
    private bool _isDragging;

    // Hover state — tracks which filled hole the cursor is currently over
    private MedallionHole _hoveredHole;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _slots = GetComponentsInChildren<MedallionSlot>();
        _canvas = GetComponentInParent<Canvas>();
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        var mousePos = Mouse.current.position.ReadValue();
        bool overUI  = EventSystem.current.IsPointerOverGameObject();

        // Hover highlight — runs every frame regardless of drag state
        if (!_isDragging)
            UpdateHoverHighlight(overUI ? null : mousePos);

        // Click (not drag) on a filled hole → retrieve medallion back to UI
        if (!_isDragging && Mouse.current.leftButton.wasPressedThisFrame && !overUI)
            TryRetrieveFromHole(mousePos);
    }

    private void OnDisable()
    {
        ClearHover();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores the puzzle order and refreshes slot display using the player's collection order.
    /// Call every time the panel opens and after any retrieval.
    /// </summary>
    public void Populate(ItemData[] medallionOrder)
    {
        _medallionOrder = medallionOrder;
        RefreshSlots();
    }

    /// <summary>Returns the item currently placed in each hole (null if empty). Used by the save system.</summary>
    public ItemData[] GetHoleStates()
    {
        var states = new ItemData[_holes.Length];
        for (int i = 0; i < _holes.Length; i++)
            states[i] = _holes[i].PlacedItem;
        return states;
    }

    /// <summary>
    /// Restores hole state on load — fills holes immediately without animation.
    /// <paramref name="placedItemIds"/> maps to each hole by index; null or empty = hole is empty.
    /// <paramref name="allItems"/> is the full medallion list used to resolve IDs to ItemData.
    /// </summary>
    public void RestoreState(string[] placedItemIds, ItemData[] allItems)
    {
        for (int i = 0; i < _holes.Length && i < placedItemIds.Length; i++)
        {
            var id = placedItemIds[i];
            if (string.IsNullOrEmpty(id)) continue;

            var item = FindItem(id, allItems);
            if (item != null)
                _holes[i].FillImmediate(item, _coinPrefab);
        }
    }

    private static ItemData FindItem(string id, ItemData[] items)
    {
        if (items == null) return null;
        foreach (var item in items)
            if (item != null && item.ItemId == id) return item;
        return null;
    }

    // ── Drag handlers (called by MedallionSlot) ───────────────────────────────

    /// <summary>Begins a drag — dims the source slot and spawns a ghost image.</summary>
    public void OnBeginDrag(MedallionSlot slot, PointerEventData eventData)
    {
        _dragSource = slot;
        _dragItem = slot.Item;
        _isDragging = true;

        slot.SetDragVisual(dimmed: true);
        SpawnGhost(slot.Item.icon, eventData.position);
    }

    /// <summary>Moves the ghost to follow the cursor.</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (_dragGhost != null)
            _dragGhost.rectTransform.position = eventData.position;
    }

    /// <summary>
    /// Ends the drag — attempts to place the medallion on any empty hole.
    /// Restores the slot icon if placement fails.
    /// </summary>
    public void OnEndDrag(MedallionSlot slot, PointerEventData eventData)
    {
        _isDragging = false;
        DestroyGhost();

        bool placed = TryPlaceOnHole(eventData.position);

        if (!placed)
            slot.SetDragVisual(dimmed: false); // return visual, item stays in slot

        _dragSource = null;
        _dragItem = null;
    }

    // ── Hover highlight ────────────────────────────────────────────────────────

    /// <summary>
    /// Raycasts from <paramref name="screenPos"/> into the hole layer each frame.
    /// Highlights the hovered filled hole and clears the previous one.
    /// Pass <c>null</c> to clear all highlights (e.g. when cursor is over UI).
    /// </summary>
    private void UpdateHoverHighlight(Vector2? screenPos)
    {
        MedallionHole hit = null;

        if (screenPos.HasValue && Camera.main != null)
        {
            var ray = Camera.main.ScreenPointToRay(screenPos.Value);
            if (Physics.Raycast(ray, out var hitInfo, 50f, _holeLayer, QueryTriggerInteraction.Collide))
            {
                var hole = hitInfo.collider.GetComponent<MedallionHole>();
                if (hole != null && hole.IsFilled)
                    hit = hole;
            }
        }

        if (hit == _hoveredHole) return;

        _hoveredHole?.Highlight(false);
        _hoveredHole = hit;
        _hoveredHole?.Highlight(true);
    }

    /// <summary>Removes highlight from the currently hovered hole and resets tracking.</summary>
    private void ClearHover()
    {
        _hoveredHole?.Highlight(false);
        _hoveredHole = null;
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    private bool TryPlaceOnHole(Vector2 screenPos)
    {
        if (_dragItem == null || Camera.main == null) return false;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out var hit, 50f, _holeLayer, QueryTriggerInteraction.Collide))
            return false;

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || hole.IsFilled) return false;

        // Any medallion accepted — order is checked after placement
        hole.Fill(_dragItem, _coinPrefab, _dropHeight, _dropDuration);
        InventorySystem.Instance?.RemoveItem(_dragItem);
        _dragSource?.SetItem(null);

        CheckVictory();
        return true;
    }

    // ── Retrieval ─────────────────────────────────────────────────────────────

    private void TryRetrieveFromHole(Vector2 screenPos)
    {
        if (Camera.main == null) return;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out var hit, 50f, _holeLayer, QueryTriggerInteraction.Collide))
            return;

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || !hole.IsFilled) return;

        var item = hole.Retrieve(_dropHeight, _dropDuration);
        if (item == null) return;

        // Hole is now empty — clear hover so the highlight is not stuck
        ClearHover();

        // Restore to inventory and refresh UI slots
        InventorySystem.Instance?.AddItem(item);
        RefreshSlots();
    }

    // ── Victory ───────────────────────────────────────────────────────────────

    private void CheckVictory()
    {
        if (_medallionOrder == null || _holes.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _holes.Length; i++)
        {
            // Hole must be filled AND contain the correct item for its position
            if (_holes[i].PlacedItem != _medallionOrder[i]) return;
        }

        OnPuzzleSolved?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes slot icons using the player's collection order from MedallionCollectionTracker.
    /// Slots that haven't been collected yet, or whose medallion is in a hole, show as empty.
    /// </summary>
    private void RefreshSlots()
    {
        var inv = InventorySystem.Instance;
        if (inv == null || _medallionOrder == null) return;

        var collectionOrder = MedallionCollectionTracker.Instance?.CollectionOrder;

        int slotIdx = 0;

        // Fill slots in collection order — preserves the position even when item is in a hole
        if (collectionOrder != null)
        {
            foreach (var item in collectionOrder)
            {
                if (slotIdx >= _slots.Length) break;
                _slots[slotIdx++].SetItem(inv.HasItem(item) ? item : null);
            }
        }

        // Clear any remaining slots (not yet collected or no tracker)
        while (slotIdx < _slots.Length)
            _slots[slotIdx++].SetItem(null);
    }

    // ── Ghost ─────────────────────────────────────────────────────────────────

    private void SpawnGhost(Sprite sprite, Vector2 screenPos)
    {
        if (_canvas == null || sprite == null) return;

        var go = new GameObject("DragGhost", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        go.transform.SetAsLastSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(_ghostSize, _ghostSize);
        rt.position = screenPos;

        _dragGhost = go.AddComponent<Image>();
        _dragGhost.sprite = sprite;
        _dragGhost.raycastTarget = false;
        _dragGhost.color = new Color(1f, 1f, 1f, 0.85f);
    }

    private void DestroyGhost()
    {
        if (_dragGhost != null)
        {
            Destroy(_dragGhost.gameObject);
            _dragGhost = null;
        }
    }
}
