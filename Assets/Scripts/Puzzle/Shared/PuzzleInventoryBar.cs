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

    [Tooltip("Width and height of each slot in pixels.")]
    [SerializeField] private float slotSize = 80f;

    [Tooltip("Horizontal spacing between slots in pixels.")]
    [SerializeField] private float slotSpacing = 8f;

    [Tooltip("Size of the drag ghost image in pixels.")]
    [SerializeField] private float ghostSize = 64f;

    // ── Pool & state ─────────────────────────────────────────────────────────

    private PuzzleInventorySlot[] _slotPool;
    private int _activeSlotCount;
    private int _scrollIndex;
    private IPuzzleDropHandler _activeHandler;
    private bool _isOpen;

    // ── Drag state ───────────────────────────────────────────────────────────

    private Image _dragGhost;
    private PuzzleInventorySlot _dragSource;
    private ItemData _dragItem;
    private bool _isDragging;
    private Canvas _rootCanvas;
    private CanvasGroup _canvasGroup;

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

        SetBarVisible(false);
    }

    private void Start()
    {
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
        if (handler == null)
        {
            Debug.LogWarning("[PuzzleInventoryBar] Show called with null handler.", this);
            return;
        }

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

        _dragSource = slot;
        _dragItem = slot.Item;
        _isDragging = true;

        slot.SetDragVisual(dimmed: true);
        SpawnGhost(slot.Item.icon, eventData.position);
    }

    /// <summary>Moves the ghost to follow the cursor.</summary>
    public void OnSlotDrag(PointerEventData eventData)
    {
        if (_dragGhost != null)
            _dragGhost.rectTransform.position = eventData.position;
    }

    /// <summary>
    /// Ends the drag — asks the active handler to accept the item.
    /// If accepted, the item is removed from the inventory (bar refreshes via event).
    /// If rejected, the slot icon is restored.
    /// </summary>
    public void OnSlotEndDrag(PuzzleInventorySlot slot, PointerEventData eventData)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        DestroyGhost();

        bool accepted = false;

        if (_dragItem != null && _activeHandler != null)
            accepted = _activeHandler.HandleDrop(_dragItem, eventData.position);

        if (accepted)
        {
            InventorySystem.Instance?.RemoveItem(_dragItem);
            // RefreshSlots will be called by OnInventoryChanged event
        }
        else
        {
            // Return visual — item stays in inventory
            slot?.SetDragVisual(dimmed: false);
        }

        _dragSource = null;
        _dragItem = null;
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
        int maxIndex = Mathf.Max(0, _activeSlotCount - visibleSlotCount);
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
            slot.Init(this);
            slot.Clear();
            slot.gameObject.SetActive(false);
            _slotPool[i] = slot;
        }
    }

    /// <summary>
    /// Fills active slots with all non-null items from the inventory.
    /// Empty inventory slots are skipped — bar shows a compact list.
    /// </summary>
    private void RefreshSlots()
    {
        var inv = InventorySystem.Instance;
        if (inv == null || _slotPool == null) return;

        int displayIndex = 0;

        for (int i = 0; i < inv.MaxSlots; i++)
        {
            var item = inv.GetItemAt(i);
            if (item == null) continue;

            if (displayIndex < _slotPool.Length)
            {
                _slotPool[displayIndex].gameObject.SetActive(true);
                _slotPool[displayIndex].SetItem(item);
                displayIndex++;
            }
        }

        _activeSlotCount = displayIndex;

        // Deactivate remaining pool slots
        for (int i = displayIndex; i < _slotPool.Length; i++)
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
        int maxIndex = Mathf.Max(0, _activeSlotCount - visibleSlotCount);
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
        int maxIndex = Mathf.Max(0, _activeSlotCount - visibleSlotCount);

        if (scrollLeftButton != null)
            scrollLeftButton.interactable = _scrollIndex > 0;

        if (scrollRightButton != null)
            scrollRightButton.interactable = _scrollIndex < maxIndex;
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

    private void SpawnGhost(Sprite sprite, Vector2 screenPos)
    {
        if (_rootCanvas == null || sprite == null) return;

        var go = new GameObject("PuzzleDragGhost", typeof(RectTransform));
        go.transform.SetParent(_rootCanvas.transform, false);
        go.transform.SetAsLastSibling();

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
        _dragSource?.SetDragVisual(dimmed: false);
        DestroyGhost();
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
