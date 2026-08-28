using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Slot used by the debug Save / Load / Delete buttons in the pause menu.</summary>
    public const int DebugSlot = 999;

    // When set via RequestLoadFromSlot(), the next SaveManager.Start() loads from
    // this slot instead of defaultSlot. Consumed on first read — survives scene reload
    // because it's static, even though the SaveManager instance is destroyed and recreated.
    private static int _pendingLoadSlot = -1;

    /// <summary>Tells the next SaveManager instance to load from a specific slot on Start().</summary>
    public static void RequestLoadFromSlot(int slot) => _pendingLoadSlot = slot;

    private readonly Dictionary<string, ISaveable> _saveables = new();

    private float      _autoSaveTimer;
    private bool       _pendingSave;
    private float      _pendingSaveTimer;
    // Snapshot collected the moment Save() is requested — before any Destroy() runs.
    private GameSaveData _pendingSnapshot;

    // Set to true after the initial Load() in Start() completes.
    // Used to distinguish scene-load registration from runtime clones.
    private bool _initialLoadComplete;

    // Background write infrastructure — file I/O offloaded to avoid main-thread stalls.
    private readonly Queue<Action> _mainThreadCallbacks = new();
    private int _isWriting; // 0 = idle, 1 = write in progress (Interlocked)

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
        int slot = _pendingLoadSlot >= 0 ? _pendingLoadSlot : defaultSlot;
        _pendingLoadSlot = -1;
        Load(slot);
        _initialLoadComplete = true;
    }

    private void Update()
    {
        // Drain background-thread callbacks (e.g. OnSaved event after async write).
        while (_mainThreadCallbacks.Count > 0)
            _mainThreadCallbacks.Dequeue()?.Invoke();

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
        // Flush any pending debounced save synchronously so no data is lost on exit.
        if (_pendingSave)
            FlushPendingSave(synchronous: true);
    }

    private void FlushPendingSave(bool synchronous = false)
    {
        _pendingSave   = false;
        _autoSaveTimer = 0f;
        var snapshot     = _pendingSnapshot ?? BuildSnapshot();
        _pendingSnapshot = null;
        WriteToFile(defaultSlot, snapshot, synchronous);
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

            // After the initial scene load, a destroyed entry was intentionally left registered
            // (e.g. a collected PickableItem keeping its "collected=true" snapshot data alive).
            // Do NOT let runtime clones (puzzle coin visuals, inspection previews) overwrite it.
            if (existingUnityObj == null && _initialLoadComplete)
            {
                Debug.Log($"[SaveManager] Register: '{saveable.SaveId}' retained destroyed entry — blocking runtime clone from '{newUnityObj?.name}'.");
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

    /// <summary>
    /// Snapshots all ISaveable data and writes it to disk synchronously (main thread).
    /// Use for debug UI buttons where the write must complete before the next action.
    /// Pass a slot to write to a specific slot; omit to use the default slot.
    /// </summary>
    public void SaveImmediate(int slot = -1)
        => WriteToFile(slot >= 0 ? slot : defaultSlot, BuildSnapshot(), synchronous: true);

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

    /// <summary>
    /// Serializes data on the main thread, then offloads backup rotation and file
    /// writing to a background thread. OnSaved is queued back to the main thread
    /// after the write completes. Skips if a previous write is still in flight.
    /// Pass synchronous: true for the OnApplicationQuit path.
    /// </summary>
    private void WriteToFile(int slot, GameSaveData data, bool synchronous = false)
    {
        string json      = JsonUtility.ToJson(data, prettyPrint: false);
        string savePath  = GetSavePath(slot);
        string dir       = Path.GetDirectoryName(savePath)!;

        // Pre-compute backup paths on the main thread (Application.persistentDataPath
        // must not be read on a background thread).
        var backupPaths = new string[backupCount];
        for (int i = 0; i < backupCount; i++)
            backupPaths[i] = GetBackupPath(slot, i + 1);

        if (synchronous)
        {
            PerformWrite(slot, savePath, dir, backupPaths, json);
            OnSaved?.Invoke();
            return;
        }

        // Skip if a previous background write hasn't finished yet.
        if (Interlocked.CompareExchange(ref _isWriting, 1, 0) != 0)
        {
            Debug.LogWarning($"[SaveManager] Write skipped (previous write in progress) → slot {slot}");
            return;
        }

        int slotCopy = slot;
        Task.Run(() =>
        {
            try { PerformWrite(slotCopy, savePath, dir, backupPaths, json); }
            catch (Exception e) { Debug.LogError($"[SaveManager] Write failed: {e.Message}"); }
            finally
            {
                Interlocked.Exchange(ref _isWriting, 0);
                _mainThreadCallbacks.Enqueue(() => OnSaved?.Invoke());
            }
        });
    }

    /// <summary>Performs the actual disk write — safe to call from any thread.</summary>
    private static void PerformWrite(int slot, string savePath, string dir, string[] backupPaths, string json)
    {
        Directory.CreateDirectory(dir);
        RotateBackups(savePath, backupPaths);
        File.WriteAllText(savePath, json);
        Debug.Log($"[SaveManager] Saved slot {slot} → {savePath}");
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

    /// <summary>
    /// Rotates backup files. Called from a background thread — all paths must be
    /// pre-computed on the main thread.
    /// </summary>
    private static void RotateBackups(string savePath, string[] backupPaths)
    {
        if (backupPaths.Length == 0) return;
        TryDelete(backupPaths[backupPaths.Length - 1]);
        for (int i = backupPaths.Length - 2; i >= 0; i--)
            TryMove(backupPaths[i], backupPaths[i + 1]);
        TryMove(savePath, backupPaths[0]);
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
