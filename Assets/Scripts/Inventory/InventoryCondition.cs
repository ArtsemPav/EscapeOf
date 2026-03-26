using UnityEngine;

/// <summary>
/// ScriptableObject condition that checks whether a specific item (or any of several items)
/// is present in the inventory. Can be reused by any game system to gate functionality
/// behind inventory requirements.
/// </summary>
[CreateAssetMenu(menuName = "Game/Conditions/Inventory Condition")]
public class InventoryCondition : ScriptableObject
{
    [Tooltip("The item that must be present in the inventory for this condition to be met.")]
    public ItemData requiredItem;

    [Tooltip("Optional additional items — condition is met if ANY of these is in the inventory. " +
             "Useful when multiple variants of an item should satisfy the same condition.")]
    public ItemData[] anyOfItems;

    /// <summary>Returns true if the required item or any of the optional items is currently in inventory.</summary>
    public bool IsMet()
    {
        if (InventorySystem.Instance == null) return false;

        if (requiredItem != null && InventorySystem.Instance.HasItem(requiredItem))
            return true;

        if (anyOfItems != null)
        {
            foreach (ItemData item in anyOfItems)
            {
                if (item != null && InventorySystem.Instance.HasItem(item))
                    return true;
            }
        }

        return false;
    }
}
