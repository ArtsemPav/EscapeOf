using System;
using System.Collections.Generic;
using System.Linq;
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
    private FPSController _player;
    private readonly List<RoomTrigger> _allTriggers = new();

    /// <summary>Set once the spawn position has been reconciled, so Start() never
    /// overwrites the reconciled active set with _startingRooms (order-independent).</summary>
    private bool _spawnReconciled;

    // Spawn reconcile deferred to Update, so every Start() (including RoomPuzzleGate
    // enabling its trigger colliders) has run by the time it executes.
    private Vector3? _pendingSpawnReconcile;

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
        _player = FindFirstObjectByType<FPSController>();
        _allTriggers.AddRange(FindObjectsByType<RoomTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        // Cache every managed zone id once so suppression can be applied as a clean set.
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
        // Skip when the player already reconciled from a save — applying _startingRooms
        // here would cull the room the player respawned in (its trigger never fired
        // because the player was teleported INTO it, so no OnTriggerEnter).
        if (_spawnReconciled) return;

        if (_startingRooms != null && _startingRooms.Length > 0)
            SetActiveRoomsDirect(_startingRooms, "Start");
    }

    private void Update()
    {
        // Deferred spawn reconcile: by the first Update every Start() has run, so gated
        // triggers are already re-enabled by their RoomPuzzleGate and ClosestPoint can
        // match them. Reconciling earlier (from FPSController.Start) runs while the gate
        // colliders are still disabled and misses every trigger.
        if (_pendingSpawnReconcile.HasValue)
        {
            Vector3 position = _pendingSpawnReconcile.Value;
            _pendingSpawnReconcile = null;
            ReconcileAfterSpawnInternal(position);
            return;
        }

        // Continuous occupancy sync: teleport-capable flows (save load, cheat panel,
        // cinematics) move the player WITHOUT physics sweeps, so OnTriggerEnter/Exit for
        // the moved capsule are unreliable — _occupiedTriggers can drift from reality
        // (e.g. listing a trigger the player has actually left), and the next ExitTrigger
        // then recomputes the union without the room the player stands in, culling it.
        // Rebuilding occupancy from point containment every frame keeps the union honest;
        // a synchronous pass over 30+ triggers is cheap compared to the geometry toggles.
        SyncOccupiedTriggers();
    }

    /// <summary>
    /// Rebuilds _occupiedTriggers from the player's current position (point containment,
    /// same test the spawn reconcile uses) and recomputes visibility when the set changed.
    /// Physics events remain the fast path: when the synced set equals the tracked one,
    /// nothing is recomputed.
    /// </summary>
    private void SyncOccupiedTriggers()
    {
        if (_player == null || _allRooms == null || _allRooms.Length == 0) return;

        Vector3 position = _player.transform.position;

        bool changed = false;

        // Drop triggers the player is no longer inside (teleports skip OnTriggerExit).
        foreach (var trigger in _occupiedTriggers.Keys.ToList())
        {
            if (trigger == null || !trigger.ContainsPoint(position))
            {
                _occupiedTriggers.Remove(trigger);
                changed = true;
            }
        }

        // Register triggers the player is inside but that never reported an enter event
        // (spawned/restored inside the volume, or teleported into it).
        foreach (var trigger in _allTriggers)
        {
            if (trigger == null || _occupiedTriggers.ContainsKey(trigger)) continue;
            if (!trigger.ContainsPoint(position)) continue;

            _occupiedTriggers[trigger] = trigger.ResolveRooms();
            changed = true;
        }

        // Gap tolerance: when the player has left EVERY trigger (vent shafts, doorways,
        // unmapped corridor sections), keep the last visible set instead of recomputing
        // from an empty union — same contract as ExitTrigger's last-exit branch. Without
        // this, the first step into a coverage gap culls the room the player is walking
        // toward (e.g. CoridorFDoor right after leaving the basement vent trigger).
        if (_occupiedTriggers.Count == 0)
        {
            if (changed) LogGap(position);
            return;
        }

        if (changed) RecomputeActiveRooms("OccupancySync");
    }

    /// <summary>Diagnostics for coverage gaps: logs the closest trigger and how far the player is from it.</summary>
    private void LogGap(Vector3 position)
    {
        RoomTrigger nearest = null;
        float nearestSqr = float.MaxValue;

        foreach (var trigger in _allTriggers)
        {
            if (trigger == null) continue;
            float sqr = trigger.SqrDistanceTo(position, out _);
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = trigger;
            }
        }

        Debug.Log($"[RoomVisibility] OccupancySync: player in a trigger GAP at {position.ToString("F1")}. " +
                  $"Closest trigger: {(nearest != null ? nearest.name : "none")}, " +
                  $"distance {(Mathf.Sqrt(nearestSqr)).ToString("F2")}m (path: {nearest?.transform.parent?.name}/{nearest?.name})");
    }

    /// <summary>
    /// Queues a visibility rebuild from the player's spawn/teleport position. Applied on
    /// the next Update so it runs after all Start() calls — trigger colliders enabled by
    /// RoomPuzzleGate must be live for the trigger match to succeed.
    /// </summary>
    public void ReconcileAfterSpawn(Vector3 playerPosition)
    {
        _spawnReconciled = true;
        _pendingSpawnReconcile = playerPosition;
    }

    /// <summary>
    /// Raised when the room the player physically stands in changes (tightest geometry
    /// bounds containing the player). Parameter: room GameObject name, null when nowhere.
    /// Used by ProgressLogger to record room visits.
    /// </summary>
    public event Action<string> CurrentRoomChanged;

    /// <summary>Room name reported by the last CurrentRoomChanged event.</summary>
    public string CurrentRoomName { get; private set; }

    /// <summary>
    /// Rebuilds room visibility from the player's spawn/teleport position without relying
    /// on physics events. Unity fires OnTriggerEnter only on overlap CHANGES — a player
    /// restored directly inside a trigger volume generates no event, and Start()-order
    /// races can leave the spawned room culled.
    /// </summary>
    private void ReconcileAfterSpawnInternal(Vector3 playerPosition)
    {
        if (_allRooms == null || _allRooms.Length == 0) return;

        _occupiedTriggers.Clear();

        foreach (var trigger in _allTriggers)
        {
            if (trigger == null || !trigger.ContainsPoint(playerPosition)) continue;
            _occupiedTriggers[trigger] = trigger.ResolveRooms();
        }

        if (_occupiedTriggers.Count > 0)
        {
            RecomputeActiveRooms("SpawnReconcile");
            return;
        }

        // Fallback: the spawn point is covered by no enabled trigger (often because it is
        // outside the volumes on one axis — e.g. below floor-level triggers when the
        // player saved inside a basement vent). Show every room whose geometry bounds
        // contain the point (nested rooms overlap; under-showing breaks sightlines,
        // over-showing only costs a bit of culling) plus the rooms of the nearest
        // trigger for good measure.
        _activeRooms.Clear();
        foreach (var room in _allRooms)
        {
            if (room != null && room.ContainsPoint(playerPosition))
                _activeRooms.Add(room);
        }

        RoomTrigger nearest = GetNearestTrigger(playerPosition);
        if (nearest != null)
        {
            _occupiedTriggers[nearest] = nearest.ResolveRooms();
            foreach (var room in nearest.ResolveRooms())
                if (room != null) _activeRooms.Add(room);
        }

        ApplyActiveRooms("SpawnReconcileFallback");
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
        // When the LAST trigger is exited the previously visible set is intentionally kept:
        // gaps between trigger volumes (crawl spaces, doorways, vent shafts) must not cull
        // geometry while the player walks through them. The next RecomputeActiveRooms
        // (Enter/Reconcile) restores the strict set.
    }

    /// <summary>
    /// Returns the enabled trigger closest to the given point (exact containment reported
    /// via <paramref name="inside"/>), or null when the list is empty.
    /// </summary>
    private RoomTrigger GetNearestTrigger(Vector3 position)
    {
        RoomTrigger nearest = null;
        float nearestSqr = float.MaxValue;

        foreach (var trigger in _allTriggers)
        {
            if (trigger == null) continue;
            float sqr = trigger.SqrDistanceTo(position, out bool inside);
            if (inside) continue; // containment is handled by the caller
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = trigger;
            }
        }

        return nearest;
    }

    /// <summary>Rebuilds the active room set as the union of every currently occupied trigger's list.</summary>
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
        // Safety net: the room the player physically stands in is never culled, no matter
        // what the occupied-trigger union says (teleport races, vent shafts below the
        // trigger volumes, missing trigger coverage).
        RoomController currentRoom = FindCurrentRoom();
        ReportCurrentRoom(currentRoom);

        // Geometry: toggle each room's renderers independently.
        foreach (var candidate in _allRooms)
        {
            if (candidate == null) continue;
            bool isActive = _activeRooms.Contains(candidate) || candidate == currentRoom;
            candidate.SetGeometryActive(isActive);
        }

        // Lights: a zone is lit if ANY active room owns it.
        _activeZones.Clear();
        if (currentRoom != null) _activeRooms.Add(currentRoom);
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

        LogState(sourceName);
    }

    /// <summary>
    /// Returns the tightest room whose geometry bounds contain the player position,
    /// or null when the player is nowhere (e.g. between rooms) or the player is missing.
    /// </summary>
    private RoomController FindCurrentRoom()
    {
        if (_player == null) return null;

        RoomController current = null;
        float bestVolume = float.MaxValue;
        Vector3 position = _player.transform.position;

        foreach (var room in _allRooms)
        {
            if (room == null || !room.ContainsPoint(position)) continue;
            if (current == null || room.GeometryVolume < bestVolume)
            {
                bestVolume = room.GeometryVolume;
                current = room;
            }
        }

        return current;
    }

    /// <summary>Fires CurrentRoomChanged when the physical room changes.</summary>
    private void ReportCurrentRoom(RoomController room)
    {
        string name = room != null ? room.gameObject.name : null;
        if (string.Equals(name, CurrentRoomName, StringComparison.Ordinal)) return;

        CurrentRoomName = name;
        CurrentRoomChanged?.Invoke(name);
    }

    private void LogState(string sourceName)
    {
        var visible = new List<string>();
        foreach (var room in _activeRooms)
            if (room != null) visible.Add(room.name);

        var occupied = new List<string>();
        foreach (var t in _occupiedTriggers.Keys)
            if (t != null) occupied.Add($"{t.name}@{_player.transform.position.ToString("F1")}");

        Debug.Log($"[RoomVisibility] Source: '{sourceName}'. Occupied({occupied.Count}): [{string.Join(", ", occupied)}]. " +
                  $"Visible({visible.Count}): [{string.Join(", ", visible)}]. Pos: {_player.transform.position.ToString("F1")}");
    }
}
