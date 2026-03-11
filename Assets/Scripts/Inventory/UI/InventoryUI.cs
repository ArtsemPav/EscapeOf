using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the inventory panel visibility.
/// Toggles on Tab, locks/unlocks cursor and player input accordingly.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventorySlot slotPrefab;
    [SerializeField] private Transform slotsContainer;

    private PlayerInputActions _input;
    private bool _isOpen;

    private void Start()
    {
        if (InventorySystem.Instance == null)
        {
            Debug.LogError("InventorySystem не найден в сцене! Добавь GameObject с компонентом InventorySystem.", this);
            return;
        }

        InventorySystem.Instance.OnInventoryChanged += RefreshSlots;
    }
    private void Awake()
    {
        _input = new PlayerInputActions();
        inventoryPanel.SetActive(false);
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

    private void RefreshSlots()
    {
        foreach (Transform child in slotsContainer)
            Destroy(child.gameObject);

        foreach (var item in InventorySystem.Instance.Items)
        {
            InventorySlot slot = Instantiate(slotPrefab, slotsContainer);
            slot.Setup(item);
        }
    }
}
