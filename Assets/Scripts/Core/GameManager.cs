using System;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Menu UI")]
    [SerializeField] private GameObject menuUI;

    private int _currentRoomIndex = 0;
    private bool _isPaused;

    public int CurrentRoomIndex => _currentRoomIndex;
    public int TotalRooms => rooms != null ? rooms.Length : 0;
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Returns the local Volume of the currently active room, or null if not assigned.
    /// Used by SetPause to enable/disable post-processing per room.
    /// </summary>
    private Volume CurrentVolume =>
        rooms != null && _currentRoomIndex < rooms.Length && rooms[_currentRoomIndex] != null
            ? rooms[_currentRoomIndex].LocalVolume
            : null;

    public event Action<int> OnRoomChanged;
    public event Action OnGameCompleted;
    public event Action<bool> OnPauseStateChanged;

    private void Awake()
    {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
            return;
        }
        // InitializeRooms вызывается в Start — все RoomController.Awake() к этому моменту уже выполнены
    }

    private void Start() {
        InitializeRooms();
        UpdateCursorState();
        SetPause(true);
        if (InputManager.Instance != null) {
            InputManager.Instance.OnMenuPerformed += OnToggleMenu;
        }
    }

    private void OnDisable() {
        if (InputManager.Instance != null) {
            InputManager.Instance.OnMenuPerformed -= OnToggleMenu;
        }
    }

    private void OnToggleMenu()
    {
        // Не обрабатываем ESC если открыта другая панель (инвентарь, превью и т.д.)
        if (!_isPaused && UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen)
            return;

        TogglePause();
    }

    /// <summary>
    /// Toggles the game pause state.
    /// </summary>
    public void TogglePause()
    {
        SetPause(!_isPaused);
    }

    /// <summary>
    /// Sets the pause state and updates time scale and cursor.
    /// </summary>
    public void SetPause(bool pause)
    {
        _isPaused = pause;
        Time.timeScale = _isPaused ? 0f : 1f;

        if (_isPaused)
        {
            UIManager.Instance?.OpenPanel(menuUI);
            AudioManager.Instance?.PlayMenuMusic();
        }
        else
        {
            UIManager.Instance?.ClosePanel(menuUI);
            AudioManager.Instance?.PlayGameMusic();
        }

        UpdateCursorState();
        OnPauseStateChanged?.Invoke(_isPaused);
    }

    /// <summary>
    /// Updates the cursor lock and visibility based on pause state and UI panels.
    /// </summary>
    public void UpdateCursorState()
    {
        bool shouldUnlock = _isPaused || (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen);
        
        Cursor.lockState = shouldUnlock ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = shouldUnlock;
    }

    /// <summary>
    /// Locks all rooms except the first one.
    /// Locking disables interaction colliders � room geometry stays visible.
    /// </summary>
    private void InitializeRooms()
    {
        if (rooms == null || rooms.Length == 0)
        {
            Debug.LogWarning("GameManager: Rooms array is empty or not assigned.", this);
            return;
        }

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
