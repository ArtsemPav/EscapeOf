using UnityEngine;

/// <summary>
/// Place on any world object to make it pickable.
/// When the player interacts — item is added to inventory and object is destroyed.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;

    /// <summary>Picks up the item: adds to inventory and removes from world.</summary>
    public void Interact()
    {
        if (itemData == null)
        {
            Debug.LogWarning($"PickableItem on {gameObject.name} has no ItemData assigned.", this);
            return;
        }

        InventorySystem.Instance.AddItem(itemData);
        Destroy(gameObject);
    }
}
