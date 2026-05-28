using UnityEngine;

/// <summary>
/// Interface for puzzle controllers that accept items dragged from the PuzzleInventoryBar.
/// Implement on any puzzle that needs to receive items from the shared inventory bar.
/// </summary>
public interface IPuzzleDropHandler
{
    /// <summary>
    /// Attempts to place the dragged item at the given screen position.
    /// The bar handles inventory manipulation — implementations must NOT call InventorySystem directly.
    /// </summary>
    /// <param name="item">The item being dropped.</param>
    /// <param name="screenPosition">Cursor position in screen coordinates at the moment of release.</param>
    /// <param name="replacement">
    /// When non-null, the bar replaces the dragged item with this item instead of removing it.
    /// Use to return an empty container (e.g. an empty flask) when a device consumes the contents.
    /// </param>
    /// <returns>True if the item was accepted; false to return it to the bar unchanged.</returns>
    bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement);
}
