using System;
using System.Collections;
using ChemicalPuzzle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    [Header("UI")]
    [Tooltip("If true, the PuzzleInventoryBar will be shown when entering puzzle mode.")]
    [SerializeField] private bool _showInventoryBar = false;

    [Header("Flashlight")]
    [Tooltip("If true, the flashlight is forced off while in puzzle mode and cannot be toggled. " +
             "Original state is restored on exit.")]
    [SerializeField] private bool _disableFlashlightInPuzzle = true;

    [Tooltip("Lights activated during puzzle mode to compensate for the disabled flashlight. " +
             "Only activated if the flashlight was on when entering the puzzle. " +
             "Deactivated on exit.")]
    [SerializeField] private Light[] _flashlightSupportLights;

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

    private LensDistortion _lensDistortion;
    private bool           _wasLensDistortionActive;

    private bool _flashlightWasOn;
    private bool _flashlightStateSaved;

    private SimpleInteractable[] _cachedChildInteractables;
    private PuzzleInteractable[] _cachedPuzzleInteractables;

    // High enough to override any player camera (PlayerCamera uses 1000).
    private const int PuzzleCameraPriority = 2000;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the puzzle mode is currently active.</summary>
    public bool IsActive => _isActive;

    /// <summary>True if the puzzle has been solved.</summary>
    public bool IsSolved => _isSolved;

    /// <summary>
    /// Marks the puzzle as solved and fires solve events.
    /// </summary>
    public void SetSolved()
    {
        if (_isSolved) return;

        _isSolved = true;
        FireSolvedEvents();
    }

    /// <summary>
    /// Fires solve events, exits puzzle mode if active, and persists state.
    /// </summary>
    private void FireSolvedEvents()
    {
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

        // Fire only the UnityEvent (for visuals/UI wired in the Inspector).
        // The C# OnSolved event is intentionally NOT raised here so that
        // gameplay-side handlers (audio, animation, movement) do not trigger
        // again on load. Each system should read IsSolved in its own Start()
        // to restore its visual state silently.
        if (_isSolved)
        {
            OnPuzzleSolved?.Invoke();
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
        // Auto-find the puzzle camera in children if not assigned in the Inspector.
        if (_puzzleCamera == null)
            _puzzleCamera = GetComponentInChildren<CinemachineCamera>(includeInactive: true);

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

        // Cache child interactables for efficient enable/disable during puzzle mode transitions.
        _cachedChildInteractables = GetComponentsInChildren<SimpleInteractable>(true);
        _cachedPuzzleInteractables = GetComponentsInChildren<PuzzleInteractable>(true);

        // Ensure support lights are off until the puzzle is entered with flashlight on.
        SetSupportLightsEnabled(false);

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
        DisableLensDistortion();

        // Enable child interactables when entering puzzle mode (e.g. puzzle buttons/elements).
        SetChildInteractablesEnabled(true);

        // Disable main puzzle interactable collider to prevent raycast blocking or accidental re-triggering.
        SetPuzzleInteractableColliderEnabled(false);

        if (_puzzleCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _puzzleCamera.Priority = PuzzleCameraPriority;
            _puzzleCamera.gameObject.SetActive(true);

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(RestoreBlendAfterTransition());
            }
        }

        // Block FPS input and prevent GameManager from opening the pause menu on Esc.
        UIManager.Instance?.PushModalState();

        // Show cursor for puzzle interaction.
        SetCursorState(true);
        if (UI.PuzzleCursor.Instance != null)
        {
            UI.PuzzleCursor.Instance.Show();
        }

        // Force flashlight off and lock it while in puzzle mode.
        DisableFlashlight();

        if (_showInventoryBar)
        {
            var handler = GetComponentInChildren<IPuzzleDropHandler>();
            PuzzleInventoryBar.Instance?.Show(handler);
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
        RestoreLensDistortion();

        // Disable child interactables when exiting puzzle mode.
        SetChildInteractablesEnabled(false);

        // Re-enable main puzzle interactable collider.
        SetPuzzleInteractableColliderEnabled(true);

        if (_puzzleCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _puzzleCamera.Priority = 0;
            _puzzleCamera.gameObject.SetActive(false);

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(RestoreBlendAfterTransition());
            }
            else
            {
                // If the object is being destroyed or disabled, restore blend time immediately 
                // since we can't start a coroutine to wait for the transition.
                SetBlendDuration(_originalBlendTime);
            }
        }

        // Restore FPS input and decrement the modal panel counter.
        UIManager.Instance?.PopModalState();

        // Restore FPS cursor state.
        SetCursorState(false);
        if (UI.PuzzleCursor.Instance != null)
        {
            UI.PuzzleCursor.Instance.Hide();
        }

        // Restore flashlight to its pre-puzzle state.
        RestoreFlashlight();

        if (_showInventoryBar)
        {
            PuzzleInventoryBar.Instance?.Hide();
        }

        OnPuzzleModeExited?.Invoke();
        OnExited?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetChildInteractablesEnabled(bool isEnabled)
    {
        if (_cachedChildInteractables == null) return;
        foreach (var interactable in _cachedChildInteractables)
        {
            if (interactable.TryGetComponent<Collider>(out var col))
            {
                col.enabled = isEnabled;
            }
        }
    }

    private void SetPuzzleInteractableColliderEnabled(bool isEnabled)
    {
        if (_cachedPuzzleInteractables == null) return;
        foreach (var pi in _cachedPuzzleInteractables)
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

        // Check if any puzzle component blocks exiting (e.g. devices still processing).
        // This prevents the player from leaving while flasks are locked in devices.
        var exitGuards = GetComponentsInChildren<IPuzzleExitGuard>(true);
        foreach (var guard in exitGuards)
        {
            if (!guard.CanExitPuzzle())
            {
                // Optionally play a feedback sound or show a tooltip here.
                return;
            }
        }

        // Use a coroutine to ensure the input event finishes before changing state,
        // preventing the Pause menu from immediately opening.
        StartCoroutine(ExitNextFrameRoutine());
    }

    private IEnumerator ExitNextFrameRoutine()
    {
        yield return null;
        ExitPuzzleMode();
    }

    private void DisableLensDistortion()
    {
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        foreach (var v in volumes)
        {
            if (v.profile != null && v.profile.TryGet<LensDistortion>(out var ld))
            {
                if (ld.active)
                {
                    _lensDistortion = ld;
                    _wasLensDistortionActive = true;
                    ld.active = false;
                }
            }
        }
    }

    private void RestoreLensDistortion()
    {
        if (_lensDistortion != null && _wasLensDistortionActive)
        {
            _lensDistortion.active = true;
            _lensDistortion = null;
            _wasLensDistortionActive = false;
        }
    }

    // ── Flashlight ────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the current flashlight state, forces it off, and locks toggling.
    /// Does nothing if _disableFlashlightInPuzzle is false or no FlashlightController exists.
    /// </summary>
    private void DisableFlashlight()
    {
        if (!_disableFlashlightInPuzzle) return;

        var fc = FlashlightController.Instance;
        if (fc == null) return;

        _flashlightWasOn    = fc.IsOn;
        _flashlightStateSaved = true;

        if (fc.IsOn)
        {
            fc.ForceOff();
            // Compensate for the lost flashlight with support lights.
            SetSupportLightsEnabled(true);
        }

        fc.IsLocked = true;
    }

    /// <summary>
    /// Unlocks the flashlight and restores it to the state it had before entering puzzle mode.
    /// </summary>
    private void RestoreFlashlight()
    {
        if (!_disableFlashlightInPuzzle) return;

        var fc = FlashlightController.Instance;
        if (fc == null) return;

        fc.IsLocked = false;

        if (_flashlightStateSaved && _flashlightWasOn)
            fc.ForceOn();

        _flashlightStateSaved = false;

        SetSupportLightsEnabled(false);
    }

    /// <summary>Enables or disables all flashlight support lights.</summary>
    private void SetSupportLightsEnabled(bool enabled)
    {
        if (_flashlightSupportLights == null) return;

        foreach (var light in _flashlightSupportLights)
        {
            if (light != null)
                light.enabled = enabled;
        }
    }
}
