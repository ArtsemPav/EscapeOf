using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Performance light/geometry culling driven by the player's physical location.
/// Tracks every RoomTrigger the player currently overlaps and keeps the UNION of their
/// configured room lists rendered. Because overlapping triggers are combined instead of
/// the last one winning, partially stepping into a room while still standing in a corridor
/// keeps both visible, so backing out never leaves culled geometry (holes in walls).
/// Everything outside the active set is suppressed. Acts as a safety layer on top of
/// occlusion culling.
/// </summary>
public class RoomVisibilityManager : MonoBehaviour
{
    public static RoomVisibilityManager Instance { get; private set; }

    [Tooltip("Rooms rendered at game start, before the player enters any trigger.")]
    [SerializeField] private RoomController[] _startingRooms;

    [Tooltip("Logs which triggers are occupied and which rooms stay visible on each change.")]
    [SerializeField] private bool _debugLogging;

    private RoomController[] _allRooms;

    // Triggers the player is currently inside, mapped to the rooms each one keeps visible.
    private readonly Dictionary<RoomTrigger, IReadOnlyList<RoomController>> _occupiedTriggers = new();
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
            SetActiveRoomsDirect(_startingRooms, "Start");
    }

    /// <summary>
    /// Registers a trigger the player has just entered along with the rooms it keeps visible,
    /// then recomputes the active set as the union of all currently occupied triggers.
    /// </summary>
    public void EnterTrigger(RoomTrigger trigger, IReadOnlyList<RoomController> rooms)
    {
        if (trigger == null || _allRooms == null) return;
        _occupiedTriggers[trigger] = rooms;
        RecomputeActiveRooms($"Enter '{trigger.name}'");
    }

    /// <summary>
    /// Removes a trigger the player has left and recomputes the active set from the triggers
    /// that remain. If the player is no longer inside any trigger, the last visible set is
    /// kept to avoid culling geometry during gaps between trigger volumes.
    /// </summary>
    public void ExitTrigger(RoomTrigger trigger)
    {
        if (trigger == null || _allRooms == null) return;
        if (!_occupiedTriggers.Remove(trigger)) return;

        if (_occupiedTriggers.Count > 0)
            RecomputeActiveRooms($"Exit '{trigger.name}'");
    }

    /// <summary>
    /// Rebuilds the active room set as the union of every currently occupied trigger's list.
    /// </summary>
    private void RecomputeActiveRooms(string sourceName)
    {
        _activeRooms.Clear();
        foreach (var rooms in _occupiedTriggers.Values)
        {
            if (rooms == null) continue;
            foreach (var room in rooms)
                if (room != null) _activeRooms.Add(room);
        }
        ApplyActiveRooms(sourceName);
    }

    /// <summary>
    /// Sets the active rooms to an explicit list, bypassing the occupied-trigger union.
    /// Used for the initial starting state.
    /// </summary>
    private void SetActiveRoomsDirect(IReadOnlyList<RoomController> rooms, string sourceName)
    {
        _activeRooms.Clear();
        if (rooms != null)
        {
            foreach (var room in rooms)
                if (room != null) _activeRooms.Add(room);
        }
        ApplyActiveRooms(sourceName);
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
            Debug.Log($"[RoomVisibility] Source: '{sourceName}'. Occupied triggers: {_occupiedTriggers.Count}. Visible: {string.Join(", ", visible)}");
        }
    }
}
