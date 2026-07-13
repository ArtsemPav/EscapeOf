using System;
using UnityEngine;

/// <summary>
/// Place on any world object to make it pickable.
/// When the player interacts — item is added to inventory and object is destroyed.
/// Implements ISaveable: persists collected state so the item is not respawned after load.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour, IInteractable, ISaveable
{
    [SerializeField] private ItemData itemData;

    [Header("Inspect-Only")]
    [Tooltip("Если включено — предмет можно только осмотреть в 3D-превью. Он не попадает в инвентарь и не удаляется из сцены.")]
    [SerializeField] private bool inspectOnly;

    [Tooltip("Промпт взаимодействия для осматриваемых предметов.")]
    [SerializeField] private string inspectPrefix = "Осмотреть";

    [Header("Save")]
    [Tooltip("Stable unique ID used by the save system. Right-click this component → Generate Save ID to auto-fill.")]
    [SerializeField] private string _saveId;

    private bool _collected;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>Serializes whether this item has been collected.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new PickableSaveData { collected = _collected });

    /// <summary>If the item was already collected in a previous session, destroys itself on load.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<PickableSaveData>(json);
        Debug.Log($"[PickableItem] LoadSaveData '{_saveId}': collected={data.collected}");
        if (data.collected)
        {
            // Set _collected before Destroy so OnDestroy keeps this object registered.
            // Any subsequent BuildSnapshot() will still find it and write collected=true.
            _collected = true;
            Destroy(gameObject);
        }
    }

    [Serializable]
    private struct PickableSaveData
    {
        public bool collected;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (!inspectOnly && !string.IsNullOrEmpty(_saveId))
            SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        // Collected items stay registered: SaveManager may still call GetSaveData()
        // during the debounce window after this object is destroyed.
        // The next scene load will overwrite the registry entry with a fresh instance.
        if (!inspectOnly && !string.IsNullOrEmpty(_saveId) && !_collected)
            SaveManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// Records that this item has been collected and queues a save checkpoint.
    /// Must be called before Destroy(gameObject) in all pickup paths.
    /// </summary>
    public void NotifyPickedUp()
    {
        _collected = true;
        SaveManager.Instance?.Save();
    }

    /// <summary>Opens inspection view, or picks up directly if no inspectionPrefab is set.
    /// When inspectOnly is true, opens a read-only 3D preview without adding to inventory or destroying the object.</summary>
    public void Interact()
    {
        if (itemData == null)
        {
            Debug.LogWarning($"PickableItem on {gameObject.name} has no ItemData assigned.", this);
            return;
        }

        if (inspectOnly)
        {
            ItemInspector.Instance?.BeginWorldPreview(itemData, gameObject);
            return;
        }

        if (ItemInspector.Instance != null)
        {
            ItemInspector.Instance.BeginInspection(itemData, gameObject);
        }
        else
        {
            if (!InventorySystem.Instance.AddItem(itemData)) return;
            NotifyPickedUp();
            Destroy(gameObject);
        }
    }

    /// <summary>Returns the interaction prompt shown to the player.</summary>
    public string GetInteractText()
    {
        if (inspectOnly)
        {
            return itemData != null ? $"{inspectPrefix} {itemData.itemName}" : inspectPrefix;
        }

        string prefix = UIManager.Instance?.Config?.pickUpPrefix ?? "Взять";
        return itemData != null ? $"{prefix} {itemData.itemName}" : prefix;
    }

    public bool IsPickable() => true;
    public bool UseLMBClick => true;

    /// <summary>Generates a stable GUID for the save system. Run once per object in the Inspector context menu.</summary>
    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
