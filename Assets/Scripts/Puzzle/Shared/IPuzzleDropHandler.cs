using UnityEngine;

/// <summary>
/// Interface for puzzle controllers that accept items dragged from the PuzzleInventoryBar.
/// Implement on any puzzle that needs to receive items from the shared inventory bar.
/// </summary>
public interface IPuzzleDropHandler
{
    /// <summary>
    /// Attempts to place the dragged item at the given screen position.
    /// The bar handles inventory removal — implementations must NOT call InventorySystem.RemoveItem.
    /// </summary>
    /// <param name="item">The item being dropped.</param>
    /// <param name="screenPosition">Cursor position in screen coordinates at the moment of release.</param>
    /// <returns>True if the item was accepted and placed; false to return it to the bar.</returns>
    bool HandleDrop(ItemData item, Vector2 screenPosition);
}
