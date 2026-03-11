using UnityEngine;

/// <summary>
/// Manages the interactive state of a single room.
/// Lock/Unlock controls whether the player can interact with objects inside.
/// </summary>
public class RoomController : MonoBehaviour
{
    public bool IsUnlocked { get; private set; }

    private Collider[] _interactableColliders;

    private void Awake()
    {
        // Collect colliders only from objects that implement IInteractable
        var interactables = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        var colliderList = new System.Collections.Generic.List<Collider>();

        foreach (var mb in interactables)
        {
            if (mb is IInteractable && mb.TryGetComponent(out Collider col))
                colliderList.Add(col);
        }

        _interactableColliders = colliderList.ToArray();
    }

    /// <summary>Enables interaction with all IInteractable objects in this room.</summary>
    public void Unlock()
    {
        IsUnlocked = true;
        SetCollidersEnabled(true);
    }

    /// <summary>Disables interaction with all IInteractable objects in this room.</summary>
    public void Lock()
    {
        IsUnlocked = false;
        SetCollidersEnabled(false);
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (var col in _interactableColliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }
}
