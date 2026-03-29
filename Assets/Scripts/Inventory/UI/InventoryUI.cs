using UnityEngine;

/// <summary>
/// Controls the inventory panel visibility.
/// Creates a fixed number of slots. Fills them with items on refresh.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject inventoryBackdrop;
    [SerializeField] private InventorySlot slotPrefab;
    [SerializeField] private Transform slotsContainer;

    private bool _isOpen;
    private InventorySlot[] _slots;

    private void Awake()
    {
        Instance = this;
        inventoryPanel.SetActive(false);
        if (inventoryBackdrop != null) inventoryBackdrop.SetActive(false);
    }

    private void Start()
    {
        if (InventorySystem.Instance == null)
        {
            Debug.LogError("InventorySystem не найден в сцене!", this);
            return;
        }

        CreateSlots();
        InventorySystem.Instance.OnInventoryChanged += RefreshSlots;
    }

    private void OnEnable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnInventoryPerformed += OnToggleInventory;
        }
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnInventoryPerformed -= OnToggleInventory;
        }

        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= RefreshSlots;
    }

    /// <summary>Creates all slots once at start. Slots are reused, not recreated.</summary>
    private void CreateSlots()
    {
        int count = InventorySystem.Instance.MaxSlots;
        _slots = new InventorySlot[count];

        for (int i = 0; i < count; i++)
        {
            _slots[i] = Instantiate(slotPrefab, slotsContainer);
            _slots[i].SlotIndex = i;
            _slots[i].Clear();
        }
    }

    private void OnToggleInventory()
    {
        if (_isOpen)
            CloseInventory();
        else if (UIManager.Instance == null || !UIManager.Instance.IsAnyPanelOpen)
            OpenInventory();
    }

    private void OpenInventory()
    {
        _isOpen = true;
        if (inventoryBackdrop != null) inventoryBackdrop.SetActive(true);
        UIManager.Instance?.OpenPanel(inventoryPanel);
        RefreshSlots();

        // Auto-show the first available item in the embedded preview panel.
        for (int i = 0; i < InventorySystem.Instance.MaxSlots; i++)
        {
            ItemData first = InventorySystem.Instance.GetItemAt(i);
            if (first != null)
            {
                InventoryItemPreview.Instance?.Show(first);
                break;
            }
        }
    }

    /// <summary>Closes the inventory. Safe to call from external scripts (e.g., InventoryBackdrop).</summary>
    public void CloseInventory()
    {
        if (!_isOpen) return;
        _isOpen = false;
        ItemTooltip.Instance?.Hide();
        InventoryItemPreview.Instance?.Clear();
        if (inventoryBackdrop != null) inventoryBackdrop.SetActive(false);

        // CancelPreviewIfActive is needed for the crafting case: if BeginPreview was
        // called (e.g., from OnDrop crafting), it opened InspectionPanel (incrementing
        // UIManager._openPanelCount). Without closing it here the count would go out of
        // sync and IsAnyPanelOpen would stay true, blocking all player interaction.
        ItemInspector.Instance?.CancelPreviewIfActive();

        UIManager.Instance?.ClosePanel(inventoryPanel);
    }

    /// <summary>
    /// Fills slots with items from inventory by slot index.
    /// Empty slots are cleared and show only the background.
    /// </summary>
    private void RefreshSlots()
    {
        if (_slots == null) return;

        for (int i = 0; i < _slots.Length; i++)
            _slots[i].Setup(InventorySystem.Instance.GetItemAt(i));
    }
}
