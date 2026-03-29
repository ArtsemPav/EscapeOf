using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Full-screen transparent overlay placed behind the InventoryPanel.
/// A left-click anywhere on this backdrop closes the inventory.
/// </summary>
public class InventoryBackdrop : MonoBehaviour, IPointerClickHandler
{
    /// <summary>Closes the inventory when the player clicks outside the panel area.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            InventoryUI.Instance?.CloseInventory();
    }
}
