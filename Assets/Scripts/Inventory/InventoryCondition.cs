using UnityEngine;

/// <summary>
/// ScriptableObject condition that checks whether a specific item is present in the inventory.
/// Can be reused by any game system to gate functionality behind inventory requirements.
/// </summary>
[CreateAssetMenu(menuName = "Game/Conditions/Inventory Condition")]
public class InventoryCondition : ScriptableObject
{
    [Tooltip("The item that must be present in the inventory for this condition to be met.")]
    public ItemData requiredItem;

    /// <summary>Returns true if the required item is currently in the player's inventory.</summary>
    public bool IsMet()
    {
        return InventorySystem.Instance != null && InventorySystem.Instance.HasItem(requiredItem);
    }
}
