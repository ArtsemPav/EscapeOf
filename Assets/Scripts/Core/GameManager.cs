using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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

    [Header("Post Processing")]
    [Tooltip("Post-processing Volume to disable when the menu is open. If not assigned, it will be found automatically on each scene load.")]
    [SerializeField] private Volume _postProcessVolume;

    private int _currentRoomIndex = 0;
    private bool _isPaused;

    public int CurrentRoomIndex => _currentRoomIndex;
    public int TotalRooms => rooms.Length;
    public bool IsPaused => _isPaused;

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
        InitializeRooms();
    }

    private void Start() {
        UpdateCursorState();
        if (_postProcessVolume == null)
            _postProcessVolume = FindFirstObjectByType<Volume>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Начальное состояние паузы при старте
        SetPause(true);
        if (InputManager.Instance != null) {
            InputManager.Instance.OnMenuPerformed += OnToggleMenu;
        }
    }

    /// <summary>
    /// Re-acquires the post-processing Volume when a new scene is loaded,
    /// since the previous scene's Volume is destroyed on scene transition.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _postProcessVolume = FindFirstObjectByType<Volume>();
    }

    private void OnDisable() {
        SceneManager.sceneLoaded -= OnSceneLoaded;
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
            if (_postProcessVolume != null) _postProcessVolume.enabled = false;
        }
        else
        {
            UIManager.Instance?.ClosePanel(menuUI);
            AudioManager.Instance?.PlayGameMusic();
            if (_postProcessVolume != null) _postProcessVolume.enabled = true;
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
