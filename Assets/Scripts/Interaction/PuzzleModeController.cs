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

    [Tooltip("Duration of the Cinemachine blend when entering and exiting the puzzle camera (seconds).")]
    [SerializeField, Min(0f)] private float _blendDuration = 0.75f;

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

    private CinemachineBrain _brain;
    private float            _originalBlendTime;

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

        // Cache the CinemachineBrain and store the original blend time for restoration.
        _brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();

        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Deferred subscription: InputManager singleton is guaranteed to exist by Start.
        SubscribeToInput();
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
    }

    private void OnEnable()
    {
        // Re-subscribe after a hot-reload or after the object is re-enabled
        // (only if Start has already run, i.e. InputManager is already available).
        if (_isSubscribed == false && InputManager.Instance != null)
        {
            SubscribeToInput();
        }
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

        // Enable child interactables when entering puzzle mode (e.g. puzzle buttons/elements).
        SetChildInteractablesEnabled(true);

        // Disable main puzzle interactable collider to prevent raycast blocking or accidental re-triggering.
        SetPuzzleInteractableColliderEnabled(false);

        if (_puzzleCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _puzzleCamera.gameObject.SetActive(true);
            StartCoroutine(RestoreBlendAfterTransition());
        }

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Show cursor for puzzle interaction.
        SetCursorState(true);
        if (UI.PuzzleCursor.Instance != null)
        {
            UI.PuzzleCursor.Instance.Show();
        }

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

        // Disable child interactables when exiting puzzle mode.
        SetChildInteractablesEnabled(false);

        // Re-enable main puzzle interactable collider.
        SetPuzzleInteractableColliderEnabled(true);

        if (_puzzleCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _puzzleCamera.gameObject.SetActive(false);
            StartCoroutine(RestoreBlendAfterTransition());
        }

        // Restore FPS input and decrement the modal panel counter.
        UIManager.Instance?.PopModalState();

        // Restore FPS cursor state.
        SetCursorState(false);
        if (UI.PuzzleCursor.Instance != null)
        {
            UI.PuzzleCursor.Instance.Hide();
        }

        OnPuzzleModeExited?.Invoke();
        OnExited?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetChildInteractablesEnabled(bool isEnabled)
    {
        var interactables = GetComponentsInChildren<SimpleInteractable>(true);
        foreach (var interactable in interactables)
        {
            if (interactable.TryGetComponent<Collider>(out var col))
            {
                col.enabled = isEnabled;
            }
        }
    }

    private void SetPuzzleInteractableColliderEnabled(bool isEnabled)
    {
        var puzzleInteractables = GetComponentsInChildren<PuzzleInteractable>(true);
        foreach (var pi in puzzleInteractables)
        {
            if (pi.TryGetComponent<Collider>(out var col))
            {
                col.enabled = isEnabled;
            }
        }
    }

    private void SetCursorState(bool visible)
    {
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;
    }

    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend  = _brain.DefaultBlend;
        blend.Time = duration;
        _brain.DefaultBlend = blend;
    }

    private IEnumerator RestoreBlendAfterTransition()
    {
        yield return null;
        while (_brain != null && _brain.IsBlending) yield return null;
        SetBlendDuration(_originalBlendTime);
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
