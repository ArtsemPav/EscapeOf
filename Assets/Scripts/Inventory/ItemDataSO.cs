using UnityEngine;

/// <summary>
/// ScriptableObject describing a single item in the game.
/// Create instances via Assets > Create > Inventory > Item Data.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    public string itemName;
    [TextArea] public string description;

    [Header("Visual")]
    public Sprite icon;
}
