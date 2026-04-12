using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Service component that handles entering / exiting puzzle mode for a puzzle GameObject.
/// Manages the puzzle camera, FPS input blocking, and Esc handling.
/// </summary>
public class PuzzleModeController : MonoBehaviour, IInteractable
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Interaction Settings")]
    [SerializeField] private string _interactText = "Осмотреть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    [Header("Camera")]
    [Tooltip("CinemachineCamera that frames the puzzle. Starts inactive and activates on Interact.")]
    [SerializeField] private CinemachineCamera _puzzleCamera;

    [Header("UI & Feedback")]
    [Tooltip("Text shown in InteractionUI while puzzle mode is active.")]
    [SerializeField] private string _activeText = "Выход: Esc";

    [Header("Events")]
    [Tooltip("Fired when the player enters puzzle mode.")]
    [SerializeField] public UnityEvent OnPuzzleModeEntered;

    [Tooltip("Fired when the player exits puzzle mode.")]
    [SerializeField] public UnityEvent OnPuzzleModeExited;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _isActive;
    private bool _isSubscribed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the puzzle mode is currently active.</summary>
    public bool IsActive => _isActive;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_puzzleCamera != null)
        {
            _puzzleCamera.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"[{nameof(PuzzleModeController)}] Puzzle camera is not assigned on {gameObject.name}.", this);
        }
    }

    private void Start()
    {
        SubscribeToInput();
    }

    private void OnEnable()
    {
        SubscribeToInput();
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
    }

    private void OnDestroy()
    {
        if (_isActive)
        {
            ExitPuzzleMode();
        }
        UnsubscribeFromInput();
    }

    // ── Puzzle Mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the puzzle mode: enables camera, blocks player input, and shows cursor.
    /// </summary>
    public void EnterPuzzleMode()
    {
        if (_isActive) return;

        _isActive = true;

        if (_puzzleCamera != null)
        {
            _puzzleCamera.gameObject.SetActive(true);
        }

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Show cursor for puzzle interaction.
        SetCursorState(true);

        if (PopupMessageSystem.Instance != null)
        {
            PopupMessageSystem.Instance.Show(_activeText, PopupMessageType.Warning, 4f);
        }

        OnPuzzleModeEntered?.Invoke();
    }

    /// <summary>
    /// Deactivates the puzzle mode: restores camera, player input, and hides cursor.
    /// </summary>
    public void ExitPuzzleMode()
    {
        if (!_isActive) return;

        _isActive = false;

        if (_puzzleCamera != null)
        {
            _puzzleCamera.gameObject.SetActive(false);
        }

        // Restore FPS input and decrement the modal panel counter.
        UIManager.Instance?.PopModalState();

        // Restore FPS cursor state.
        SetCursorState(false);

        OnPuzzleModeExited?.Invoke();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => !_isActive;

    public void Interact()
    {
        if (CanInteract())
        {
            EnterPuzzleMode();
        }
    }

    public string GetInteractText() => _interactText;
    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => _crosshairMode;

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetCursorState(bool visible)
    {
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;
    }

    private void SubscribeToInput()
    {
        if (_isSubscribed || InputManager.Instance == null) return;

        InputManager.Instance.OnMenuPerformed += OnMenuPerformed;
        _isSubscribed = true;
    }

    private void UnsubscribeFromInput()
    {
        if (!_isSubscribed || InputManager.Instance == null) return;

        InputManager.Instance.OnMenuPerformed -= OnMenuPerformed;
        _isSubscribed = false;
    }

    private void OnMenuPerformed()
    {
        if (!_isActive) return;

        // Use a coroutine to ensure the input event finishes before changing state,
        // preventing the Pause menu from immediately opening.
        StartCoroutine(ExitNextFrameRoutine());
    }

    private IEnumerator ExitNextFrameRoutine()
    {
        yield return null;
        ExitPuzzleMode();
    }
}
