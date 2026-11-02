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

    [Header("Focus Handling")]
    [Tooltip("Automatically enter the ESC pause (pause menu + muted world) when the game window loses focus (Alt-Tab). The player resumes manually.")]
    [SerializeField] private bool _pauseOnFocusLoss = true;

    // Serialized so a mid-play domain reload (script recompile while playing)
    // restores the pause state instead of resetting it: the native
    // AudioListener.pause / Time.timeScale survive the reload, a managed bool
    // would not — a desync there mutes every AudioSource while the game
    // believes it is unpaused, and Play() silently fails on new sources.
    [SerializeField] private bool _isPaused;

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
    /// Alt-Tab / window focus loss: enter the same pause as ESC so the game and
    /// its music do not keep running while the player is in another application.
    /// The world is muted by AudioListener.pause; MusicDirector silences the
    /// pause-menu track itself while the window is unfocused, so the pause is
    /// fully silent in the background. The player resumes manually (ESC / Resume).
    /// </summary>
    private void OnApplicationFocus(bool hasFocus) {
        if (hasFocus || !_pauseOnFocusLoss || _isPaused) return;
        // A puzzle/inventory panel already occupies the screen — its own state
        // governs audio; do not stack the pause menu on top of it.
        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen) return;
        SetPause(true);
        Debug.Log("[GameManager] Focus lost — auto-paused (ESC pause).");
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

    private void Update()
    {
        // Self-heal a state desync: the native audio pause must always follow
        // the managed pause flag. If they ever diverge (a domain reload
        // mid-play, external code touching AudioListener.pause), realign to
        // the managed state — otherwise the whole world stays muted (or
        // unmutes) behind the director's back and new sources cannot start.
        if (AudioListener.pause != _isPaused)
        {
            Debug.LogWarning($"[GameManager] Audio pause desync (AudioListener.pause={AudioListener.pause}, IsPaused={_isPaused}) — realigned.");
            AudioListener.pause = _isPaused;
            Time.timeScale = _isPaused ? 0f : 1f;
        }
    }
}
