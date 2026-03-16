using UnityEngine;

/// <summary>
/// Place on any world object to make it pickable.
/// When the player interacts � item is added to inventory and object is destroyed.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;

    /// <summary>Opens inspection view, or picks up directly if no inspectionPrefab is set.</summary>
    public void Interact()
    {
        if (itemData == null)
        {
            Debug.LogWarning($"PickableItem on {gameObject.name} has no ItemData assigned.", this);
            return;
        }

        if (ItemInspector.Instance != null)
            ItemInspector.Instance.BeginInspection(itemData, gameObject);
        else
        {
            InventorySystem.Instance.AddItem(itemData);
            Destroy(gameObject);
        }
    }

    public string GetInteractText()
    {
        string prefix = UIManager.Instance?.Config?.pickUpPrefix ?? "Взять";
        return itemData != null ? $"{prefix} {itemData.itemName}" : prefix;
    }

    public bool IsPickable() => true;
    public bool UseLMBClick => true;
}
