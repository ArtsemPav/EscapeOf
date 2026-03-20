using UnityEngine;

/// <summary>
/// Central manager for all UI panels in the game.
/// Handles cursor state, player input locking, and panel stack.
///
/// SETUP: Add this component to a persistent GameObject in the scene.
/// Assign the FPSController and a GameConfig asset in the Inspector.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("The player FPS controller. Drag the Player GameObject here.")]
    [SerializeField] private FPSController _playerController;

    [Header("Configuration")]
    [Tooltip("Game-wide config: texts and colors. Create via right-click → Create → Game → Game Config.")]
    [SerializeField] private GameConfig _config;

    /// <summary>Game-wide configuration. Access texts and colors via UIManager.Instance.Config.</summary>
    public GameConfig Config => _config;

    /// <summary>The player's FPS controller. All UI systems read this reference.</summary>
    public FPSController PlayerController => _playerController;

    private int _openPanelCount;

    /// <summary>True when at least one UI panel is currently open.</summary>
    public bool IsAnyPanelOpen => _openPanelCount > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (_playerController == null)
            Debug.LogWarning("UIManager: FPSController not assigned. Player input will not be blocked when panels open.", this);

        if (_config == null)
            Debug.LogWarning("UIManager: GameConfig not assigned. UI texts and colors will use fallback values.", this);
    }

    /// <summary>
    /// Opens a panel: activates its GameObject, shows the cursor, and disables player input.
    /// Also hides the interaction hint — it reappears automatically when the panel is closed.
    /// </summary>
    /// <param name="panel">Root GameObject of the panel to open.</param>
    /// <param name="cursorMode">Cursor lock mode while the panel is open. Defaults to None (free cursor).</param>
    public void OpenPanel(GameObject panel, CursorLockMode cursorMode = CursorLockMode.None) {
        _openPanelCount++;
        panel.SetActive(true);
        
        GameManager.Instance?.UpdateCursorState();

        if (_playerController != null) {
            _playerController.SetPlayerInputEnabled(false);
            _playerController.ResetInteractionCache();
        }

        InteractionUI.Instance?.SetHint(false);
    }

    /// <summary>
    /// Closes a panel: deactivates its GameObject.
    /// Restores cursor and player input only when all panels are closed.
    /// Interaction hint reappears automatically on the next frame if the player is still looking at an object.
    /// </summary>
    /// <param name="panel">Root GameObject of the panel to close.</param>
    public void ClosePanel(GameObject panel)
    {
        _openPanelCount = Mathf.Max(0, _openPanelCount - 1);
        panel.SetActive(false);

        GameManager.Instance?.UpdateCursorState();

        if (!IsAnyPanelOpen)
        {
            _playerController?.SetPlayerInputEnabled(true);

            // Сбрасываем кеш — следующий кадр Update() переопределит объект под прицелом
            // и снова покажет подсказку если нужно.
            _playerController?.ResetInteractionCache();
        }
    }

    /// <summary>
    /// Forces all panels closed and immediately restores gameplay state.
    /// Use for emergency resets (e.g., death screen, scene reload).
    /// </summary>
    public void CloseAll()
    {
        _openPanelCount = 0;
        GameManager.Instance?.UpdateCursorState();
        _playerController?.SetPlayerInputEnabled(true);
        _playerController?.ResetInteractionCache();
    }
}
