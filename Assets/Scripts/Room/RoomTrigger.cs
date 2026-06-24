using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger volume that controls which rooms stay rendered while the player is inside it.
/// Its room list is supplied by the owning RoomController via Configure(), so a long
/// corridor can use several triggers each lighting a different group of rooms. The owning
/// room is always kept visible, so it never needs to be added to the list manually.
/// If no list is configured, it falls back to the parent RoomController.
/// Reports both entry and exit so the manager can keep the union of all overlapping
/// triggers visible, preventing culled geometry when the player half-steps between rooms.
/// Requires a Collider with isTrigger = true.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomTrigger : MonoBehaviour
{
    [Tooltip("Fallback room used when no Visible Rooms are configured by a RoomController. " +
             "Auto-filled from the parent RoomController.")]
    [SerializeField] private RoomController _room;

    private RoomController[] _activeRooms;

    /// <summary>
    /// Adds the given rooms to the set this trigger renders. The owner room is always
    /// included so the room the player stands in is never culled. Called by the owning
    /// RoomController on Awake. This MERGES with any rooms a previous caller already
    /// configured, so when several RoomControllers reference the same trigger their lists
    /// combine instead of the last one silently overwriting the others.
    /// </summary>
    public void Configure(RoomController owner, RoomController[] visibleRooms)
    {
        var rooms = _activeRooms != null
            ? new List<RoomController>(_activeRooms)
            : new List<RoomController>();

        if (owner != null && !rooms.Contains(owner))
            rooms.Add(owner);

        if (visibleRooms != null)
        {
            foreach (var room in visibleRooms)
                if (room != null && !rooms.Contains(room)) rooms.Add(room);
        }

        _activeRooms = rooms.ToArray();
    }

    private void Reset()
    {
        _room = GetComponentInParent<RoomController>();
        if (TryGetComponent(out Collider col))
            col.isTrigger = true;
    }

    private void Awake()
    {
        if (_room == null)
            _room = GetComponentInParent<RoomController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<FPSController>() == null) return;

        var manager = RoomVisibilityManager.Instance;
        if (manager == null) return;

        manager.EnterTrigger(this, ResolveRooms());
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<FPSController>() == null) return;

        var manager = RoomVisibilityManager.Instance;
        if (manager == null) return;

        manager.ExitTrigger(this);
    }

    private void OnDisable()
    {
        // Make sure a disabled/destroyed trigger never stays registered as occupied.
        if (RoomVisibilityManager.Instance != null)
            RoomVisibilityManager.Instance.ExitTrigger(this);
    }

    /// <summary>
    /// Returns the rooms this trigger keeps visible: the configured list if present,
    /// otherwise the fallback parent room.
    /// </summary>
    private RoomController[] ResolveRooms()
    {
        if (_activeRooms != null && _activeRooms.Length > 0)
            return _activeRooms;
        if (_room != null)
            return new[] { _room };
        return Array.Empty<RoomController>();
    }
}
