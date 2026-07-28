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
    [HideInInspector]
    [SerializeField] private CraftingRecipe[] recipes;

    // Populated automatically by InventoryAutoPopulate (Editor script).
    // Do not edit manually — add ItemData assets to Assets/Data/Items instead.
    [HideInInspector]
    [SerializeField] private ItemData[] _allItems;

    private ItemData[] _slots;

    // Indices of slots that are cleared but waiting for a device result to return.
    // AddItem skips these slots so they cannot be claimed by another operation
    // while a device (Burner, Centrifuge, Analyzer) is still processing the item.
    // PlaceItemAt always clears the reservation when it writes to a slot.
    private readonly System.Collections.Generic.HashSet<int> _reservedSlots =
        new System.Collections.Generic.HashSet<int>();

    public int MaxSlots => maxSlots;

    public event Action OnInventoryChanged;

    /// <summary>Fires whenever a crafting recipe is successfully matched and executed.</summary>
    public event Action OnCrafted;

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

        CompactSlots();
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
        if (string.IsNullOrEmpty(id)) return null;
        if (_allItems == null || _allItems.Length == 0)
        {
            Debug.LogWarning("[InventorySystem] _allItems is empty — inventory cannot be restored. " +
                             "Run Tools → Inventory → Refresh Items and Recipes.", this);
            return null;
        }
        foreach (var item in _allItems)
            if (item != null && item.ItemId == id) return item;

        Debug.LogWarning($"[InventorySystem] Item '{id}' not found. " +
                         "Add its ItemData to Assets/Data/Items and run Tools → Inventory → Refresh Items and Recipes.", this);
        return null;
    }

    /// <summary>
    /// Places <paramref name="item"/> into the first empty, non-reserved slot.
    /// Does NOT fire events or save — the caller is responsible for that.
    /// Returns true on success, false if no slot is available.
    /// </summary>
    private bool PlaceInFirstEmptySlot(ItemData item)
    {
        if (item == null) return false;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null || _reservedSlots.Contains(i)) continue;
            _slots[i] = item;
            return true;
        }
        Debug.LogWarning($"[InventorySystem] No free slot for secondary craft result '{item.name}'.", this);
        return false;
    }

    /// <summary>
    /// Shifts all non-null items to the left, eliminating gaps.
    /// Called automatically after every remove or combine operation.
    /// </summary>
    private void CompactSlots()
    {
        int write = 0;
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] != null)
                _slots[write++] = _slots[i];
        for (int i = write; i < _slots.Length; i++)
            _slots[i] = null;
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

    /// <summary>True when every inventory slot is occupied.</summary>
    public bool IsFull
    {
        get
        {
            foreach (var slot in _slots)
                if (slot == null) return false;
            return true;
        }
    }

    /// <summary>
    /// Adds item to the first empty, non-reserved slot.
    /// Returns true on success, false if the inventory is full (item is NOT consumed).
    /// Reserved slots are skipped — they are waiting for a device result via PlaceItemAt.
    /// </summary>
    public bool AddItem(ItemData item)
    {
        if (item == null) return false;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null || _reservedSlots.Contains(i)) continue;
            _slots[i] = item;
            OnInventoryChanged?.Invoke();
            SaveManager.Instance?.Save();
            return true;
        }

        Debug.LogWarning("Inventory is full — cannot add item.", this);
        return false;
    }

    /// <summary>Removes the item from its slot. Returns true on success.</summary>
    public bool RemoveItem(ItemData item)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != item) continue;
            _slots[i] = null;
            CompactSlots();
            OnInventoryChanged?.Invoke();
            SaveManager.Instance?.Save();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears the item at a specific slot index WITHOUT compacting.
    /// The slot becomes visually empty while keeping every other slot in place.
    /// When <paramref name="reserve"/> is true, the slot is also marked as reserved so
    /// <see cref="AddItem"/> cannot claim it while a device is processing the item.
    /// Call <see cref="PlaceItemAt"/> to write the result back and release the reservation.
    /// Returns true when the slot contained an item.
    /// </summary>
    public bool ClearSlot(int slotIndex, bool reserve = false)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
        if (_slots[slotIndex] == null) return false;
        _slots[slotIndex] = null;
        if (reserve) _reservedSlots.Add(slotIndex);
        OnInventoryChanged?.Invoke();
        SaveManager.Instance?.Save();
        return true;
    }

    /// <summary>
    /// Places <paramref name="item"/> at the given <paramref name="slotIndex"/> when that slot
    /// is currently empty. Falls back to <see cref="AddItem"/> when the slot is occupied or
    /// the index is out of range.
    /// Always clears any reservation on <paramref name="slotIndex"/> first, so the slot
    /// becomes available to <see cref="AddItem"/> if the fallback path is taken.
    /// Does NOT compact — use after <see cref="ClearSlot"/> to restore the same position.
    /// </summary>
    public bool PlaceItemAt(int slotIndex, ItemData item)
    {
        if (item == null) return false;

        // Always release the reservation — whether we succeed or fall back.
        _reservedSlots.Remove(slotIndex);

        if (slotIndex >= 0 && slotIndex < _slots.Length && _slots[slotIndex] == null)
        {
            _slots[slotIndex] = item;
            OnInventoryChanged?.Invoke();
            SaveManager.Instance?.Save();
            return true;
        }
        return AddItem(item); // fallback: first available empty slot
    }

    /// <summary>
    /// Replaces the first slot containing <paramref name="from"/> with <paramref name="to"/>.
    /// Returns true when a replacement was made; false when <paramref name="from"/> was not found.
    /// Does NOT compact slots — position is preserved intentionally.
    /// </summary>
    public bool ReplaceItem(ItemData from, ItemData to)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != from) continue;
            _slots[i] = to;
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
    /// Does NOT swap when either slot is reserved — reserved slots are waiting
    /// for a device result and must not be claimed by a manual swap.
    /// Returns true when the swap was performed, false when it was blocked.
    /// </summary>
    public bool SwapSlots(int slotA, int slotB)
    {
        if (slotA < 0 || slotB < 0 || slotA >= _slots.Length || slotB >= _slots.Length) return false;
        if (slotA == slotB) return false;
        if (_reservedSlots.Contains(slotA) || _reservedSlots.Contains(slotB)) return false;

        (_slots[slotA], _slots[slotB]) = (_slots[slotB], _slots[slotA]);
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Attempts to combine items from two slots without placing the result into inventory.
    /// If a matching recipe is found, the ingredients are consumed and <paramref name="result"/>
    /// is returned for the caller to handle (e.g. show an inspection preview first).
    /// <para>When both ingredients are conserved by the recipe, nothing is consumed and
    /// <paramref name="result"/> is still returned — the caller decides how to proceed.</para>
    /// Fires <see cref="OnInventoryChanged"/> only when at least one ingredient is consumed.
    /// </summary>
    public bool TryCombineDeferred(int sourceSlotIndex, int targetSlotIndex, out ItemData result)
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
            bool aMatchesSource = recipe.ingredientA == a && recipe.ingredientB == b;
            bool aMatchesTarget = recipe.ingredientA == b && recipe.ingredientB == a;

            if (!aMatchesSource && !aMatchesTarget) continue;

            result = recipe.result;

            int slotIndexA = aMatchesSource ? sourceSlotIndex : targetSlotIndex;
            int slotIndexB = aMatchesSource ? targetSlotIndex : sourceSlotIndex;

            bool inventoryChanged = false;

            if (!recipe.conserveIngredientA && !recipe.conserveIngredientB)
            {
                _slots[sourceSlotIndex] = null;
                _slots[targetSlotIndex] = null;
                inventoryChanged = true;
            }
            else if (!recipe.conserveIngredientA)
            {
                _slots[slotIndexA] = null;
                inventoryChanged = true;
            }
            else if (!recipe.conserveIngredientB)
            {
                _slots[slotIndexB] = null;
                inventoryChanged = true;
            }

            if (inventoryChanged)
            {
                // Do NOT compact here — result placement is deferred to the caller.
                // The caller uses PlaceItemAt to put the result at the source slot,
                // then calls Compact() to eliminate the remaining hole.
                OnInventoryChanged?.Invoke();
            }

            if (recipe.secondaryResult != null)
            {
                PlaceInFirstEmptySlot(recipe.secondaryResult);
                OnInventoryChanged?.Invoke();
            }

            OnCrafted?.Invoke();
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Releases a slot reservation without placing any item into it.
    /// Call this when a puzzle permanently consumes an item and will never return a result
    /// to the reserved slot (e.g. the medallion box).
    /// </summary>
    public void ReleaseReservation(int slotIndex)
    {
        _reservedSlots.Remove(slotIndex);
    }

    /// <summary>
    /// Releases all slot reservations at once.
    /// Use when a puzzle that permanently consumes items needs to return them to inventory
    /// without worrying about which specific slots were reserved.
    /// </summary>
    public void ReleaseAllReservations()
    {
        _reservedSlots.Clear();
    }

    /// <summary>
    /// Compacts all slots, shifting non-null items to the left and eliminating gaps.
    /// Call this after a deferred result has been placed to clean up any remaining holes.
    /// Fires <see cref="OnInventoryChanged"/> and triggers a save.
    /// </summary>
    public void Compact()
    {
        CompactSlots();
        OnInventoryChanged?.Invoke();
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Tries to combine items from two slots using registered recipes.
    /// Respects conserveIngredientA / conserveIngredientB flags on the matched recipe:
    /// the consumed slot receives the result; preserved slots are not modified.
    /// If both ingredients are conserved, the result is added to the first free slot.
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
            bool aMatchesSource = recipe.ingredientA == a && recipe.ingredientB == b;
            bool aMatchesTarget = recipe.ingredientA == b && recipe.ingredientB == a;

            if (!aMatchesSource && !aMatchesTarget) continue;

            result = recipe.result;

            // Determine which slot index holds ingredientA and which holds ingredientB.
            int slotIndexA = aMatchesSource ? sourceSlotIndex : targetSlotIndex;
            int slotIndexB = aMatchesSource ? targetSlotIndex : sourceSlotIndex;

            if (!recipe.conserveIngredientA && !recipe.conserveIngredientB)
            {
                // Default behaviour: result goes into target slot, source slot is cleared.
                _slots[targetSlotIndex] = result;
                _slots[sourceSlotIndex] = null;
            }
            else if (!recipe.conserveIngredientA)
            {
                // Ingredient A is consumed: result replaces it; ingredient B stays untouched.
                _slots[slotIndexA] = result;
            }
            else if (!recipe.conserveIngredientB)
            {
                // Ingredient B is consumed: result replaces it; ingredient A stays untouched.
                _slots[slotIndexB] = result;
            }
            else
            {
                // Both ingredients are preserved: add result to the first free slot.
                if (!AddItem(result))
                {
                    result = null;
                    return false;
                }
                if (recipe.secondaryResult != null)
                {
                    AddItem(recipe.secondaryResult);
                }
                // AddItem already fires OnInventoryChanged, so return early.
                OnCrafted?.Invoke();
                return true;
            }

            if (recipe.secondaryResult != null)
            {
                PlaceInFirstEmptySlot(recipe.secondaryResult);
            }

            CompactSlots();
            OnInventoryChanged?.Invoke();
            OnCrafted?.Invoke();
            return true;
        }

        result = null;
        return false;
    }

}
