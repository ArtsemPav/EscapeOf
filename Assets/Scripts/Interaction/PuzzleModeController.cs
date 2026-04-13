using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Service component that handles entering / exiting puzzle mode for a puzzle GameObject.
/// Manages the puzzle camera, FPS input blocking, and Esc handling.
/// </summary>
public class PuzzleModeController : MonoBehaviour, ISaveable
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Save Settings")]
    [SerializeField] private string _saveId = "puzzle_mode_unique_id";

    [Header("Camera")]
    [Tooltip("CinemachineCamera that frames the puzzle. Starts inactive and activates on Interact.")]
    [SerializeField] private CinemachineCamera _puzzleCamera;

    [Header("Events")]
    [Tooltip("Fired when the player enters puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeEntered;

    [Tooltip("Fired when the player exits puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeExited;

    [Tooltip("Fired when the puzzle is solved.")]
    [SerializeField] private UnityEvent OnPuzzleSolved;

    // ── C# Events (for code-side subscriptions; prefer these over AddListener) ─

    /// <summary>Raised when the player enters puzzle mode.</summary>
    public event Action OnEntered;

    /// <summary>Raised when the player exits puzzle mode.</summary>
    public event Action OnExited;

    /// <summary>Raised when the puzzle is solved.</summary>
    public event Action OnSolved;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _isActive;
    private bool _isSolved;
    private bool _isSubscribed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the puzzle mode is currently active.</summary>
    public bool IsActive => _isActive;

    /// <summary>True if the puzzle has been solved.</summary>
    public bool IsSolved => _isSolved;

    /// <summary>
    /// Marks the puzzle as solved, exits puzzle mode, and saves state.
    /// </summary>
    public void SetSolved()
    {
        if (_isSolved) return;

        _isSolved = true;
        OnPuzzleSolved?.Invoke();
        OnSolved?.Invoke();

        if (_isActive)
        {
            ExitPuzzleMode();
        }

        // Notify SaveManager to persist the solved state
        SaveManager.Instance?.Save();
    }

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isSolved = _isSolved });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.isSolved;

        if (_isSolved)
        {
            OnPuzzleSolved?.Invoke();
            OnSolved?.Invoke();
        }
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
    }

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

        SaveManager.Instance?.Register(this);
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
        SaveManager.Instance?.Unregister(this);
    }

    // ── Puzzle Mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the puzzle mode: enables camera, blocks player input, and shows cursor.
    /// </summary>
    public void EnterPuzzleMode()
    {
        if (_isActive || _isSolved) return;

        _isActive = true;

        if (_puzzleCamera != null)
        {
            _puzzleCamera.gameObject.SetActive(true);
        }

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Show cursor for puzzle interaction.
        SetCursorState(true);

        OnPuzzleModeEntered?.Invoke();
        OnEntered?.Invoke();
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
        OnExited?.Invoke();
    }

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
