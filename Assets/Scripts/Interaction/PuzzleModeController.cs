using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attaches to a puzzle GameObject and handles entering / exiting puzzle mode.
///
/// Flow:
///   1. Player presses E while looking at the puzzle — the puzzle camera activates,
///      FPS input is blocked, and the cursor becomes free.
///   2. Press Esc — exits puzzle mode, restores the player camera and FPS input.
///
/// Setup:
///   • Add this component to the puzzle's root GameObject.
///   • Assign <see cref="_puzzleCamera"/> — a CinemachineCamera pointing at the puzzle.
///   • (Optional) Handle <see cref="OnPuzzleModeEntered"/> and <see cref="OnPuzzleModeExited"/>
///     in other systems that need to react to mode changes.
/// </summary>
public class PuzzleModeController : MonoBehaviour, IInteractable
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("CinemachineCamera that frames the puzzle. Starts inactive and activates on Interact.")]
    [SerializeField] private CinemachineCamera _puzzleCamera;

    [Header("Interaction Text")]
    [SerializeField] private string _interactText = "Взаимодействовать";
    [SerializeField] private string _activeText   = "Выход: Esc";

    [Header("Events")]
    [Tooltip("Fired when the player enters puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeEntered;

    [Tooltip("Fired when the player exits puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeExited;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _isActive;
    private bool _menuSubscribed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the puzzle mode is currently active.</summary>
    public bool IsActive => _isActive;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_puzzleCamera != null)
            _puzzleCamera.gameObject.SetActive(false);
        else
            Debug.LogWarning("[PuzzleModeController] Puzzle camera is not assigned.", this);
    }

    private void Start()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnMenuPerformed += OnMenuPerformed;
            _menuSubscribed = true;
        }
        else
        {
            Debug.LogWarning("[PuzzleModeController] InputManager.Instance is null in Start. Esc will not exit puzzle mode.", this);
        }
    }

    private void OnDisable()
    {
        if (_menuSubscribed && InputManager.Instance != null)
        {
            InputManager.Instance.OnMenuPerformed -= OnMenuPerformed;
            _menuSubscribed = false;
        }
    }

    private void OnDestroy()
    {
        if (_isActive)
            ExitPuzzleMode();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Puzzle can only be interacted with when not already active.</summary>
    public bool CanInteract() => !_isActive;

    /// <summary>Entering puzzle mode on player interaction.</summary>
    public void Interact()
    {
        if (_isActive) return;
        EnterPuzzleMode();
    }

    public string GetInteractText() => _isActive ? _activeText : _interactText;
    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Puzzle Mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the puzzle camera, blocks FPS input, and frees the cursor.
    /// </summary>
    public void EnterPuzzleMode()
    {
        _isActive = true;

        if (_puzzleCamera != null)
            _puzzleCamera.gameObject.SetActive(true);

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Free the cursor for mouse interaction with the puzzle UI.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        InteractionUI.Instance?.SetHint(true, _activeText, false, CrosshairMode.Default);

        OnPuzzleModeEntered?.Invoke();
    }

    /// <summary>
    /// Deactivates the puzzle camera and restores FPS input and the locked cursor.
    /// </summary>
    public void ExitPuzzleMode()
    {
        _isActive = false;

        if (_puzzleCamera != null)
            _puzzleCamera.gameObject.SetActive(false);

        // Restore FPS input and decrement the modal panel counter.
        UIManager.Instance?.PopModalState();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        InteractionUI.Instance?.SetHint(false);

        OnPuzzleModeExited?.Invoke();
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles the Menu action (Esc). Exits puzzle mode on the next frame so that
    /// GameManager.OnToggleMenu still sees IsAnyPanelOpen == true during the same
    /// event dispatch and skips opening the pause menu.
    /// </summary>
    private void OnMenuPerformed()
    {
        if (!_isActive) return;
        StartCoroutine(ExitNextFrame());
    }

    private IEnumerator ExitNextFrame()
    {
        yield return null;
        ExitPuzzleMode();
    }
}
