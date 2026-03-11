using System;
using UnityEngine;

/// <summary>
/// Tracks room progression and unlocks interaction in the next room.
/// All rooms remain active in the scene at all times.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Rooms")]
    [Tooltip("Assign RoomController components in order Room_01..Room_05.")]
    [SerializeField] private RoomController[] rooms;

    private int _currentRoomIndex = 0;

    public int CurrentRoomIndex => _currentRoomIndex;
    public int TotalRooms => rooms.Length;

    public event Action<int> OnRoomChanged;
    public event Action OnGameCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InitializeRooms();
    }

    /// <summary>
    /// Locks all rooms except the first one.
    /// Locking disables interaction colliders — room geometry stays visible.
    /// </summary>
    private void InitializeRooms()
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            if (rooms[i] == null) continue;

            if (i == 0)
                rooms[i].Unlock();
            else
                rooms[i].Lock();
        }
    }

    /// <summary>
    /// Called by RoomDoor when the player successfully exits the current room.
    /// Unlocks the next room for interaction.
    /// </summary>
    public void OnRoomExited()
    {
        int nextIndex = _currentRoomIndex + 1;

        if (nextIndex >= rooms.Length)
        {
            OnGameCompleted?.Invoke();
            return;
        }

        rooms[nextIndex].Unlock();
        _currentRoomIndex = nextIndex;
        OnRoomChanged?.Invoke(_currentRoomIndex);
    }
}
