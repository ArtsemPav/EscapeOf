using System;
using System.Collections;
using ChemicalPuzzle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Multi-camera puzzle mode controller for the final door puzzle.
/// Manages an array of Cinemachine cameras, FPS input blocking, flashlight,
/// cursor, and Esc handling. Implements <see cref="IInteractable"/> directly
/// so the player can interact with any child collider on the Interactable layer
/// (FPSController resolves via GetComponentInParent).
///
/// Cameras are auto-discovered by name if the _cameras array is left empty:
///   "FinalDoorCameraAll"   → Overview (index 0)
///   "FinalDoorCameraLeft"  → LeftSide  (index 1)
///   "FinalDoorCameraRight" → RightSide (index 2)
///   "FinalDoorCameraSkull" → Skull     (index 3, optional — added later)
/// </summary>
public class FinalDoorPuzzleController : MonoBehaviour, ISaveable, IInteractable
{
    /// <summary>Camera slot indices within the _cameras array.</summary>
    public enum CameraId { Overview = 0, LeftSide = 1, RightSide = 2, Skull = 3 }

    [Header("Save")]
    [SerializeField] private string _saveId = "final_door_puzzle_mode";

    [Header("Cameras")]
    [Tooltip("Assign in order: 0=Overview, 1=LeftSide, 2=RightSide, 3=Skull (optional). " +
             "Auto-discovered by name if empty.")]
    [SerializeField] private CinemachineCamera[] _cameras;

    [Tooltip("Which camera to activate when entering puzzle mode.")]
    [SerializeField] private CameraId _entryCamera = CameraId.LeftSide;

    [Tooltip("Blend duration between cameras (seconds).")]
    [SerializeField, Min(0f)] private float _blendDuration = 0.75f;

    [Header("UI")]
    [Tooltip("If true, the PuzzleInventoryBar will be shown when entering puzzle mode.")]
    [SerializeField] private bool _showInventoryBar = true;

    [Header("Flashlight")]
    [Tooltip("If true, the flashlight is forced off while in puzzle mode and cannot be toggled. " +
             "Original state is restored on exit.")]
    [SerializeField] private bool _disableFlashlightInPuzzle = true;

    [Tooltip("Lights activated during puzzle mode to compensate for the disabled flashlight.")]
    [SerializeField] private Light[] _flashlightSupportLights;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Осмотреть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    [Tooltip("If true, shows a popup hint when entering puzzle mode.")]
    [SerializeField] private bool _useExitPopup = true;
    [SerializeField] private string _exitPopupText = "Выход: Esc";
    [SerializeField] private PopupMessageType _exitPopupType = PopupMessageType.Warning;

    [Header("Events")]
    [SerializeField] private UnityEvent OnPuzzleModeEntered;
    [SerializeField] private UnityEvent OnPuzzleModeExited;
    [SerializeField] private UnityEvent OnPuzzleSolved;

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
    private CameraId _currentCamera;

    private CinemachineBrain _brain;
    private float _originalBlendTime;

    private LensDistortion _lensDistortion;
    private bool _wasLensDistortionActive;

    private bool _flashlightWasOn;
    private bool _flashlightStateSaved;

    private Collider _ownCollider;

    // High enough to override any player camera (PlayerCamera uses 1000).
    private const int PuzzleCameraPriority = 2000;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the puzzle mode is currently active.</summary>
    public bool IsActive => _isActive;

    /// <summary>True if the puzzle has been solved.</summary>
    public bool IsSolved => _isSolved;

    /// <summary>The camera that is currently active.</summary>
    public CameraId CurrentCamera => _currentCamera;

    /// <summary>
    /// Switches to the specified camera with a Cinemachine blend.
    /// Silently ignores null camera slots (e.g. Skull before it is assigned).
    /// </summary>
    public void SwitchCamera(CameraId id)
    {
        int index = (int)id;
        if (_cameras == null || index < 0 || index >= _cameras.Length || _cameras[index] == null) return;

        // Deactivate previous camera.
        int prev = (int)_currentCamera;
        if (prev >= 0 && prev < _cameras.Length && _cameras[prev] != null)
            _cameras[prev].Priority = 0;

        // Activate new camera.
        _cameras[index].Priority = PuzzleCameraPriority;
        _currentCamera = id;

        if (_brain != null && gameObject.activeInHierarchy)
        {
            SetBlendDuration(_blendDuration);
            StartCoroutine(RestoreBlendAfterTransition());
        }
    }

    /// <summary>Marks the puzzle as solved and fires solve events.</summary>
    public void SetSolved()
    {
        if (_isSolved) return;
        _isSolved = true;
        FireSolvedEvents();
    }

    private void FireSolvedEvents()
    {
        OnPuzzleSolved?.Invoke();
        OnSolved?.Invoke();

        if (_isActive)
            ExitPuzzleMode();

        SaveManager.Instance?.Save();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => !_isActive && !_isSolved;

    public void Interact()
    {
        if (CanInteract())
        {
            EnterPuzzleMode();
            ShowEntryHint();
        }
    }

    public string GetInteractText() => _isSolved ? string.Empty : _interactText;
    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => _isSolved ? CrosshairMode.Default : _crosshairMode;

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
            OnPuzzleSolved?.Invoke();
    }

    [Serializable]
    private struct SaveData { public bool isSolved; }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Auto-discover cameras by name if not assigned.
        if (_cameras == null || _cameras.Length == 0 || System.Array.TrueForAll(_cameras, c => c == null))
            AutoDiscoverCameras();

