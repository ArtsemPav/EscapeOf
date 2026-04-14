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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Mouse.current == null) return;

        var mousePos = Mouse.current.position.ReadValue();
        bool overUI  = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Hover highlight — runs every frame
        UpdateHoverHighlight(overUI ? null : mousePos);

        // Click on a filled hole → retrieve medallion back to inventory
        if (Mouse.current.leftButton.wasPressedThisFrame && !overUI)
            TryRetrieveFromHole(mousePos);
    }

    private void OnDisable()
    {
        ClearHover();
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

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to place the dragged item on an empty hole via 3D raycast.
    /// Does NOT remove the item from inventory — PuzzleInventoryBar handles that.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition)
    {
        if (item == null || Camera.main == null) return false;

        // Only medallions belonging to this puzzle are accepted
        if (_medallionOrder == null || System.Array.IndexOf(_medallionOrder, item) < 0)
            return false;

        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, 50f, _holeLayer, QueryTriggerInteraction.Collide))
            return false;

        var hole = hit.collider.GetComponent<MedallionHole>();
        if (hole == null || hole.IsFilled) return false;

        hole.Fill(item, _coinPrefab, _dropHeight, _dropDuration);
        CheckVictory();
        return true;
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

        // Restore to inventory — PuzzleInventoryBar refreshes via OnInventoryChanged
        InventorySystem.Instance?.AddItem(item);
    }

    // ── Victory ───────────────────────────────────────────────────────────────

    private void CheckVictory()
    {
        if (_medallionOrder == null || _holes.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _holes.Length; i++)
        {
            if (_holes[i].PlacedItem != _medallionOrder[i]) return;
        }

        OnPuzzleSolved?.Invoke();
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
