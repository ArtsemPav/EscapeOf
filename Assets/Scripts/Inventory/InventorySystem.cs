using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that holds the player's items and handles crafting logic.
/// Does not depend on any UI — fires events for UI to react.
/// </summary>
public class InventorySystem : MonoBehaviour
{
    public static InventorySystem Instance { get; private set; }

    [Header("Crafting")]
    [SerializeField] private CraftingRecipe[] recipes;

    private readonly List<ItemData> _items = new();

    public IReadOnlyList<ItemData> Items => _items;

    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>Adds an item to the inventory and notifies listeners.</summary>
    public void AddItem(ItemData item)
    {
        if (item == null) return;
        _items.Add(item);
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Removes an item from the inventory and notifies listeners.</summary>
    public bool RemoveItem(ItemData item)
    {
        bool removed = _items.Remove(item);
        if (removed) OnInventoryChanged?.Invoke();
        return removed;
    }

    /// <summary>Returns true if the inventory contains the given item.</summary>
    public bool HasItem(ItemData item) => _items.Contains(item);

    /// <summary>
    /// Tries to combine two items using registered recipes.
    /// On success removes both ingredients, adds result and returns true.
    /// </summary>
    public bool TryCombine(ItemData a, ItemData b, out ItemData result)
    {
        foreach (var recipe in recipes)
        {
            bool match = (recipe.ingredientA == a && recipe.ingredientB == b)
                      || (recipe.ingredientA == b && recipe.ingredientB == a);

            if (!match) continue;

            result = recipe.result;
            _items.Remove(a);
            _items.Remove(b);
            _items.Add(result);
            OnInventoryChanged?.Invoke();
            return true;
        }

        result = null;
        return false;
    }
}