        // Deactivate all cameras on startup.
        if (_cameras != null)
        {
            foreach (var cam in _cameras)
            {
                if (cam != null)
                    cam.gameObject.SetActive(false);
            }
        }

        _brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();
        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;

        _ownCollider = GetComponent<Collider>();

        SetSupportLightsEnabled(false);
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        SubscribeToInput();
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
    }

    private void OnEnable()
    {
        if (!_isSubscribed && InputManager.Instance != null)
            SubscribeToInput();
    }

    private void OnDestroy()
    {
        if (_isActive)
            ExitPuzzleMode();
        UnsubscribeFromInput();
        SaveManager.Instance?.Unregister(this);
    }

    // ── Puzzle Mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates puzzle mode: enables cameras, blocks player input, shows cursor.
    /// </summary>
    public void EnterPuzzleMode()
    {
        if (_isActive || _isSolved) return;

        _isActive = true;
        DisableLensDistortion();

        // Disable own collider so FPSController doesn't re-detect the door.
        if (_ownCollider != null)
            _ownCollider.enabled = false;

        // Activate all cameras and set entry camera as active.
        if (_cameras != null)
        {
            SetBlendDuration(_blendDuration);
            int entryIndex = (int)_entryCamera;
            _currentCamera = _entryCamera;

            for (int i = 0; i < _cameras.Length; i++)
            {
                if (_cameras[i] == null) continue;
                _cameras[i].gameObject.SetActive(true);
                _cameras[i].Priority = (i == entryIndex) ? PuzzleCameraPriority : 0;
            }

            if (gameObject.activeInHierarchy)
                StartCoroutine(RestoreBlendAfterTransition());
        }

        UIManager.Instance?.PushModalState();
        SetCursorState(true);
        if (UI.PuzzleCursor.Instance != null)
            UI.PuzzleCursor.Instance.Show();

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
    /// Deactivates puzzle mode: restores camera, player input, hides cursor.
    /// </summary>
    public void ExitPuzzleMode()
    {
        if (!_isActive) return;

        _isActive = false;
        RestoreLensDistortion();

        if (_ownCollider != null)
            _ownCollider.enabled = true;

        if (_cameras != null)
        {
            SetBlendDuration(_blendDuration);
            foreach (var cam in _cameras)
            {
                if (cam == null) continue;
                cam.Priority = 0;
                cam.gameObject.SetActive(false);
            }

            if (gameObject.activeInHierarchy)
                StartCoroutine(RestoreBlendAfterTransition());
            else
                SetBlendDuration(_originalBlendTime);
        }

        UIManager.Instance?.PopModalState();
        SetCursorState(false);
        if (UI.PuzzleCursor.Instance != null)
            UI.PuzzleCursor.Instance.Hide();

        RestoreFlashlight();

        if (_showInventoryBar)
            PuzzleInventoryBar.Instance?.Hide();

        OnPuzzleModeExited?.Invoke();
        OnExited?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetCursorState(bool visible)
    {
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;
    }

    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend = _brain.DefaultBlend;
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

        // Check exit guards (e.g. activation sequence in progress).
        var exitGuards = GetComponentsInChildren<IPuzzleExitGuard>(true);
        foreach (var guard in exitGuards)
        {
            if (!guard.CanExitPuzzle())
                return;
        }

        StartCoroutine(ExitNextFrameRoutine());
    }

    private IEnumerator ExitNextFrameRoutine()
    {
        yield return null;
        ExitPuzzleMode();
    }

    private void ShowEntryHint()
    {
        if (_useExitPopup && !string.IsNullOrEmpty(_exitPopupText))
            PopupMessageSystem.Instance?.Show(_exitPopupText, _exitPopupType, 3f);
    }

    // ── Lens Distortion ────────────────────────────────────────────────────────

    private void DisableLensDistortion()
    {
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        foreach (var v in volumes)
        {
            if (v.profile != null && v.profile.TryGet<LensDistortion>(out var ld) && ld.active)
            {
                _lensDistortion = ld;
                _wasLensDistortionActive = true;
                ld.active = false;
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

    // ── Flashlight ─────────────────────────────────────────────────────────────

    private void DisableFlashlight()
    {
        if (!_disableFlashlightInPuzzle) return;
        var fc = FlashlightController.Instance;
        if (fc == null) return;

        _flashlightWasOn = fc.IsOn;
        _flashlightStateSaved = true;

        if (fc.IsOn)
        {
            fc.ForceOff();
            SetSupportLightsEnabled(true);
        }
        fc.IsLocked = true;
    }

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

    private void SetSupportLightsEnabled(bool enabled)
    {
        if (_flashlightSupportLights == null) return;
        foreach (var light in _flashlightSupportLights)
            if (light != null) light.enabled = enabled;
    }

    // ── Camera Auto-Discovery ──────────────────────────────────────────────────

    private void AutoDiscoverCameras()
    {
        var allCams = GetComponentsInChildren<CinemachineCamera>(true);
        if (allCams.Length == 0) return;

        _cameras = new CinemachineCamera[4]; // 4 slots: Overview, Left, Right, Skull

        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            string name = cam.gameObject.name;

            if (name.Contains("All") || name.Contains("Overview"))
                _cameras[0] = cam;
            else if (name.Contains("Left"))
                _cameras[1] = cam;
            else if (name.Contains("Right"))
                _cameras[2] = cam;
            else if (name.Contains("Skull"))
                _cameras[3] = cam;
        }
    }
}
