using UnityEngine;

/// <summary>
/// Implemented by objects that respond to LMB hold + mouse movement.
/// FPSController detects when the focused IInteractable also implements IDraggable,
/// then routes mouse delta here instead of rotating the camera.
/// </summary>
public interface IDraggable
{
    /// <summary>
    /// Called once when the player presses LMB while looking at this object.
    /// <paramref name="hitPoint"/> is the world-space position of the raycast hit
    /// used to determine the correct drag direction regardless of camera angle.
    /// <paramref name="cam"/> is the player's active rendering camera for screen-space projections.
    /// </summary>
    void OnDragStart(Vector3 hitPoint, Camera cam);

    /// <summary>Called every frame while LMB is held. mouseDelta is raw screen-space pixels.</summary>
    void OnDrag(Vector2 mouseDelta);

    /// <summary>Called once when the player releases LMB.</summary>
    void OnDragEnd();
}

/// <summary>
/// Defines how the player's crosshair changes when looking at an interactable object.
/// </summary>
public enum CrosshairMode
{
    /// <summary>Standard dot/crosshair — nothing special nearby.</summary>
    Default,

    /// <summary>Open hand — object can be picked up or door can be opened.</summary>
    Hand,

    /// <summary>Lock icon — interaction is blocked (e.g. locked door, no key).</summary>
    Locked,

    /// <summary>Open lock icon — door is locked but the player has the required key.</summary>
    Unlocked,

    /// <summary>Grab/drag icon — physics object can be dragged.</summary>
    Grab,

    /// <summary>Read icon — object contains readable text (note, book, sign).</summary>
    Read,

    /// <summary>Point icon.</summary>
    Point,

    /// <summary>Item drag icon — displayed while dragging an item from PuzzleInventoryBar onto a 3D object.</summary>
    ItemDrag
}

public interface IInteractable
{
    /// <summary>
    /// Returns true when the object is ready to be interacted with.
    /// When false, FPSController skips this object entirely — no hint, no crosshair
    /// change, and Interact() will not be called.
    /// Default is true; override to add contextual blocking logic.
    /// </summary>
    bool CanInteract() => true;

    /// <summary>Called when the player interacts with this object.</summary>
    void Interact();

    /// <summary>Returns the hint text to display when looking at this object.</summary>
    string GetInteractText();

    /// <summary>Returns true if the object can be picked up.</summary>
    bool IsPickable();

    /// <summary>
    /// When true, FPSController triggers Interact() on LMB click in addition to the E key.
    /// Override to true for notes, pickups and other single-click interactions.
    /// Drag-based objects (IDraggable) are handled separately and ignore this flag.
    /// </summary>
    bool UseLMBClick => false;

    /// <summary>Returns the crosshair mode to show when looking at this object.</summary>
    CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    /// <summary>
    /// Returns a hint explaining why interaction is blocked (e.g. missing item).
    /// Return an empty string if the interaction is not blocked.
    /// </summary>
    string GetBlockedHint() => string.Empty;
}
