/// <summary>
/// Interface for 3D scene objects that can serve as a drop target when the player
/// drags an item from PuzzleInventoryBar. Implement alongside <see cref="IPuzzleDropHandler"/>
/// on puzzle controllers that need hover-feedback during drag.
/// </summary>
public interface IPuzzleDropTarget
{
    /// <summary>Returns the hint text to display when hovering with a dragged item.</summary>
    string GetDropHint();

    /// <summary>Returns true when this target is compatible with the given item (used for preview).</summary>
    bool CanAccept(ItemData item);
}
