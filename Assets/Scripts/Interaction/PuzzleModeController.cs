using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Service component that handles entering / exiting puzzle mode for a puzzle GameObject.
/// Manages the puzzle camera, FPS input blocking, and Esc handling.
///
/// Usage:
///   • Add to the same GameObject as the puzzle script (e.g. LockDial).
///   • The puzzle script calls <see cref="EnterPuzzleMode"/> from its own Interact() method.
///   • Esc is handled automatically — no wiring required.
///   • Listen to <see cref="OnPuzzleModeEntered"/> / <see cref="OnPuzzleModeExited"/> if
///     other systems need to react to mode changes.
/// </summary>
public class PuzzleModeController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("CinemachineCamera that frames the puzzle. Starts inactive and activates on Interact.")]
    [SerializeField] private CinemachineCamera _puzzleCamera;

    [Header("Hint")]
    [Tooltip("Text shown in InteractionUI while puzzle mode is active.")]
    [SerializeField] private string _activeText = "Выход: Esc";

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

    // ── Puzzle Mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the puzzle camera and blocks FPS input.
    /// Cursor is kept locked so that Mouse.delta returns valid per-frame delta
    /// for drag-based puzzle interactions (e.g. LockDial).
    /// </summary>
    public void EnterPuzzleMode()
    {
        _isActive = true;

        if (_puzzleCamera != null)
            _puzzleCamera.gameObject.SetActive(true);

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Free the cursor for drag interaction within the puzzle.
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
