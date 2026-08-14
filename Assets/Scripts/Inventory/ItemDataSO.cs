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

    [Header("Usage")]
    [Tooltip("Если включено — предмет удаляется из инвентаря после использования (например, ключ открыл дверь).")]
    public bool consumeOnUse = true;

    [Header("Dev Panel")]
    [Tooltip("If enabled, this item appears in the developer panel item list for quick testing.")]
    public bool showInDevPanel = false;

    [Header("Inspection")]
    [Tooltip("3D prefab shown in the inspection view. If null, item is picked up directly.")]
    public GameObject inspectionPrefab;

    [Tooltip("When enabled, overrides the global initial rotation used in all 3D previews for this item specifically.")]
    public bool useCustomPreviewRotation;

    [Tooltip("Euler angles for the initial rotation in the inspection / inventory preview. Active only when useCustomPreviewRotation is true.")]
    public Vector3 previewRotation = new Vector3(15f, -35f, 0f);

    [Header("Inspection Behaviour")]
    [Tooltip("Если включено — предмет не будет автоматически вращаться в превью осмотра.")]
    public bool disableIdleSpin;

    [Tooltip("Запретить ручное вращение по оси X (наклон вверх/вниз) во время превью и осмотра.")]
    public bool lockRotationX;

    [Tooltip("Запретить ручное вращение по оси Y (поворот влево/вправо) во время превью и осмотра.")]
    public bool lockRotationY;

    [Tooltip("Запретить ручное вращение по оси Z (наклон вбок) во время превью и осмотра.")]
    public bool lockRotationZ;

    [Tooltip("Если включено — предмет не будет привлекать внимание мерцанием (shimmer).")]
    public bool disableShimmer;

    [Tooltip("Множитель масштаба модели в превью осмотра. 1 = реальный размер, >1 = больше, <1 = меньше.")]
    public float previewScale = 1f;

    private static readonly int LiquidColorId = Shader.PropertyToID("_LiquidColor");

    /// <summary>
    /// Returns the liquid color for this item.
    /// First checks for a <see cref="ChemicalPuzzle.LiquidWobble"/> component on the inspectionPrefab
    /// (colba prefab variants store their color there as a serialized field override).
    /// Falls back to reading the <c>_LiquidColor</c> property from the first matching material.
    /// Returns <see cref="Color.white"/> if nothing is found.
    /// </summary>
    public Color GetLiquidColor()
    {
        if (inspectionPrefab == null) return Color.white;

        // Priority: LiquidWobble component field (colba prefab variants store colour here).
        var wobble = inspectionPrefab.GetComponentInChildren<ChemicalPuzzle.LiquidWobble>(includeInactive: true);
        if (wobble != null)
            return wobble.LiquidColor;

        // Fallback: read _LiquidColor from the first material that declares it.
        foreach (var rend in inspectionPrefab.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat != null && mat.HasProperty(LiquidColorId))
                    return mat.GetColor(LiquidColorId);
            }
        }

        return Color.white;
    }
}
