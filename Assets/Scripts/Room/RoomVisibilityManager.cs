using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Performance light/geometry culling driven by the player's physical location.
/// RoomTrigger volumes call SetCurrentRoom() when the player enters a room; only the
/// current room and its whitelisted Visible Rooms are rendered, everything else is
/// suppressed. Acts as a safety layer on top of occlusion culling.
/// </summary>
public class RoomVisibilityManager : MonoBehaviour
{
    public static RoomVisibilityManager Instance { get; private set; }

    [Tooltip("Rooms rendered at game start, before the player enters any trigger.")]
    [SerializeField] private RoomController[] _startingRooms;

    [Tooltip("Logs which room becomes current and which rooms stay visible on each switch.")]
    [SerializeField] private bool _debugLogging;

    private RoomController[] _allRooms;
    private object _currentSource;
    private readonly HashSet<RoomController> _activeRooms = new();
    private readonly HashSet<string> _activeZones = new();
    private readonly HashSet<string> _allZones = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _allRooms = FindObjectsByType<RoomController>(FindObjectsSortMode.None);

        // Cache every managed zone id once so suppression can be applied as a clean set.
        _allZones.Clear();
        foreach (var room in _allRooms)
        {
            if (room?.ZoneIds == null) continue;
            foreach (var zoneId in room.ZoneIds)
                if (!string.IsNullOrEmpty(zoneId)) _allZones.Add(zoneId);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (_startingRooms != null && _startingRooms.Length > 0)
            SetVisibleRooms(null, _startingRooms);
    }

    /// <summary>
    /// Makes the given room the only active one and refreshes rendering. Fallback used by
    /// a RoomTrigger that has no configured room list.
    /// </summary>
    public void SetCurrentRoom(RoomController room)
    {
        if (room == null || _allRooms == null) return;
        if (ReferenceEquals(_currentSource, room)) return;
        _currentSource = room;

        _activeRooms.Clear();
        _activeRooms.Add(room);

        ApplyActiveRooms(room.name);
    }

    /// <summary>
    /// Makes the given explicit set of rooms the active ones, regardless of any room
    /// whitelist. Lets a single trigger render an arbitrary group of rooms, so a long
    /// corridor can be split into multiple trigger zones each lighting different rooms.
    /// Called by RoomTrigger when it has its own Visible Rooms list.
    /// </summary>
    public void SetVisibleRooms(RoomTrigger source, IReadOnlyList<RoomController> rooms)
    {
        if (_allRooms == null) return;
        if (source != null && ReferenceEquals(_currentSource, source)) return;
        _currentSource = source;

        _activeRooms.Clear();
        if (rooms != null)
        {
            foreach (var room in rooms)
                if (room != null) _activeRooms.Add(room);
        }

        ApplyActiveRooms(source != null ? source.name : "Trigger");
    }

    /// <summary>
    /// Applies the current _activeRooms set to geometry and light zones. A zone stays lit
    /// if ANY active room owns it; the union avoids order-dependent conflicts when a
    /// single ZoneId is shared across several rooms.
    /// </summary>
    private void ApplyActiveRooms(string sourceName)
    {
        // Geometry: toggle each room's renderers independently.
        foreach (var candidate in _allRooms)
        {
            if (candidate != null)
                candidate.SetGeometryActive(_activeRooms.Contains(candidate));
        }

        // Lights: a zone is lit if ANY active room owns it.
        _activeZones.Clear();
        foreach (var active in _activeRooms)
        {
            if (active?.ZoneIds == null) continue;
            foreach (var zoneId in active.ZoneIds)
                if (!string.IsNullOrEmpty(zoneId)) _activeZones.Add(zoneId);
        }

        if (LightingSystem.Instance != null)
        {
            foreach (var zoneId in _allZones)
                LightingSystem.Instance.SetZoneRenderSuppressed(zoneId, !_activeZones.Contains(zoneId));
        }

        if (_debugLogging)
        {
            var visible = new List<string>();
            foreach (var r in _activeRooms)
                if (r != null) visible.Add(r.name);
            Debug.Log($"[RoomVisibility] Source: '{sourceName}'. Visible: {string.Join(", ", visible)}");
        }
    }
}
