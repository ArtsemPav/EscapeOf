using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Local playtest telemetry: appends gameplay events to a JSONL file in
/// persistentDataPath. No network — the file is only meant to be collected
/// from playtesters (manually or with the feedback form).
///
/// All file I/O runs on a dedicated background thread: the game thread only
/// formats the event line and enqueues it, so a logging write can never stall
/// a frame (no HDD seek, no antivirus scan on the main thread).
///
/// What is logged automatically:
/// - session_start / scene_loaded            — when and where the session begins;
/// - room_enter                              — the room the player is physically in (any change);
/// - room_unlocked                           — room progression index (GameManager);
/// - inventory_changed                       — full inventory contents after every change;
/// - puzzle_solved                           — explicit events from puzzle scripts;
/// - progress_snapshot                       — after every save: SaveId + short state
///   of every registered ISaveable (shows which puzzles are solved).
///
/// File: persistentDataPath/progress_log.jsonl (rotated to .old past the size cap).
/// Cleared by Tools → Escape → Reset Save Progress in the Editor.
/// Attach to the GameManager GameObject in the Game scene (it is DontDestroyOnLoad).
/// </summary>
public class ProgressLogger : MonoBehaviour
{
    public static ProgressLogger Instance { get; private set; }

    [Header("Limits")]
    [Tooltip("Rotate the log to .old past this size (bytes). 0 disables rotation.")]
    [SerializeField] private long _maxLogBytes = 2_000_000;

    [Tooltip("Seconds between progress snapshots even if no save happened.")]
    [SerializeField, Min(0f)] private float _snapshotInterval = 300f;

    private const string LogFileName = "progress_log.jsonl";
    private const string OldFileName = "progress_log.old.jsonl";

    private readonly BlockingCollection<string> _writeQueue = new(new ConcurrentQueue<string>(), 512);
    private Thread _writerThread;
    private readonly StringBuilder _lineBuffer = new(256);

