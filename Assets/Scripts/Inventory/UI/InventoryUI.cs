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

    private PlayerInputActions _input;
    private bool _isOpen;
    private InventorySlot[] _slots;
    private FPSController _playerController;

    private void Awake()
    {
        _input = new PlayerInputActions();
        inventoryPanel.SetActive(false);
    }

    private void Start()
    {
        if (InventorySystem.Instance == null)
        {
            Debug.LogError("InventorySystem �� ������ � �����!", this);
            return;
        }

        CreateSlots();
        InventorySystem.Instance.OnInventoryChanged += RefreshSlots;
        _playerController = GetComponentInParent<FPSController>(includeInactive: true)
                            ?? Object.FindFirstObjectByType<FPSController>();

        if (_playerController == null)
            Debug.LogWarning("InventoryUI: FPSController не найден — камера не будет блокироваться.", this);
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
        int count = InventorySystem.Instance.MaxSlots;
        _slots = new InventorySlot[count];

        for (int i = 0; i < count; i++)
        {
            _slots[i] = Instantiate(slotPrefab, slotsContainer);
            _slots[i].SlotIndex = i;
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
        _playerController?.SetPlayerInputEnabled(false);
        RefreshSlots();
    }

    private void CloseInventory()
    {
        _isOpen = false;
        ItemTooltip.Instance?.Hide();
        inventoryPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _playerController?.SetPlayerInputEnabled(true);
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
