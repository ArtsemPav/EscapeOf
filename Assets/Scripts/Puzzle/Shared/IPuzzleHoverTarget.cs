/// <summary>
/// Implemented by 3D puzzle slots that hold a retrievable item. PuzzleCursor
/// raycasts the PuzzleInteractable layer while a puzzle mode is active and:
/// - shows <see cref="HoverCrosshair"/> (e.g. the hand) when <see cref="CanRetrieve"/> is true;
/// - calls <see cref="SetHover"/> so the slot can highlight/unhighlight the placed item;
/// - unhighlights via SetHover(false) when the cursor leaves or puzzle mode exits,
///   so the highlight can never stay stuck after leaving the puzzle.
/// </summary>
public interface IPuzzleHoverTarget
{
    /// <summary>True when the cursor hover feedback (hand cursor + highlight) applies.</summary>
    bool CanRetrieve { get; }

    /// <summary>Crosshair mode shown while hovering a retrievable slot.</summary>
    CrosshairMode HoverCrosshair { get; }

    /// <summary>Called when the cursor hover starts (true) or ends (false).</summary>
    void SetHover(bool on);
}
