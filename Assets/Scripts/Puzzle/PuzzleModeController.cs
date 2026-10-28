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

    [Header("Solved State")]
    [Tooltip("If true, the puzzle can still be entered after it has been solved. Use for puzzles " +
             "that keep a useful interactive element after solving (e.g. the paint puzzle TV whose " +
             "camera switch contains hints for the next puzzle). Puzzle buttons stay locked — only " +
             "unlocked elements like the TV switch remain clickable.")]
    [SerializeField] private bool _allowEnterWhenSolved = false;

    /// <summary>True if the puzzle may be re-entered after being solved.</summary>
    public bool AllowEnterWhenSolved => _allowEnterWhenSolved;

    [Header("Flashlight")]
    [Tooltip("If true, the flashlight is forced off while in puzzle mode and cannot be toggled. " +
             "Original state is restored on exit.")]
    [SerializeField] private bool _disableFlashlightInPuzzle = true;

    [Tooltip("Lights activated during puzzle mode to compensate for the disabled flashlight. " +
             "Only activated if the flashlight was on when entering the puzzle. " +
             "Deactivated on exit.")]
    [SerializeField] private Light[] _flashlightSupportLights;

    [Header("Power")]
    [Tooltip("If true, the puzzle cannot be entered while master power is off. " +
             "A popup hint is shown to the player instead.")]
    [SerializeField] private bool _requirePower = false;

    [Tooltip("Hint message shown when the player tries to enter the puzzle without power.")]
    [SerializeField] private string _noPowerHint = "Нужно включить свет";

    [Header("Events")]
    [Tooltip("Fired when the player enters puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeEntered;

    [Tooltip("Fired when the player exits puzzle mode.")]
    [SerializeField] private UnityEvent OnPuzzleModeExited;

    [Tooltip("Fired when the puzzle is solved.")]
    [SerializeField] private UnityEvent OnPuzzleSolved;

    [Tooltip("Fired when a solved state is restored from a save on load. Use this to restore the " +
             "scene instantly (no delays), e.g. DelayedActionSequence.RunInstant(), so delayed " +
             "transitions and physics effects from the live solve do not replay on load.")]
    [SerializeField] private UnityEvent OnPuzzleSolvedRestored;

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

        // If puzzle mode is not active, ExitPuzzleMode will never run — the main
        // interactable collider must be disabled right here so it doesn't block
        // raycasts to items revealed by the solve (e.g. the medallion in the box).
        // Collider stays enabled ONLY when re-entry after solving is allowed.
        if (!_isActive)
            SetPuzzleInteractableColliderEnabled(_allowEnterWhenSolved);

        FireSolvedEvents();
    }

    /// <summary>
    /// Fires solve events, exits puzzle mode if active, and persists state.
    /// </summary>
    private void FireSolvedEvents()
    {
        OnPuzzleSolved?.Invoke();
        OnSolved?.Invoke();
        ProgressLogger.Log("puzzle_solved", _saveId);

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

        // Fire the restore UnityEvent (for visuals/UI wired in the Inspector).
        // The live-solve event OnPuzzleSolved is intentionally NOT raised here so that
        // delayed sequences, audio, animation, and movement do not trigger again on
        // load. Handlers that must restore scene state instantly should be wired to
        // OnPuzzleSolvedRestored. Each system should also read IsSolved in its own
        // Start() to restore its visual state silently.
        if (_isSolved)
        {
            OnPuzzleSolvedRestored?.Invoke();
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

        // Always normalize the interactable collider state on start: enabled for an
        // unsolved puzzle; for a solved one — enabled only when re-entry is allowed.
        // This also heals a collider left disabled in the saved scene (e.g. by an
        // Edit Mode tool run that persisted the runtime toggling).
        // NOTE: this must run in Start, not Awake — SaveManager distributes
        // loaded data in its Start(), which runs after every Awake() but before this.
        SetPuzzleInteractableColliderEnabled(!_isSolved || _allowEnterWhenSolved);
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
        if (_isActive) return;
        if (_isSolved && !_allowEnterWhenSolved) return;

        if (_requirePower && LightingSystem.Instance != null && !LightingSystem.Instance.IsPowered)
        {
            PopupMessageSystem.Instance?.Show(_noPowerHint, PopupMessageType.Hint, 3f);
            return;
        }

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

        // Unlock the mouse and show the puzzle cursor — the player clicks the puzzle's
        // physical buttons (PuzzleInteractable layer) with the mouse, like in other puzzles.
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

        PuzzleModeStarted?.Invoke();
    }

    /// <summary>Raised when the player enters any puzzle mode (started solving).</summary>
    public static event Action PuzzleModeStarted;

    /// <summary>Raised when the player leaves a puzzle mode (solved or gave up).</summary>
    public static event Action PuzzleModeEnded;

    /// <summary>
    /// Deactivates the puzzle mode: restores camera, player input, and hides cursor.
    /// </summary>
    public void ExitPuzzleMode()
    {
        if (!_isActive) return;

        _isActive = false;
        if (!_isSolved)
            ProgressLogger.Log("puzzle_exit_unsolved", _saveId);

        PuzzleModeEnded?.Invoke();
        RestoreLensDistortion();

        // Disable child interactables when exiting puzzle mode.
        SetChildInteractablesEnabled(false);

        // Re-enable main puzzle interactable collider — unless the puzzle is
        // solved and re-entry is not allowed, in which case it must stay off so
        // it doesn't block raycasts to items revealed by the open animation.
        SetPuzzleInteractableColliderEnabled(_allowEnterWhenSolved || !_isSolved);

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

        // Restore FPS cursor state and hide the puzzle cursor.
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
        // Same empty-cache guard as SetPuzzleInteractableColliderEnabled.
        if (_cachedChildInteractables == null || _cachedChildInteractables.Length == 0)
            _cachedChildInteractables = GetComponentsInChildren<SimpleInteractable>(true);

        if (_cachedChildInteractables == null) return;
        foreach (var interactable in _cachedChildInteractables)
        {
            if (interactable == null) continue;
            if (interactable.TryGetComponent<Collider>(out var col))
            {
                col.enabled = isEnabled;
            }
        }
    }

    private void SetPuzzleInteractableColliderEnabled(bool isEnabled)
    {
        // Rebuild the cache when it's empty: Awake can run before the room/prefab
        // hierarchy is fully instantiated (e.g. room visibility activation), leaving
        // zero cached interactables and silently skipping the collider toggle.
        if (_cachedPuzzleInteractables == null || _cachedPuzzleInteractables.Length == 0)
            _cachedPuzzleInteractables = GetComponentsInChildren<PuzzleInteractable>(true);

        if (_cachedPuzzleInteractables == null) return;
        foreach (var pi in _cachedPuzzleInteractables)
        {
            if (pi == null) continue;
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

        // A deferred result (crafting or device output) shown in the inspection panel
        // exists only there — its pickup callback is what returns it to the inventory.
        // Block puzzle exit while such an item is on screen, otherwise Esc would
        // destroy the item (the inspector alone no longer loses it, but the puzzle
        // would close over the panel and desync the inventory bar).
        if (ItemInspector.Instance != null && ItemInspector.Instance.IsInspecting &&
            ItemInspector.Instance.HoldsDeferredResult)
        {
            return;
        }

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
