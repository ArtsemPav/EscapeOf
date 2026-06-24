using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger volume that controls which rooms stay rendered while the player is inside it.
/// Its room list is supplied by the owning RoomController via Configure(), so a long
/// corridor can use several triggers each lighting a different group of rooms. The owning
/// room is always kept visible, so it never needs to be added to the list manually.
/// If no list is configured, it falls back to the parent RoomController.
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
    /// Sets the rooms this trigger renders. The owner room is always included so the room
    /// the player stands in is never culled. Called by the owning RoomController on Awake.
    /// </summary>
    public void Configure(RoomController owner, RoomController[] visibleRooms)
    {
        var rooms = new List<RoomController>();

        if (owner != null)
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

        if (_activeRooms != null && _activeRooms.Length > 0)
            manager.SetVisibleRooms(this, _activeRooms);
        else if (_room != null)
            manager.SetCurrentRoom(_room);
    }
}
