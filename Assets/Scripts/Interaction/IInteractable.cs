public interface IInteractable
{
    /// <summary>Called when the player interacts with this object.</summary>
    void Interact();

    /// <summary>Returns the hint text to display when looking at this object.</summary>
    string GetInteractText();

    /// <summary>Returns true if the object can be picked up.</summary>
    bool IsPickable();
}
