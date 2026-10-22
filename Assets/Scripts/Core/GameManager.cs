using System;
using UnityEngine;

/// <summary>
/// Global pause controller: ESC menu, time scale, cursor state, audio DSP pause
/// and menu music switching. Singleton; the GameObject is DontDestroyOnLoad.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Menu UI")]
    [SerializeField] private GameObject menuUI;

    private bool _isPaused;

    public bool IsPaused => _isPaused;

    public event Action<bool> OnPauseStateChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
            return;
        }
    }

    private void Start() {
        UpdateCursorState();
        SetPause(false); // start directly in gameplay — no pause menu on scene load
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
        // ESC закрывает таб-инвентарь и не открывает паузу в тот же кадр.
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen)
        {
            InventoryUI.Instance.CloseInventory();
            return;
        }

        // Не обрабатываем ESC если открыта другая панель (превью, пазл и т.д.)
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
        // Pause the audio DSP for the whole game — silence everything except
        // sources marked ignoreListenerPause (the pause menu music only).
        AudioListener.pause = pause;

        if (_isPaused)
        {
            UIManager.Instance?.OpenPanel(menuUI);
            MusicDirector.PlayMenuMusic();
        }
        else
        {
            UIManager.Instance?.ClosePanel(menuUI);
            MusicDirector.PlayGameMusic();
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
}
