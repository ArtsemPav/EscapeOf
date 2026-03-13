/// <summary>
/// Defines how the player's crosshair changes when looking at an interactable object.
/// </summary>
public enum CrosshairMode
{
    /// <summary>Standard dot/crosshair — nothing special nearby.</summary>
    Default,

    /// <summary>Open hand — object can be picked up or door can be opened.</summary>
    Hand,

    /// <summary>Lock icon — interaction is blocked (e.g. locked door).</summary>
    Locked,

    /// <summary>Grab/drag icon — physics object can be dragged.</summary>
    Grab
}

public interface IInteractable
{
    /// <summary>Called when the player interacts with this object.</summary>
    void Interact();

    /// <summary>Returns the hint text to display when looking at this object.</summary>
    string GetInteractText();

    /// <summary>Returns true if the object can be picked up.</summary>
    bool IsPickable();

    /// <summary>Returns the crosshair mode to show when looking at this object.</summary>
    CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
}
