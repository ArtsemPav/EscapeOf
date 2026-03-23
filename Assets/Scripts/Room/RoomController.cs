using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the interactive state of a single room.
/// Lock/Unlock controls whether the player can interact with objects inside.
/// Optionally holds a local post-processing Volume that GameManager enables/disables on pause.
/// </summary>
public class RoomController : MonoBehaviour
{
    [Tooltip("Local post-processing Volume for this room. Leave empty if the room uses no post-processing.")]
    [SerializeField] private Volume _localVolume;

    public bool IsUnlocked { get; private set; }

    /// <summary>The local post-processing Volume assigned to this room. May be null.</summary>
    public Volume LocalVolume => _localVolume;

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
        if (_interactableColliders == null) return;
        foreach (var col in _interactableColliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }
}
