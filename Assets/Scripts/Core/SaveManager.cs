using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Manages all game persistence: auto-save on a configurable interval,
/// debounced manual saves triggered by game events, multiple slots with rolling backup rotation.
///
/// Execution order -10 ensures Awake() runs before all default-order scripts,
/// so SaveManager.Instance is available when ISaveables register in their Awake().
/// Start() also runs first, distributing loaded data before other scripts' Start() apply state.
/// </summary>
[DefaultExecutionOrder(-10)]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Auto-Save")]
    [Tooltip("Seconds between automatic saves. Set to 0 to disable.")]
    [SerializeField] private float autoSaveInterval = 120f;

    [Header("Save Debounce")]
    [Tooltip("Minimum seconds between two consecutive event-driven writes. Prevents IO spam when multiple events fire at once.")]
    [SerializeField] private float saveDebounceDelay = 2f;

    [Header("Slots & Backups")]
    [SerializeField] private int defaultSlot = 0;
    [Tooltip("Number of rolling backup files kept per slot.")]
    [SerializeField] private int backupCount = 2;

    private const int CurrentVersion = 2;
    private const string SaveFolder = "saves";
    private const string FilePrefix = "slot_";
    private const string FileExtension = ".json";

    private readonly Dictionary<string, ISaveable> _saveables = new();

    private float      _autoSaveTimer;
    private bool       _pendingSave;
    private float      _pendingSaveTimer;
    // Snapshot collected the moment Save() is requested — before any Destroy() runs.
    private GameSaveData _pendingSnapshot;

    /// <summary>Fired after every successful write. SaveIndicatorUI subscribes to show the on-screen label.</summary>
    public event Action OnSaved;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Load(defaultSlot);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Debounced event-driven save
        if (_pendingSave)
        {
            _pendingSaveTimer -= dt;
            if (_pendingSaveTimer <= 0f)
            {
                FlushPendingSave();
            }
        }

        // Periodic auto-save — always builds a fresh snapshot at write time
        if (autoSaveInterval > 0f)
        {
            _autoSaveTimer += dt;
            if (_autoSaveTimer >= autoSaveInterval)
            {
                _autoSaveTimer   = 0f;
                _pendingSave     = false;
                _pendingSnapshot = null;
                WriteToFile(defaultSlot, BuildSnapshot());
            }
        }
    }

    private void OnApplicationQuit()
    {
        // Flush any pending debounced save so no data is lost on exit.
        if (_pendingSave)
            FlushPendingSave();
    }

    private void FlushPendingSave()
    {
        _pendingSave   = false;
        _autoSaveTimer = 0f;
        var snapshot     = _pendingSnapshot ?? BuildSnapshot();
        _pendingSnapshot = null;
        WriteToFile(defaultSlot, snapshot);
    }

    // ── ISaveable Registration ────────────────────────────────────────────────

    /// <summary>Registers an ISaveable to participate in save/load. Call in Awake().</summary>
    public void Register(ISaveable saveable)
    {
        if (string.IsNullOrEmpty(saveable.SaveId))
        {
            Debug.LogWarning("SaveManager: ISaveable registered with an empty SaveId — skipping.", this);
            return;
        }

        // If a different live Unity object is already registered under this ID, do not overwrite it.
        // This prevents inspection-preview clones (same prefab, same SaveId) from displacing
        // the original world-object registration in the dictionary.
        // A destroyed object compares equal to null via Unity's overridden == operator,
        // so a stale reference from a previous scene load is always replaced.
        if (_saveables.TryGetValue(saveable.SaveId, out var existing))
        {
            var existingUnityObj = existing as UnityEngine.Object;
            var newUnityObj      = saveable as UnityEngine.Object;
            if (existingUnityObj != null && existingUnityObj != newUnityObj)
            {
                Debug.LogWarning($"[SaveManager] Register: '{saveable.SaveId}' already held by '{existingUnityObj.name}' — ignoring duplicate registration from '{newUnityObj?.name}'.", this);
                return;
            }
        }

        _saveables[saveable.SaveId] = saveable;
    }

    /// <summary>Unregisters an ISaveable. Call in OnDestroy().</summary>
    public void Unregister(ISaveable saveable)
    {
        if (string.IsNullOrEmpty(saveable.SaveId)) return;

        // Only remove if the entry in the dictionary is THIS saveable instance.
        // If a duplicate (e.g. an inspection-preview clone) was blocked from registering,
        // its OnDestroy must not evict the legitimate world-object registration.
        if (_saveables.TryGetValue(saveable.SaveId, out var registered) &&
            registered as UnityEngine.Object == saveable as UnityEngine.Object)
        {
            _saveables.Remove(saveable.SaveId);
        }
    }

    // ── Save / Load / Delete ──────────────────────────────────────────────────

    /// <summary>
    /// Snapshots all ISaveable data immediately (while objects are still alive),
    /// then writes the file after saveDebounceDelay. Multiple calls within the
    /// delay window merge into the existing snapshot and reset the timer,
    /// collapsing into one write without losing any intermediate state.
    /// </summary>
    public void Save()
    {
        if (_pendingSnapshot == null)
        {
            // First call in this debounce window — take a fresh snapshot.
            _pendingSnapshot = BuildSnapshot();
        }
        else
        {
            // Subsequent call — merge fresh data INTO the existing snapshot.
            // This preserves entries for objects that may have been destroyed since
            // the first snapshot (e.g. a second picked-up item arriving 0.5s later).
            MergeIntoSnapshot(_pendingSnapshot);
        }
        _pendingSave      = true;
        _pendingSaveTimer = saveDebounceDelay;
    }

    /// <summary>Builds a fresh snapshot and writes it immediately to the specified slot.</summary>
    public void Save(int slot) => WriteToFile(slot, BuildSnapshot());

    /// <summary>Collects GetSaveData() from every registered ISaveable into a new GameSaveData object.</summary>
    private GameSaveData BuildSnapshot()
    {
        var data = new GameSaveData
        {
            version   = CurrentVersion,
            timestamp = DateTime.UtcNow.ToString("o"),
        };
        foreach (var kvp in _saveables)
        {
            try
            {
                string saveData = kvp.Value.GetSaveData();
                data.entities.Add(new EntityRecord { id = kvp.Key, data = saveData });
            }
            catch (Exception e) { Debug.LogError($"[SaveManager] GetSaveData() failed for '{kvp.Key}': {e.Message}"); }
        }
        return data;
    }

    /// <summary>
    /// Updates or adds entries in an existing snapshot with the latest data from registered ISaveables.
    /// Entries already present in the snapshot but no longer registered are preserved as-is.
    /// </summary>
    private void MergeIntoSnapshot(GameSaveData snapshot)
    {
        snapshot.timestamp = DateTime.UtcNow.ToString("o");
        foreach (var kvp in _saveables)
        {
            try
            {
                string fresh = kvp.Value.GetSaveData();
                bool found   = false;
                for (int i = 0; i < snapshot.entities.Count; i++)
                {
                    if (snapshot.entities[i].id != kvp.Key) continue;
                    snapshot.entities[i] = new EntityRecord { id = kvp.Key, data = fresh };
                    found = true;
                    break;
                }
                if (!found)
                    snapshot.entities.Add(new EntityRecord { id = kvp.Key, data = fresh });
            }
            catch (Exception e) { Debug.LogError($"[SaveManager] GetSaveData() failed for '{kvp.Key}': {e.Message}"); }
        }
    }

    /// <summary>Rotates backups and writes a GameSaveData object to disk.</summary>
    private void WriteToFile(int slot, GameSaveData data)
    {
        string path = GetSavePath(slot);
        RotateBackups(slot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: false));
            Debug.Log($"[SaveManager] Saved slot {slot} → {path}");
            OnSaved?.Invoke();
        }
        catch (Exception e) { Debug.LogError($"[SaveManager] Write failed: {e.Message}"); }
    }

    /// <summary>
    /// Loads the default slot. Falls back to backup files if the main file is missing or corrupted.
    /// Returns true if any save data was found and applied.
    /// </summary>
    public bool Load() => Load(defaultSlot);

    /// <summary>Loads the specified slot with automatic backup fallback.</summary>
    public bool Load(int slot)
    {
        if (!TryReadSaveFile(slot, out string json))
            return false;

        GameSaveData data;
        try { data = JsonUtility.FromJson<GameSaveData>(json); }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Parse failed: {e.Message}");
            return false;
        }

        if (data == null) return false;

        if (data.version != CurrentVersion)
            Debug.LogWarning($"[SaveManager] Save version mismatch: expected {CurrentVersion}, got {data.version}. Some data may not restore correctly.");

        if (data.entities != null)
        {
            foreach (var record in data.entities)
            {
                if (!_saveables.TryGetValue(record.id, out var saveable)) continue;
                try { saveable.LoadSaveData(record.data); }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] LoadSaveData() failed for '{record.id}': {e.Message}");
                }
            }
        }

        Debug.Log($"[SaveManager] Loaded slot {slot} (timestamp: {data.timestamp})");
        return true;
    }

    /// <summary>Deletes the save file and all backups for the given slot.</summary>
    public void DeleteSave(int slot = 0)
    {
        TryDelete(GetSavePath(slot));
        for (int i = 1; i <= backupCount; i++)
            TryDelete(GetBackupPath(slot, i));

        Debug.Log($"[SaveManager] Deleted save slot {slot}.");
    }

    /// <summary>Clears the ISaveable registry without deleting files. Call before scene reload on reset.</summary>
    public void ClearRegistry()
    {
        _saveables.Clear();
    }

    /// <summary>Returns true if a save file exists for the given slot.</summary>
    public bool HasSave(int slot = 0) => File.Exists(GetSavePath(slot));

    // ── File helpers ──────────────────────────────────────────────────────────

    /// <summary>Tries the main file then each backup in order until one is readable.</summary>
    private bool TryReadSaveFile(int slot, out string json)
    {
        if (TryReadFile(GetSavePath(slot), out json)) return true;

        for (int i = 1; i <= backupCount; i++)
        {
            if (TryReadFile(GetBackupPath(slot, i), out json))
            {
                Debug.LogWarning($"[SaveManager] Main save unreadable. Loaded backup {i} for slot {slot}.");
                return true;
            }
        }

        json = null;
        return false;
    }

    private static bool TryReadFile(string path, out string content)
    {
        if (!File.Exists(path)) { content = null; return false; }
        try { content = File.ReadAllText(path); return !string.IsNullOrWhiteSpace(content); }
        catch { content = null; return false; }
    }

    private void RotateBackups(int slot)
    {
        if (backupCount <= 0) return;
        TryDelete(GetBackupPath(slot, backupCount));
        for (int i = backupCount - 1; i >= 1; i--)
            TryMove(GetBackupPath(slot, i), GetBackupPath(slot, i + 1));
        TryMove(GetSavePath(slot), GetBackupPath(slot, 1));
    }

    private string SaveDir => Path.Combine(Application.persistentDataPath, SaveFolder);
    private string GetSavePath(int slot)        => Path.Combine(SaveDir, $"{FilePrefix}{slot}{FileExtension}");
    private string GetBackupPath(int slot, int n) => Path.Combine(SaveDir, $"{FilePrefix}{slot}_bk{n}{FileExtension}");

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception e) { Debug.LogWarning($"[SaveManager] Delete failed ({path}): {e.Message}"); }
    }

    private static void TryMove(string src, string dst)
    {
        if (!File.Exists(src)) return;
        try
        {
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }
        catch (Exception e) { Debug.LogWarning($"[SaveManager] Move failed ({src}→{dst}): {e.Message}"); }
    }
}
