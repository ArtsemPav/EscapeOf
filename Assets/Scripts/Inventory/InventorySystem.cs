using System;
using UnityEngine;

/// <summary>
/// Singleton that holds the player's items in fixed-position slots and handles crafting.
/// Slot positions are preserved during drag-and-drop reordering.
/// Does not depend on any UI — fires events for UI to react.
/// Implements ISaveable: registers with SaveManager to persist inventory across sessions.
/// </summary>
public class InventorySystem : MonoBehaviour, ISaveable
{
    public static InventorySystem Instance { get; private set; }

    [Header("Inventory")]
    [SerializeField] private int maxSlots = 8;

    [Header("Crafting")]
    [SerializeField] private CraftingRecipe[] recipes;

    [Header("Save")]
    [Tooltip("All ItemData assets in the game. Required for restoring inventory on load. Assign every item here.")]
    [SerializeField] private ItemData[] _allItems;

    private ItemData[] _slots;

    public int MaxSlots => maxSlots;

    public event Action OnInventoryChanged;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "inventory";

    /// <summary>Serializes current slot contents as an array of item IDs.</summary>
    public string GetSaveData()
    {
        var slotIds = new string[_slots.Length];
        for (int i = 0; i < _slots.Length; i++)
            slotIds[i] = _slots[i] != null ? _slots[i].ItemId : null;
        return JsonUtility.ToJson(new InventorySaveData { slotIds = slotIds });
    }

    /// <summary>Restores slot contents from saved item IDs using the _allItems database.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<InventorySaveData>(json);
        if (data.slotIds == null) return;

        for (int i = 0; i < _slots.Length && i < data.slotIds.Length; i++)
            _slots[i] = FindItemById(data.slotIds[i]);

        OnInventoryChanged?.Invoke();
    }

    /// <summary>Clears all slots. Used when resetting save progress.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = null;
        OnInventoryChanged?.Invoke();
    }

    private ItemData FindItemById(string id)
    {
        if (string.IsNullOrEmpty(id) || _allItems == null) return null;
        foreach (var item in _allItems)
            if (item != null && item.ItemId == id) return item;
        return null;
    }

    [Serializable]
    private struct InventorySaveData
    {
        public string[] slotIds;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _slots = new ItemData[maxSlots];
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Returns the item at a given slot index, or null if the slot is empty.</summary>
    public ItemData GetItemAt(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _slots.Length ? _slots[slotIndex] : null;

    /// <summary>Adds item to the first empty slot. Logs a warning if inventory is full.</summary>
    public void AddItem(ItemData item)
    {
        if (item == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null) continue;
            _slots[i] = item;
            OnInventoryChanged?.Invoke();
            SaveManager.Instance?.Save();
            return;
        }

        Debug.LogWarning("Inventory is full — cannot add item.", this);
    }

    /// <summary>Removes the item from its slot. Returns true on success.</summary>
    public bool RemoveItem(ItemData item)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != item) continue;
            _slots[i] = null;
            OnInventoryChanged?.Invoke();
            SaveManager.Instance?.Save();
            return true;
        }
        return false;
    }

    /// <summary>Returns true if the inventory contains the given item.</summary>
    public bool HasItem(ItemData item)
    {
        foreach (var slot in _slots)
            if (slot == item) return true;
        return false;
    }

    /// <summary>
    /// Swaps items between two slot indices.
    /// Works with empty slots — effectively moves an item to an empty slot.
    /// </summary>
    public void SwapSlots(int slotA, int slotB)
    {
        if (slotA < 0 || slotB < 0 || slotA >= _slots.Length || slotB >= _slots.Length) return;
        if (slotA == slotB) return;

        (_slots[slotA], _slots[slotB]) = (_slots[slotB], _slots[slotA]);
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Tries to combine items from two slots using registered recipes.
    /// On success, places the result in the source slot and clears the target slot.
    /// </summary>
    public bool TryCombine(int sourceSlotIndex, int targetSlotIndex, out ItemData result)
    {
        ItemData a = GetItemAt(sourceSlotIndex);
        ItemData b = GetItemAt(targetSlotIndex);

        if (a == null || b == null)
        {
            result = null;
            return false;
        }

        foreach (var recipe in recipes)
        {
            bool match = (recipe.ingredientA == a && recipe.ingredientB == b)
                      || (recipe.ingredientA == b && recipe.ingredientB == a);

            if (!match) continue;

            result = recipe.result;
            _slots[targetSlotIndex] = result;
            _slots[sourceSlotIndex] = null;
            OnInventoryChanged?.Invoke();
            return true;
        }

        result = null;
        return false;
    }

}
