using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Local playtest telemetry: appends gameplay events to a plain-text file.
/// No network — the file is only meant to be collected from playtesters.
///
/// Format — one event per line, "HH:mm:ss | event | details":
///   14:03:41 | room_enter | Room 3
///   14:04:12 | puzzle_failed | code_lock: код 3719
/// Readable in Notepad, no JSON parsing needed.
///
/// All file I/O runs on a dedicated background thread: the game thread only
/// formats the event line and enqueues it, so a logging write can never stall
/// a frame (no HDD seek, no antivirus scan on the main thread).
///
/// What is logged automatically:
/// - session_start / scene_loaded — when and where the session begins;
/// - room_enter                   — the room the player is physically in (RoomVisibilityManager);
/// - inventory_changed            — inventory contents after every change;
/// - puzzle_solved / puzzle_failed / puzzle_exit_unsolved — puzzle outcomes.
///
/// In a build the file lives next to the game exe; in the Editor — in
/// persistentDataPath. Cleared by Tools → Escape → Reset Save Progress.
/// Attach to the GameManager GameObject in the Game scene (it is DontDestroyOnLoad).
/// </summary>
public class ProgressLogger : MonoBehaviour
{
    public static ProgressLogger Instance { get; private set; }

    [Header("Limits")]
    [Tooltip("Rotate the log to .old past this size (bytes). 0 disables rotation.")]
    [SerializeField] private long _maxLogBytes = 2_000_000;

    private const string LogFileName = "progress_log.txt";
    private const string OldFileName = "progress_log.old.txt";

    private readonly BlockingCollection<string> _writeQueue = new(new ConcurrentQueue<string>(), 512);
    private Thread _writerThread;
    private readonly StringBuilder _lineBuffer = new(256);

    private RoomVisibilityManager _trackedRoomManager;
    private InventorySystem _trackedInventory;
    private string _lastRoomName;
    private string _lastInventoryState;
    private volatile bool _shuttingDown;
    // Application.persistentDataPath is main-thread only — resolve it once here
    // so the background writer thread never touches the Unity API.
    private string _logDirectory;
    private string _logFilePath;
    private string _oldLogFilePath;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveLogPaths();
        StartWriterThread();
    }

    private void Start()
    {
        Log("session_start", Application.version);
        SceneManager.sceneLoaded += OnSceneLoaded;

        SaveManager saveManager = SaveManager.Instance;
        if (saveManager != null)
            saveManager.OnSaved += OnGameSaved;

        TrackInventory();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnSaved -= OnGameSaved;
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

    /// <summary>
    /// Resolves where the log lives. In a build the log is written next to the
    /// game executable (a tester finds it in one step); in the Editor — in
    /// persistentDataPath so the project folder is not littered. If the game
    /// folder is not writable (e.g. installed under Program Files), falls back
    /// to persistentDataPath.
    /// </summary>
    private void ResolveLogPaths()
    {
        string directory = null;

        if (!Application.isEditor)
        {
            // The exe sits next to <game>_Data/ — the log goes right beside it.
            string gameFolder = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(gameFolder) && CanWrite(gameFolder))
                directory = gameFolder;
        }

        if (directory == null)
            directory = Application.persistentDataPath;

        _logDirectory = directory;
        _logFilePath = Path.Combine(directory, LogFileName);
        _oldLogFilePath = Path.Combine(directory, OldFileName);

        Debug.Log($"[ProgressLogger] Log file: {_logFilePath}");
    }

    /// <summary>Checks the folder accepts creating a file (admin-installed games).</summary>
    private static bool CanWrite(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, LogFileName + ".probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void WriterLoop()
    {
        string path = _logFilePath;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[ProgressLogger] Log path was not resolved on the main thread — logging disabled.");
            return;
        }

        foreach (string line in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
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
    }

    private void UnsubscribeTracked()
    {
        if (_trackedRoomManager != null)
        {
            _trackedRoomManager.CurrentRoomChanged -= OnCurrentRoomChanged;
            _trackedRoomManager = null;
        }
    }

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

    // ── Save Logging ──────────────────────────────────────────────────────────

    /// <summary>Notes that the player saved — a natural checkpoint in the timeline.</summary>
    private void OnGameSaved()
    {
        Log("game_saved", "сохранение");
    }

    private static string Escape(string text) => text
        .Replace("|", "/")
        .Replace("\n", " ")
        .Replace("\r", "");

    // ── Line Formatting & Enqueue (game thread) ───────────────────────────────

    private void Write(string eventName, string detail)
    {
        _lineBuffer.Clear();
        _lineBuffer.Append(DateTime.Now.ToString("HH:mm:ss"));
        _lineBuffer.Append(" | ").Append(eventName);
        if (!string.IsNullOrEmpty(detail))
            _lineBuffer.Append(" | ").Append(detail);

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
        // Late subscription: RoomVisibilityManager and InventorySystem
        // live in the Game scene and may be created after this component initializes.
        TrackRoomManager();
        TrackInventory();
    }

    private void RotateIfNeeded(string path)
    {
        if (_maxLogBytes <= 0) return;

        var file = new FileInfo(path);
        if (!file.Exists || file.Length < _maxLogBytes) return;

        string oldPath = _oldLogFilePath;
        if (string.IsNullOrEmpty(oldPath)) return;
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(path, oldPath);
    }
}
