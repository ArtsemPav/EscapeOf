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

    [Header("Save")]
    [Tooltip("Stable identifier used by the save system. Auto-uses the asset file name if left empty. Never rename the asset file after saving.")]
    [SerializeField] private string _itemId;

    /// <summary>Stable identifier for save/load. Defaults to the ScriptableObject asset name.</summary>
    public string ItemId => string.IsNullOrEmpty(_itemId) ? name : _itemId;

    [Header("Visual")]
    public Sprite icon;

    [Header("Inspection")]
    [Tooltip("3D prefab shown in the inspection view. If null, item is picked up directly.")]
    public GameObject inspectionPrefab;

    [Header("Usage")]
    [Tooltip("Если включено — предмет удаляется из инвентаря после использования (например, ключ открыл дверь).")]
    public bool consumeOnUse = true;
}
