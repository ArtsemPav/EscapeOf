using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the inventory panel visibility.
/// Creates a fixed number of slots. Fills them with items on refresh.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventorySlot slotPrefab;
    [SerializeField] private Transform slotsContainer;

    [Header("Settings")]
    [SerializeField] private int slotCount = 8;

    private PlayerInputActions _input;
    private bool _isOpen;
    private InventorySlot[] _slots;

    private void Awake()
    {
        _input = new PlayerInputActions();
        inventoryPanel.SetActive(false);
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
        _input.Player.Enable();
        _input.Player.Inventory.performed += OnToggleInventory;
    }

    private void OnDisable()
    {
        _input.Player.Inventory.performed -= OnToggleInventory;
        _input.Player.Disable();

        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= RefreshSlots;
    }

    /// <summary>Creates all slots once at start. Slots are reused, not recreated.</summary>
    private void CreateSlots()
    {
        _slots = new InventorySlot[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            _slots[i] = Instantiate(slotPrefab, slotsContainer);
            _slots[i].Clear();
        }
    }

    private void OnToggleInventory(InputAction.CallbackContext ctx)
    {
        if (_isOpen) CloseInventory();
        else OpenInventory();
    }

    private void OpenInventory()
    {
        _isOpen = true;
        inventoryPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RefreshSlots();
    }

    private void CloseInventory()
    {
        _isOpen = false;
        inventoryPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Fills slots with items from inventory in order.
    /// Empty slots are cleared and show only the background.
    /// </summary>
    private void RefreshSlots()
    {
        if (_slots == null) return;

        var items = InventorySystem.Instance.Items;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (i < items.Count)
                _slots[i].Setup(items[i]);
            else
                _slots[i].Clear();
        }
    }
}
