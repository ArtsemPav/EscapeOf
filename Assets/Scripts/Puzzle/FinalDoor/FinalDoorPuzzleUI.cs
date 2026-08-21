using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// UI controller for the final door puzzle. Handles drag-and-drop into 3D door
/// holes, retrieval via LMB, hover highlight, and ghost preview while dragging.
///
/// Each medallion statue has its own camera and entry point (FinalDoorSideInteractable).
/// This component does NOT manage camera switching — the controller handles that
/// based on which statue the player interacted with.
///
/// Attach to the same GameObject as FinalDoorPuzzleController and
/// FinalDoorPuzzleInteraction. Implements <see cref="IPuzzleDropHandler"/> so
/// the controller's EnterPuzzleMode finds it via GetComponentInChildren.
///
/// Victory: all 6 door holes filled with correct ItemData → fires OnPuzzleSolved.
/// Until then, the player can freely insert any medallion into any hole and
/// retrieve any medallion from any hole.
/// </summary>
public class FinalDoorPuzzleUI : MonoBehaviour, IPuzzleDropHandler
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Holes")]
    [Tooltip("Door holes in order matching the medallion order passed to Populate (0..5). " +
             "Auto-discovered from children if empty.")]
    [SerializeField] private MedallionHole[] _doorHoles;

    [Tooltip("LayerMask for all door hole colliders.")]
    [SerializeField] private LayerMask _holeLayer;

    [Header("Coin")]
    [Tooltip("Fallback coin prefab — used only if ItemData.inspectionPrefab is null.")]
    [SerializeField] private GameObject _coinPrefab;

    [Tooltip("How far above the hole the coin starts its drop (metres).")]
    [SerializeField] private float _dropHeight = 0.005f;

    [Tooltip("Duration of the drop animation in seconds.")]
    [SerializeField] private float _dropDuration = 0.35f;

    [Header("Controller")]
    [Tooltip("Auto-found via GetComponent if empty.")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when all 6 door holes are filled correctly.</summary>
    public event Action OnPuzzleSolved;

    // ── State ─────────────────────────────────────────────────────────────────

    private ItemData[] _medallionOrder;
    private MedallionHole _hoveredHole;
    private MedallionHole _ghostHole;
    private bool _solved;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Auto-find controller if not assigned.
        if (_controller == null)
            _controller = GetComponent<FinalDoorPuzzleController>();

        // Auto-discover door holes if not assigned.
        if (_doorHoles == null || _doorHoles.Length == 0 ||
            System.Array.TrueForAll(_doorHoles, h => h == null))
        {
            _doorHoles = GetComponentsInChildren<MedallionHole>(includeInactive: true);
        }
    }

    private void Update()
    {
        if (_solved || _controller == null || !_controller.IsActive) return;
        if (Mouse.current == null) return;

        var mousePos = Mouse.current.position.ReadValue();
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Ghost preview while dragging a medallion from the inventory bar.
        if (PuzzleInventoryBar.IsDragging && !overUI)
            UpdateGhostPreview(mousePos);
        else
            ClearGhost();

        // Hover highlight (only when not dragging — ghost takes priority).
        if (!PuzzleInventoryBar.IsDragging)
            UpdateHoverHighlight(overUI ? null : mousePos);

        // Click on a filled hole → retrieve.
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
    /// Called by FinalDoorPuzzleInteraction on enter.
    /// </summary>
    public void Populate(ItemData[] medallionOrder)
    {
        _medallionOrder = medallionOrder;
    }

    /// <summary>
    /// Marks the puzzle as solved — blocks all further interaction.
    /// </summary>
    public void MarkSolved()
    {
        _solved = true;
        ClearHover();
        ClearGhost();
    }

    /// <summary>Returns the item currently placed in each door hole (null if empty).</summary>
    public ItemData[] GetHoleStates()
    {
        if (_doorHoles == null) return Array.Empty<ItemData>();
        var states = new ItemData[_doorHoles.Length];
        for (int i = 0; i < _doorHoles.Length; i++)
            states[i] = _doorHoles[i] != null ? _doorHoles[i].PlacedItem : null;
        return states;
    }

    /// <summary>
    /// Restores hole states on load — fills holes immediately without animation.
    /// </summary>
    public void RestoreState(string[] placedItemIds, ItemData[] allItems)
    {
        for (int i = 0; i < _doorHoles.Length && i < placedItemIds.Length; i++)
        {
            var id = placedItemIds[i];
            if (string.IsNullOrEmpty(id)) continue;

            var item = FindItem(id, allItems);
            if (item != null)
                _doorHoles[i].FillImmediate(item, _coinPrefab);
        }

        CheckVictorySilent();
    }

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to place the dragged item on an empty hole via 3D raycast.
    /// Does NOT remove the item from inventory — PuzzleInventoryBar handles that.
    /// Clears the ghost preview before placing the real coin.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        ClearGhost();

        if (item == null || Camera.main == null || _solved) return false;

        // Only medallions belonging to this puzzle are accepted.
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
        if (!Physics.Raycast(ray, out var hit, 50f, _holeLayer, QueryTriggerInteraction.Collide))
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

    // ── Hover Highlight ───────────────────────────────────────────────────────

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

        var inv = InventorySystem.Instance;
        if (inv == null) return;

        inv.ReleaseAllReservations();
        inv.Compact();

        if (inv.IsFull) return;

        var item = hole.Retrieve(_dropHeight, _dropDuration);
        if (item == null) return;

        ClearHover();

        // Edge case: inventory filled between guard and AddItem — restore immediately.
        if (!inv.AddItem(item))
            hole.FillImmediate(item, _coinPrefab);
    }

    // ── Victory Checks ────────────────────────────────────────────────────────

    private void CheckVictory()
    {
        if (_solved || _medallionOrder == null) return;
        if (_doorHoles.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _doorHoles.Length; i++)
            if (_doorHoles[i].PlacedItem != _medallionOrder[i]) return;

        _solved = true;
        ClearHover();
        OnPuzzleSolved?.Invoke();
    }

    /// <summary>Sets _solved without firing the event. Used when restoring from a save.</summary>
    private void CheckVictorySilent()
    {
        if (_medallionOrder == null || _doorHoles.Length != _medallionOrder.Length) return;

        for (int i = 0; i < _doorHoles.Length; i++)
            if (_doorHoles[i].PlacedItem != _medallionOrder[i]) return;

        _solved = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ItemData FindItem(string id, ItemData[] items)
    {
        if (items == null) return null;
        foreach (var item in items)
            if (item != null && item.ItemId == id) return item;
        return null;
    }
}
