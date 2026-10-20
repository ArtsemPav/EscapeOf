/// <summary>
/// Optional interface for drop targets that want visual feedback while a dragged
/// item is hovered over them (and immediately removed when the cursor leaves or
/// the drag ends). Implement on the same component as <see cref="IPuzzleDropTarget"/>.
/// </summary>
public interface IPuzzleDropHoverHandler
{
    /// <summary>
    /// Called when a compatible dragged item starts hovering over the target
    /// (<c>true</c>) or stops hovering / the drag ends (<c>false</c>).
    /// </summary>
    void SetDragHover(bool isHovering);
}