    private float _snapshotTimer;
    private GameManager _trackedGameManager;
    private RoomVisibilityManager _trackedRoomManager;
    private InventorySystem _trackedInventory;
    private string _lastRoomName;
    private string _lastInventoryState;
    private volatile bool _shuttingDown;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        StartWriterThread();
    }

    private void Start()
    {
        Log("session_start", Application.version);
        SceneManager.sceneLoaded += OnSceneLoaded;

        SaveManager saveManager = SaveManager.Instance;
        if (saveManager != null)
            saveManager.OnSaved += WriteProgressSnapshot;

        TrackInventory();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnSaved -= WriteProgressSnapshot;
        if (_trackedInventory != null)
            _trackedInventory.OnInventoryStateChanged -= OnInventoryStateChanged;
        UnsubscribeTracked();

        StopWriterThread();
        Instance = null;
    }

    private void OnApplicationQuit()
    {
        // OnApplicationQuit can precede OnDestroy; shutting down twice is safe.
        StopWriterThread();
    }

    // ── Background Writer Thread ──────────────────────────────────────────────

    /// <summary>Starts the dedicated writer thread. Takes over when events are enqueued.</summary>
    private void StartWriterThread()
    {
        _shuttingDown = false;
        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true, // never blocks app exit
            Name = "ProgressLogger.Writer"
        };
        _writerThread.Start();
    }

    /// <summary>Drains remaining events and stops the writer thread (bounded wait).</summary>
    private void StopWriterThread()
    {
        if (_writerThread == null || !_writerThread.IsAlive)
        {
            _writerThread = null;
            return;
        }

        _shuttingDown = true;
        // Give the writer a moment to flush what is already in the queue, then
        // abandon it — never block the game thread on disk at shutdown.
        if (!_writerThread.Join(100))
        {
            // IsBackground = true — the runtime kills it with the process;
            // worst case the very last events of the session are lost.
            Debug.LogWarning("[ProgressLogger] Writer thread did not flush in time — some tail events may be lost.");
        }
        _writerThread = null;
    }

    private void WriterLoop()
    {
        foreach (string line in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, LogFileName);
                RotateIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProgressLogger] Background write failed: {e.Message}");
            }

            if (_shuttingDown) break;
        }
    }

    // ── Public API — call from game code for explicit events ──────────────────

    /// <summary>Records an explicit event. Use snake_case event names. Never blocks the caller.</summary>
    public static void Log(string eventName, string detail = "")
    {
        if (Instance != null)
            Instance.Write(eventName, detail);
    }

    // ── Event Sources ─────────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log("scene_loaded", scene.name);
        WriteProgressSnapshot();
    }

    private void TrackGameManager()
    {
        if (_trackedGameManager != null) return;
        var gm = GameManager.Instance;
        if (gm == null) return;

        _trackedGameManager = gm;
        gm.OnRoomChanged += OnRoomChanged;
        gm.OnGameCompleted += OnGameCompleted;
    }

    private void UnsubscribeTracked()
    {
        if (_trackedGameManager != null)
        {
            _trackedGameManager.OnRoomChanged -= OnRoomChanged;
            _trackedGameManager.OnGameCompleted -= OnGameCompleted;
            _trackedGameManager = null;
        }
        if (_trackedRoomManager != null)
        {
            _trackedRoomManager.CurrentRoomChanged -= OnCurrentRoomChanged;
            _trackedRoomManager = null;
        }
    }

    private void OnRoomChanged(int roomIndex)
    {
        string roomName = _trackedGameManager != null &&
                          _trackedGameManager.Rooms != null &&
                          roomIndex < _trackedGameManager.Rooms.Count &&
                          _trackedGameManager.Rooms[roomIndex] != null
            ? _trackedGameManager.Rooms[roomIndex].gameObject.name
            : $"index {roomIndex}";
        Log("room_unlocked", $"{roomIndex}:{roomName}");
    }

    private void OnGameCompleted() => Log("game_completed", "all rooms exited");

    private void TrackRoomManager()
    {
        if (_trackedRoomManager != null) return;
        var rvm = RoomVisibilityManager.Instance;
        if (rvm == null) return;

        _trackedRoomManager = rvm;
        rvm.CurrentRoomChanged += OnCurrentRoomChanged;
    }

    private void OnCurrentRoomChanged(string roomName)
    {
        if (string.Equals(roomName, _lastRoomName, StringComparison.Ordinal)) return;
        _lastRoomName = roomName;
        Log("room_enter", roomName ?? "unknown");
    }

    private void TrackInventory()
    {
        if (_trackedInventory != null) return;
        var inv = InventorySystem.Instance;
        if (inv == null) return;

        _trackedInventory = inv;
        inv.OnInventoryStateChanged += OnInventoryStateChanged;
    }

    private void OnInventoryStateChanged(string snapshot)
    {
        if (string.Equals(snapshot, _lastInventoryState, StringComparison.Ordinal)) return;

        _lastInventoryState = snapshot;
        Log("inventory_changed", snapshot);
    }

    // ── Progress Snapshot ─────────────────────────────────────────────────────

    /// <summary>Writes SaveId + short state of every registered ISaveable.</summary>
    private void WriteProgressSnapshot()
    {
        _snapshotTimer = 0f;

        var saveManager = SaveManager.Instance;
        if (saveManager == null) return;

        var entries = saveManager.GetSaveableData();
        if (entries == null || entries.Count == 0) return;

        _lineBuffer.Clear();
        _lineBuffer.Append('{').Append("\"t\":\"").Append(Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append('"');
        _lineBuffer.Append(",\"e\":\"progress_snapshot\",\"d\":{");

        bool first = true;
        foreach (var (id, json) in entries)
        {
            if (!first) _lineBuffer.Append(',');
            first = false;
            _lineBuffer.Append('"').Append(Escape(id)).Append("\":\"").Append(Escape(Truncate(json))).Append('"');
        }
        _lineBuffer.Append("}}");

        Enqueue(_lineBuffer.ToString());
    }

    private static string Truncate(string text)
    {
        const int MaxLength = 200;
        return string.IsNullOrEmpty(text) || text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
    }

    private static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "");

    // ── Line Formatting & Enqueue (game thread) ───────────────────────────────

    private void Write(string eventName, string detail)
    {
        _lineBuffer.Clear();
        _lineBuffer.Append('{').Append("\"t\":\"").Append(Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append('"');
        _lineBuffer.Append(",\"e\":\"").Append(Escape(eventName)).Append('"');
        if (!string.IsNullOrEmpty(detail))
            _lineBuffer.Append(",\"d\":\"").Append(Escape(detail)).Append('"');
        _lineBuffer.Append('}');

        Enqueue(_lineBuffer.ToString());
    }

    /// <summary>Hands the line to the writer thread. Never touches the disk here.</summary>
    private void Enqueue(string line)
    {
        try
        {
            // TryAdd: if the queue is full (writer stalled for seconds), drop
            // the event instead of freezing the game — telemetry must never cost
            // a frame. 512 events is far beyond normal traffic.
            if (!_writeQueue.TryAdd(line) && !_shuttingDown)
                Debug.LogWarning("[ProgressLogger] Write queue full — event dropped.");
        }
        catch (Exception) { /* completing during shutdown — drop silently */ }
    }

    private void Update()
    {
        // Late subscription: GameManager, RoomVisibilityManager and InventorySystem
        // live in the Game scene and may be created after this component initializes.
        TrackGameManager();
        TrackRoomManager();
        TrackInventory();

        if (_snapshotInterval > 0f)
        {
            _snapshotTimer += Time.unscaledDeltaTime;
            if (_snapshotTimer >= _snapshotInterval)
            {
                _snapshotTimer = 0f;
                WriteProgressSnapshot();
            }
        }
    }

    private void RotateIfNeeded(string path)
    {
        if (_maxLogBytes <= 0) return;

        var file = new FileInfo(path);
        if (!file.Exists || file.Length < _maxLogBytes) return;

        string oldPath = Path.Combine(Application.persistentDataPath, OldFileName);
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(path, oldPath);
    }
}
