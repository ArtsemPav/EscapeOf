using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates the medallion box puzzle:
///   - Accepts items dropped from PuzzleInventoryBar via <see cref="IPuzzleDropHandler"/>.
///   - On drop: raycasts into 3D scene to find an empty <see cref="MedallionHole"/>.
///   - On LMB click on a filled hole: retrieves the medallion back to the inventory.
///   - Validates the ORDER of placed medallions — fires <see cref="OnPuzzleSolved"/> when correct.
///   - Each frame raycasts the hole layer for hover highlight on filled holes.
///
/// Drag/ghost logic and slot management are handled by PuzzleInventoryBar.
/// Attach to MedallionBoxPanel.
/// </summary>
public class MedallionBoxUI : MonoBehaviour, IPuzzleDropHandler
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

    public event Action OnPuzzleSolved;

    private ItemData[] _medallionOrder;

    // Hover state — tracks which filled hole the cursor is currently over
    private MedallionHole _hoveredHole;

    // Ghost preview — tracks which empty hole currently shows a 3D ghost coin
    private MedallionHole _ghostHole;

    // When true, medallions can no longer be retrieved from the holes.
    private bool _solved;

    // Runtime mask built from the actual layers of _holes, OR-ed with the
    // inspector _holeLayer. This prevents the raycast from silently failing
    // when hole GameObjects are on a different layer than the inspector mask.
    private LayerMask _effectiveHoleLayer;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        RebuildHoleLayerMask();
    }

    private void OnEnable()
    {
        // Rebuild in case hole layers were changed between activations.
        RebuildHoleLayerMask();
    }

    /// <summary>
    /// Computes _effectiveHoleLayer from the real layers of every assigned hole,
    /// merged with the inspector _holeLayer so manual overrides are preserved.
    /// </summary>
    private void RebuildHoleLayerMask()
    {
        int mask = _holeLayer.value;
        if (_holes != null)
        {
            foreach (var hole in _holes)
            {
                if (hole != null)
                    mask |= (1 << hole.gameObject.layer);
            }
        }
        _effectiveHoleLayer = mask;
    }

    private void Update()
    {
        if (_solved || Mouse.current == null) return;

        var mousePos = Mouse.current.position.ReadValue();
        bool overUI  = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Ghost preview while dragging a medallion from the inventory bar.
        if (PuzzleInventoryBar.IsDragging && !overUI)
            UpdateGhostPreview(mousePos);
        else
            ClearGhost();

        // Hover highlight (only when not dragging — ghost takes priority).
        if (!PuzzleInventoryBar.IsDragging)
            UpdateHoverHighlight(overUI ? null : mousePos);

        // Click on a filled hole → retrieve medallion back to inventory
        if (Mouse.current.leftButton.wasPressedThisFrame && !overUI && !PuzzleInventoryBar.IsDragging)
            TryRetrieveFromHole(mousePos);
    }

    private void OnDisable()
    {
        ClearHover();
        ClearGhost();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores the expected medallion order for victory validation.
    /// Call every time the panel opens.
    /// </summary>
    public void Populate(ItemData[] medallionOrder)
    {
        _medallionOrder = medallionOrder;
    }

    /// <summary>
    /// Forcefully marks the puzzle as solved, preventing any further retrieval.
    /// Called by <see cref="MedallionBoxInteraction"/> when restoring a solved state from save.
    /// </summary>
    public void MarkSolved()
    {
        _solved = true;
        ClearHover();
        ClearGhost();
    }

    /// <summary>Returns the item currently placed in each hole (null if empty). Used by the save system.</summary>
    public ItemData[] GetHoleStates()
    {
        if (_holes == null) return Array.Empty<ItemData>();
        var states = new ItemData[_holes.Length];
        for (int i = 0; i < _holes.Length; i++)
            states[i] = (_holes[i] != null) ? _holes[i].PlacedItem : null;
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

        // If all holes are now filled correctly, lock retrieval.
        CheckVictorySilent();
    }

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to place the dragged item on an empty hole via 3D raycast.
    /// Does NOT remove the item from inventory — PuzzleInventoryBar handles that.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        ClearGhost();

        if (item == null || Camera.main == null) return false;

        // Only medallions belonging to this puzzle are accepted
        if (_medallionOrder == null || System.Array.IndexOf(_medallionOrder, item) < 0)
            return false;

        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, 50f, _effectiveHoleLayer, QueryTriggerInteraction.Collide))
            return false;

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || hole.IsFilled) return false;

        hole.Fill(item, _coinPrefab, _dropHeight, _dropDuration);
        CheckVictory();
        return true;
    }

    // ── Ghost Preview ──────────────────────────────────────────────────────────

    /// <summary>
    /// Raycasts from the cursor position to find an empty hole. If found and the
    /// dragged item is a valid medallion, shows a 3D ghost preview on that hole.
    /// Tracks which hole the ghost is on and only updates when it changes.
    /// </summary>
    private void UpdateGhostPreview(Vector2 mousePos)
    {
        if (Camera.main == null) return;

        var draggedItem = PuzzleInventoryBar.DraggedItem;
        if (draggedItem == null || _medallionOrder == null ||
            System.Array.IndexOf(_medallionOrder, draggedItem) < 0)
        {
            ClearGhost();
            return;
        }

        var ray = Camera.main.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out var hit, 50f, _effectiveHoleLayer, QueryTriggerInteraction.Collide))
        {
            ClearGhost();
            return;
        }

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || hole.IsFilled)
        {
            ClearGhost();
            return;
        }

        // Already showing ghost on this hole — nothing to do.
        if (hole == _ghostHole) return;

        ClearGhost();
        hole.ShowGhost(draggedItem, _coinPrefab);
        _ghostHole = hole;
    }

    /// <summary>Removes the ghost preview from the current hole.</summary>
    private void ClearGhost()
    {
        if (_ghostHole != null)
        {
            _ghostHole.HideGhost();
            _ghostHole = null;
        }
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
            if (Physics.Raycast(ray, out var hitInfo, 50f, _effectiveHoleLayer, QueryTriggerInteraction.Collide))
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

    // ── Retrieval ─────────────────────────────────────────────────────────────

    private void TryRetrieveFromHole(Vector2 screenPos)
    {
        if (Camera.main == null) return;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out var hit, 50f, _effectiveHoleLayer, QueryTriggerInteraction.Collide))
            return;

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || !hole.IsFilled) return;

        // Release all stale reservations left by previous medallion drops,
        // then compact the inventory so AddItem finds the leftmost free slot.
        var inv = InventorySystem.Instance;
        if (inv == null) return;

        inv.ReleaseAllReservations();
        inv.Compact();

        if (inv.IsFull) return;

        var item = hole.Retrieve(_dropHeight, _dropDuration);
        if (item == null) return;

        // Hole is now empty — clear hover so the highlight is not stuck
        ClearHover();

        // Edge case: inventory filled between the guard and AddItem — restore medallion immediately.
        if (!inv.AddItem(item))
            hole.FillImmediate(item, _coinPrefab);
    }

    // ── Victory ───────────────────────────────────────────────────────────────

    private void CheckVictory()
    {
        if (_medallionOrder == null || _holes.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _holes.Length; i++)
        {
            if (_holes[i].PlacedItem != _medallionOrder[i]) return;
        }

        _solved = true;
        ClearHover();
        OnPuzzleSolved?.Invoke();
    }

    /// <summary>Sets <see cref="_solved"/> without firing the event. Used when restoring from a save.</summary>
    private void CheckVictorySilent()
    {
        if (_medallionOrder == null || _holes.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _holes.Length; i++)
        {
            if (_holes[i].PlacedItem != _medallionOrder[i]) return;
        }

        _solved = true;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ItemData FindItem(string id, ItemData[] items)
    {
        if (items == null) return null;
        foreach (var item in items)
            if (item != null && item.ItemId == id) return item;
        return null;
    }
}
