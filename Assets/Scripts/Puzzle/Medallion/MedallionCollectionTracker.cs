using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the order in which the player collects medallions.
/// Registers with SaveManager so the collection order persists across sessions.
/// MedallionBoxUI queries CollectionOrder to display slots in the correct sequence.
///
/// <para><b>Execution order: -5.</b> Runs after SaveManager (-10) and MedallionBoxInteraction (-7).
/// This guarantees that when <c>Start</c> calls <see cref="OnInventoryChanged"/> for the startup
/// sync, all holes have already been restored via <c>ApplyPendingLoad</c>. The <c>_isReady</c>
/// flag prevents that startup sync from triggering a premature <c>Save()</c> that would
/// overwrite the correct hole state with an empty snapshot.</para>
/// </summary>
[DefaultExecutionOrder(-5)] // After SaveManager (-10), before default scripts (0)
public class MedallionCollectionTracker : MonoBehaviour, ISaveable
{
    public static MedallionCollectionTracker Instance { get; private set; }

    [Header("Medallions")]
    [Tooltip("All medallion ItemData assets to track. Order here does not matter.")]
    [SerializeField] private ItemData[] _medallions;

    /// <summary>Medallions in the order they were picked up by the player.</summary>
    public IReadOnlyList<ItemData> CollectionOrder => _collectionOrder;

    private readonly List<ItemData> _collectionOrder = new();

    // Guards against a premature Save() during the startup sync in Start().
    // MedallionBoxInteraction.ApplyPendingLoad() runs at order -7 (before this component at -5),
    // but setting _isReady only after the sync call gives an additional safety net: even if
    // execution orders are ever changed, no snapshot is taken until all ISaveables are ready.
    private bool _isReady;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "medallion_collection";

    /// <summary>Serializes the current collection order as an array of item IDs.</summary>
    public string GetSaveData()
    {
        var ids = new string[_collectionOrder.Count];
        for (int i = 0; i < _collectionOrder.Count; i++)
            ids[i] = _collectionOrder[i]?.ItemId;
        return JsonUtility.ToJson(new CollectionSaveData { ids = ids });
    }

    /// <summary>Restores the collection order from saved item IDs.</summary>
    public void LoadSaveData(string json)
    {
        _collectionOrder.Clear();
        var data = JsonUtility.FromJson<CollectionSaveData>(json);
        if (data.ids == null) return;

        foreach (var id in data.ids)
        {
            var item = FindById(id);
            if (item != null)
                _collectionOrder.Add(item);
        }
    }

    [Serializable]
    private struct CollectionSaveData
    {
        public string[] ids;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Subscribe after InventorySystem has loaded its own save data in Start()
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;

        // Sync in case items were already in inventory at load time.
        // _isReady stays false here so no Save() fires during this startup catch-up —
        // all ISaveables must finish ApplyPendingLoad() before the first snapshot is taken.
        OnInventoryChanged();
        _isReady = true;
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;

        SaveManager.Instance?.Unregister(this);

        if (Instance == this)
            Instance = null;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void OnInventoryChanged()
    {
        if (_medallions == null) return;

        bool addedNew = false;

        foreach (var medallion in _medallions)
        {
            if (medallion == null) continue;
            if (_collectionOrder.Contains(medallion)) continue;
            if (!InventorySystem.Instance.HasItem(medallion)) continue;

            _collectionOrder.Add(medallion);
            addedNew = true;
        }

        if (addedNew)
        {
            if (_isReady)
                SaveManager.Instance?.Save();
        }
    }

    private ItemData FindById(string id)
    {
        if (string.IsNullOrEmpty(id) || _medallions == null) return null;
        foreach (var m in _medallions)
            if (m != null && m.ItemId == id) return m;
        return null;
    }
}
