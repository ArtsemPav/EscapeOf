using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers every room the player has ever entered (by room name, as reported
/// by <see cref="RoomVisibilityManager.CurrentRoomChanged"/>). Used by the ghost
/// to confine its pranks to rooms the player has already opened up — activity
/// in never-visited rooms would be invisible noise.
/// Survives scene reloads for the session; not persisted to saves by design —
/// a fresh save starts with a quiet house.
/// </summary>
public static class RoomHistory
{
    /// <summary>All room names the player has physically entered at least once.</summary>
    private static readonly HashSet<string> VisitedRooms = new HashSet<string>();

    /// <summary>Hooked once on first use — RoomVisibilityManager is a scene singleton.</summary>
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // Domain-reload-disabled safety: clear the static state between plays.
        VisitedRooms.Clear();
        _hooked = false;
    }

    /// <summary>True when the player has entered this room at least before. Unknown names (null/empty) are never visited.</summary>
    public static bool HasVisited(string roomName)
    {
        Hook();
        return !string.IsNullOrEmpty(roomName) && VisitedRooms.Contains(roomName);
    }

    /// <summary>
    /// True when any RoomController whose geometry bounds contain the point
    /// belongs to a room the player has visited. Answers "is this world
    /// position inside an opened-up room?" for prank target checks.
    /// </summary>
    public static bool ContainsRoom(Vector3 worldPosition)
    {
        Hook();
        if (VisitedRooms.Count == 0) return false;

        foreach (RoomController room in Object.FindObjectsByType<RoomController>(
                     FindObjectsSortMode.None))
        {
            if (room != null
                && room.ContainsPoint(worldPosition)
                && HasVisited(room.gameObject.name))
            {
                return true;
            }
        }

        return false;
    }

    private static void Hook()
    {
        if (_hooked) return;
        if (RoomVisibilityManager.Instance == null) return;

        _hooked = true;
        RoomVisibilityManager.Instance.CurrentRoomChanged += OnRoomChanged;

        // Record the room the player starts in, if any.
        if (!string.IsNullOrEmpty(RoomVisibilityManager.Instance.CurrentRoomName))
        {
            VisitedRooms.Add(RoomVisibilityManager.Instance.CurrentRoomName);
        }
    }

    private static void OnRoomChanged(string roomName)
    {
        if (!string.IsNullOrEmpty(roomName)) VisitedRooms.Add(roomName);
    }
}
