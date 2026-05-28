using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared horizontal inventory bar displayed at the bottom of the screen during puzzle interactions.
/// Shows all items from the player's inventory with left/right scroll buttons.
/// Any puzzle controller implementing <see cref="IPuzzleDropHandler"/> can use this bar.
///
/// Uses a CanvasGroup to show/hide so the GameObject stays active and Awake/Start always run.
///
/// SETUP:
///   1. Create a child of Canvas with this component.
///   2. Assign slotPrefab (a GameObject with <see cref="PuzzleInventorySlot"/>).
///   3. Assign slotContent (Transform inside the viewport where slots are spawned).
///   4. Assign scrollLeftButton and scrollRightButton.
///   5. The viewport parent of slotContent should have a RectMask2D to clip hidden slots.
///   6. A CanvasGroup is added automatically — no need to set up manually.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PuzzleInventoryBar : MonoBehaviour
{
    public static PuzzleInventoryBar Instance { get; private set; }

    private const float DefaultGhostAlpha = 0.85f;

    [Header("References")]
    [Tooltip("Transform inside the masked viewport where slot instances are parented.")]
    [SerializeField] private RectTransform slotContent;

    [Tooltip("Prefab with PuzzleInventorySlot component, background Image, and child Icon Image.")]
    [SerializeField] private PuzzleInventorySlot slotPrefab;

    [Tooltip("Button that scrolls the bar one slot to the left.")]
    [SerializeField] private Button scrollLeftButton;

    [Tooltip("Button that scrolls the bar one slot to the right.")]
    [SerializeField] private Button scrollRightButton;

    [Header("Settings")]
    [Tooltip("Number of slots visible at once without scrolling.")]
    [SerializeField] private int visibleSlotCount = 5;

    [Tooltip("Message shown when the dragged item is not accepted by the active puzzle handler.")]
    [SerializeField] private string _wrongItemMessage = "Этот предмет сюда не подходит";

    [Tooltip("Width and height of each slot in pixels.")]
    [SerializeField] private float slotSize = 80f;

    [Tooltip("Horizontal spacing between slots in pixels.")]
    [SerializeField] private float slotSpacing = 8f;

    [Tooltip("Size of the drag ghost image in pixels.")]
    [SerializeField] private float ghostSize = 64f;

    [Tooltip("Bar height = slotSize + barVerticalPadding. " +
             "Increase this if taller slots overflow the bar.")]
    [SerializeField] private float barVerticalPadding = 30f;

    [Tooltip("Gap in pixels between the slot edge and the item icon on each side. " +
             "Set to 0 to fill the slot completely.")]
    [SerializeField] private float iconPadding = 4f;

    // ── Pool & state ─────────────────────────────────────────────────────────

    private PuzzleInventorySlot[] _slotPool;
    private int _activeSlotCount;
    private int _filledSlotCount; // slots that actually contain an item
    private int _scrollIndex;
    private IPuzzleDropHandler _activeHandler;
    private bool _isOpen;

    // ── Drag state ───────────────────────────────────────────────────────────

    /// <summary>True while the player is dragging an item from the inventory bar.</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>The item currently being dragged, or null when not dragging.</summary>
    public static ItemData DraggedItem { get; private set; }

    private Image _dragGhost;
    private PuzzleInventorySlot _dragSource;
    private ItemData _dragItem;
    private bool _isDragging;
    private Canvas _rootCanvas;
    private CanvasGroup _canvasGroup;

    // Cached (barWidth - viewportWidth) / 2 — buttons + margins + gaps on one side.
    // Read once from the scene layout so the designer never needs to configure it manually.
    private float _buttonSideWidth;

    /// <summary>Width of one slot step used for scroll offset calculation.</summary>
    private float SlotStep => slotSize + slotSpacing;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        _canvasGroup = GetComponent<CanvasGroup>();

        CacheButtonSideWidth();
        SetBarVisible(false);
    }

    private void Start()
    {
        ApplyLayout();
        CreateSlotPool();

        if (scrollLeftButton != null)
            scrollLeftButton.onClick.AddListener(ScrollLeft);

        if (scrollRightButton != null)
            scrollRightButton.onClick.AddListener(ScrollRight);
    }

    private void OnDestroy()
    {
        if (scrollLeftButton != null)
            scrollLeftButton.onClick.RemoveListener(ScrollLeft);

        if (scrollRightButton != null)
            scrollRightButton.onClick.RemoveListener(ScrollRight);

        UnsubscribeFromInventory();

        if (Instance == this)
            Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the bar and binds it to the given puzzle drop handler.
    /// All inventory items are displayed — no filtering.
    /// </summary>
    public void Show(IPuzzleDropHandler handler)
    {
        // Allow handler to be null if we just want to display the inventory without drop interaction.
        _activeHandler = handler;
        _scrollIndex = 0;
        _isOpen = true;

        SubscribeToInventory();
        RefreshSlots();
        SetBarVisible(true);
    }

    /// <summary>Hides the bar and disconnects from the current puzzle handler.</summary>
    public void Hide()
    {
        if (!_isOpen) return;

        _isOpen = false;
        _activeHandler = null;

        CancelDragIfActive();
        UnsubscribeFromInventory();
        ItemTooltip.Instance?.Hide();
        SetBarVisible(false);
    }

    // ── Drag handlers (called by PuzzleInventorySlot) ─────────────────────────

    /// <summary>Begins a drag from the given slot — dims the source and spawns a ghost icon.</summary>
    public void OnSlotBeginDrag(PuzzleInventorySlot slot, PointerEventData eventData)
    {
        if (slot == null || !slot.HasItem) return;

        // Block new drags while the inspection/craft-preview window is open.
        if (ItemInspector.Instance != null && ItemInspector.Instance.IsInspecting) return;

        _dragSource = slot;
        _dragItem = slot.Item;
        _isDragging = true;
        IsDragging = true;
        DraggedItem = slot.Item;

        slot.SetDragVisual(dimmed: true);
        SpawnGhost(slot.Item.icon, eventData.position);
        UI.PuzzleCursor.Instance?.SetDragMode(true, _dragItem);
    }

    /// <summary>Moves the ghost to follow the cursor.</summary>
    public void OnSlotDrag(PointerEventData eventData)
    {
        if (_dragGhost != null)
            _dragGhost.rectTransform.position = eventData.position;
    }

    /// <summary>
    /// Ends the drag — first checks whether the drop landed on another bar slot for crafting.
    /// If crafting succeeds, inventory is updated automatically via OnInventoryChanged.
    /// Otherwise asks the active handler to accept the item.
    /// If rejected, the slot icon is restored.
    /// </summary>
    public void OnSlotEndDrag(PuzzleInventorySlot slot, PointerEventData eventData)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        IsDragging = false;
        DraggedItem = null;
        UI.PuzzleCursor.Instance?.SetDragMode(false, null);
        DestroyGhost();

        // Block all drop interactions while the inspection/craft-preview window is open.
        if (ItemInspector.Instance != null && ItemInspector.Instance.IsInspecting)
        {
            slot?.SetDragVisual(dimmed: false);
            _dragSource = null;
            _dragItem = null;
            return;
        }

        // Check if the drop landed on another bar slot for crafting.
        PuzzleInventorySlot targetBarSlot = FindHoveredBarSlot(eventData, exclude: slot);
        if (targetBarSlot != null && targetBarSlot.HasItem && _dragItem != null)
        {
            if (InventorySystem.Instance != null &&
                InventorySystem.Instance.TryCombineDeferred(
                    slot.SlotIndex, targetBarSlot.SlotIndex, out ItemData craftResult))
            {
                ShowCraftResult(craftResult);
                _dragSource = null;
                _dragItem = null;
                return;
            }
        }

        bool accepted = false;
        ItemData replacement = null;

        if (_dragItem != null && _activeHandler != null)
            accepted = _activeHandler.HandleDrop(_dragItem, eventData.position, out replacement);

        if (accepted)
        {
            if (replacement != null)
                InventorySystem.Instance?.ReplaceItem(_dragItem, replacement);
            else
                InventorySystem.Instance?.RemoveItem(_dragItem);
            // RefreshSlots will be called by OnInventoryChanged event
        }
        else
        {
            // Return visual — item stays in inventory
            slot?.SetDragVisual(dimmed: false);

            // Show warning only when there is an active puzzle handler and no bar slot was targeted.
            if (_dragItem != null && _activeHandler != null && targetBarSlot == null)
                PopupMessageSystem.Instance?.Show(_wrongItemMessage, PopupMessageType.Warning, 3f);
        }

        _dragSource = null;
        _dragItem = null;
    }

    /// <summary>
    /// Called by PuzzleInventorySlot.OnDrop when a slot is dropped on top of another.
    /// Secondary path — primary crafting runs in OnSlotEndDrag.
    /// </summary>
    public void OnSlotDropReceived(PuzzleInventorySlot targetSlot, PuzzleInventorySlot sourceSlot)
    {
        if (!targetSlot.HasItem || sourceSlot == null) return;

        // Block while the inspection/craft-preview window is open.
        if (ItemInspector.Instance != null && ItemInspector.Instance.IsInspecting) return;

        if (InventorySystem.Instance != null &&
            InventorySystem.Instance.TryCombineDeferred(
                sourceSlot.SlotIndex, targetSlot.SlotIndex, out ItemData craftResult))
        {
            ShowCraftResult(craftResult);
        }
    }

    /// <summary>
    /// Opens the ItemInspector preview for a freshly crafted item.
    /// The item is added to inventory only when the player confirms (clicks).
    /// Falls back to a direct AddItem call when the inspector is unavailable
    /// or the item has no inspection prefab.
    /// </summary>
    private void ShowCraftResult(ItemData result)
    {
        if (result == null) return;

        if (ItemInspector.Instance != null && result.inspectionPrefab != null)
        {
            ItemInspector.Instance.BeginInspection(result, null, item =>
            {
                InventorySystem.Instance?.AddItem(item);
            });
        }
        else
        {
            InventorySystem.Instance?.AddItem(result);
        }
    }

    // ── Scroll ────────────────────────────────────────────────────────────────

    private void ScrollLeft()
    {
        if (_scrollIndex <= 0) return;
        _scrollIndex--;
        UpdateContentPosition();
        UpdateButtonStates();
    }

    private void ScrollRight()
    {
        int maxIndex = Mathf.Max(0, _filledSlotCount - visibleSlotCount);
        if (_scrollIndex >= maxIndex) return;
        _scrollIndex++;
        UpdateContentPosition();
        UpdateButtonStates();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>Creates the full slot pool based on InventorySystem.MaxSlots.</summary>
    private void CreateSlotPool()
    {
        int poolSize = InventorySystem.Instance != null
            ? InventorySystem.Instance.MaxSlots
            : visibleSlotCount;

        _slotPool = new PuzzleInventorySlot[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            var slot = Instantiate(slotPrefab, slotContent);

            // Apply slot size from the Inspector field — slotSize is the single source of truth.
            var slotRT = slot.GetComponent<RectTransform>();
            if (slotRT != null) slotRT.sizeDelta = new Vector2(slotSize, slotSize);

            slot.ApplyIconPadding(iconPadding);
            slot.Init(this);
            slot.SlotIndex = i;
            slot.Clear();
            slot.gameObject.SetActive(false);
            _slotPool[i] = slot;
        }
    }

    /// <summary>
    /// Mirrors the inventory into the slot pool 1-to-1.
    /// Every inventory slot is always visible: filled slots show the item icon,
    /// empty slots show only the slot background without an icon.
    /// </summary>
    private void RefreshSlots()
    {
        var inv = InventorySystem.Instance;
        if (inv == null || _slotPool == null) return;

        int slotCount = Mathf.Min(inv.MaxSlots, _slotPool.Length);

        for (int i = 0; i < slotCount; i++)
        {
            _slotPool[i].gameObject.SetActive(true);

            var item = inv.GetItemAt(i);
            if (item != null)
                _slotPool[i].SetItem(item);
            else
                _slotPool[i].Clear(); // background visible, icon hidden
        }

        _activeSlotCount = slotCount;
        _filledSlotCount = 0;
        for (int i = 0; i < slotCount; i++)
            if (inv.GetItemAt(i) != null) _filledSlotCount++;

        // Hide pool slots that exceed the inventory size (shouldn't happen in normal use).
        for (int i = slotCount; i < _slotPool.Length; i++)
        {
            _slotPool[i].Clear();
            _slotPool[i].gameObject.SetActive(false);
        }

        ClampScrollIndex();
        UpdateContentPosition();
        UpdateButtonStates();
    }

    private void ClampScrollIndex()
    {
        int maxIndex = Mathf.Max(0, _filledSlotCount - visibleSlotCount);
        _scrollIndex = Mathf.Clamp(_scrollIndex, 0, maxIndex);
    }

    /// <summary>Shifts the slot content so that _scrollIndex is the first visible slot.</summary>
    private void UpdateContentPosition()
    {
        if (slotContent == null) return;
        float targetX = -_scrollIndex * SlotStep;
        slotContent.anchoredPosition = new Vector2(targetX, slotContent.anchoredPosition.y);
    }

    private void UpdateButtonStates()
    {
        int maxIndex = Mathf.Max(0, _filledSlotCount - visibleSlotCount);

        if (scrollLeftButton != null)
            scrollLeftButton.interactable = _scrollIndex > 0;

        if (scrollRightButton != null)
            scrollRightButton.interactable = _scrollIndex < maxIndex;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Caches the fixed side chrome (buttons + margins + gap) from the initial scene layout.
    /// Must be called in Awake before any resize happens.
    /// </summary>
    private void CacheButtonSideWidth()
    {
        var barRT      = GetComponent<RectTransform>();
        var viewportRT = slotContent != null ? slotContent.parent as RectTransform : null;

        if (barRT != null && viewportRT != null && viewportRT.sizeDelta.x > 0f)
            _buttonSideWidth = (barRT.sizeDelta.x - viewportRT.sizeDelta.x) * 0.5f;
        else
            _buttonSideWidth = 87f; // safe fallback matching the default scene setup
    }

    /// <summary>
    /// Resizes SlotViewport and the root bar RectTransform so that exactly
    /// <see cref="visibleSlotCount"/> slots fit without scrolling.
    /// Called automatically on Start and in the Editor via OnValidate — so changing
    /// <see cref="visibleSlotCount"/>, <see cref="slotSize"/>, or <see cref="slotSpacing"/>
    /// in the Inspector immediately updates the layout.
    /// </summary>
    private void ApplyLayout()
    {
        var barRT      = GetComponent<RectTransform>();
        var viewportRT = slotContent != null ? slotContent.parent as RectTransform : null;
        if (barRT == null || viewportRT == null) return;

        // In OnValidate _buttonSideWidth is not yet cached — recompute from current sizes.
        if (_buttonSideWidth == 0f)
            _buttonSideWidth = (barRT.sizeDelta.x - viewportRT.sizeDelta.x) * 0.5f;

        float viewportWidth = visibleSlotCount * slotSize +
                              Mathf.Max(0, visibleSlotCount - 1) * slotSpacing;

        viewportRT.sizeDelta = new Vector2(viewportWidth, viewportRT.sizeDelta.y);
        barRT.sizeDelta      = new Vector2(viewportWidth + _buttonSideWidth * 2f,
                                           slotSize + barVerticalPadding);

        // Keep HorizontalLayoutGroup spacing in sync with slotSpacing.
        var hlg = slotContent != null
            ? slotContent.GetComponent<HorizontalLayoutGroup>()
            : null;
        if (hlg != null)
            hlg.spacing = slotSpacing;
    }

    private void OnValidate()
    {
        ApplyLayout();

        // Update any already-instantiated slot instances immediately.
        // This makes Inspector changes take effect in Play Mode without restarting.
        if (slotContent == null) return;
        foreach (Transform child in slotContent)
        {
            var slotRT = child.GetComponent<RectTransform>();
            if (slotRT != null)
                slotRT.sizeDelta = new Vector2(slotSize, slotSize);

            var slot = child.GetComponent<PuzzleInventorySlot>();
            if (slot != null)
                slot.ApplyIconPadding(iconPadding);
        }
    }

    // ── Inventory subscription ────────────────────────────────────────────────

    private void SubscribeToInventory()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged += RefreshSlots;
    }

    private void UnsubscribeFromInventory()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= RefreshSlots;
    }

    // ── Ghost ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the hovered UI elements for a PuzzleInventorySlot belonging to this bar,
    /// excluding the slot currently being dragged.
    /// </summary>
    private PuzzleInventorySlot FindHoveredBarSlot(PointerEventData eventData, PuzzleInventorySlot exclude)
    {
        foreach (var hoveredObj in eventData.hovered)
        {
            var slot = hoveredObj.GetComponent<PuzzleInventorySlot>();
            if (slot != null && slot != exclude && IsPoolSlot(slot))
                return slot;
        }
        return null;
    }

    private bool IsPoolSlot(PuzzleInventorySlot slot)
    {
        if (_slotPool == null) return false;
        foreach (var poolSlot in _slotPool)
            if (poolSlot == slot) return true;
        return false;
    }

    // ── Ghost ──────────────────────────────────────────────────────────────────

    private void SpawnGhost(Sprite sprite, Vector2 screenPos)
    {
        if (_rootCanvas == null || sprite == null) return;

        var go = new GameObject("PuzzleDragGhost", typeof(RectTransform));
        go.transform.SetParent(_rootCanvas.transform, false);

        // Override sorting so the ghost always renders above every other UI element
        // on the same canvas, regardless of hierarchy position.
        var overrideCanvas = go.AddComponent<Canvas>();
        overrideCanvas.overrideSorting = true;
        overrideCanvas.sortingOrder = _rootCanvas.sortingOrder + 100;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(ghostSize, ghostSize);
        rt.position = screenPos;

        _dragGhost = go.AddComponent<Image>();
        _dragGhost.sprite = sprite;
        _dragGhost.raycastTarget = false;
        _dragGhost.color = new Color(1f, 1f, 1f, DefaultGhostAlpha);
    }

    private void DestroyGhost()
    {
        if (_dragGhost != null)
        {
            Destroy(_dragGhost.gameObject);
            _dragGhost = null;
        }
    }

    /// <summary>Cancels any active drag — restores the source slot and destroys the ghost.</summary>
    private void CancelDragIfActive()
    {
        if (!_isDragging) return;
        _isDragging = false;
        IsDragging = false;
        DraggedItem = null;
        _dragSource?.SetDragVisual(dimmed: false);
        DestroyGhost();
        UI.PuzzleCursor.Instance?.SetDragMode(false, null);
        _dragSource = null;
        _dragItem = null;
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows or hides the bar via CanvasGroup — the GameObject stays active
    /// so that Awake/Start always execute and Instance is always valid.
    /// </summary>
    private void SetBarVisible(bool visible)
    {
        if (_canvasGroup == null) return;

        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;
    }
}
