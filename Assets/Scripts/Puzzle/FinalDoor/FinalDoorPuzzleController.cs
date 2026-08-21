using System;
using System.Collections;
using System.Collections.Generic;
using ChemicalPuzzle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Multi-camera puzzle mode controller for the final door puzzle.
/// Each medallion statue has its own camera and interaction point.
/// When the player interacts with a statue, the camera cuts instantly
/// to that statue's close-up. The player inserts a medallion, presses Esc
/// to exit, and walks to the next statue.
///
/// When all medallions are correct, the interaction script calls
/// <see cref="SwitchToOverview"/> to play the activation sequence on
/// the overview camera.
///
/// Implements <see cref="IInteractable"/> as a fallback on the root —
/// normally each statue has its own <see cref="FinalDoorSideInteractable"/>
/// that takes priority via TryGetComponent.
/// </summary>
public class FinalDoorPuzzleController : MonoBehaviour, ISaveable, IInteractable
{
    [Header("Save")]
    [SerializeField] private string _saveId = "final_door_puzzle_mode";

    [Header("Cameras")]
    [Tooltip("Overview camera for the activation sequence. Auto-found by name " +
             "('All' or 'Overview') if not assigned.")]
    [SerializeField] private CinemachineCamera _overviewCamera;

    [Tooltip("All statue cameras. Auto-discovered from children if empty.")]
    [SerializeField] private CinemachineCamera[] _statueCameras;

    [Tooltip("Blend duration for transitions DURING puzzle mode (activation sequence, etc.).")]
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

    [Header("Interaction (root fallback)")]
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

    private CinemachineCamera _activeCamera;

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

    /// <summary>The camera that is currently active, or null if not in puzzle mode.</summary>
    public CinemachineCamera ActiveCamera => _activeCamera;

    /// <summary>
    /// Enters puzzle mode and instantly cuts to the specified camera.
    /// Pass null to use the overview camera as default.
    /// </summary>
    public void EnterPuzzleMode(CinemachineCamera entryCamera = null)
    {
        if (_isActive || _isSolved) return;

        _isActive = true;
        DisableLensDistortion();

        if (_ownCollider != null)
            _ownCollider.enabled = false;

        // Activate all cameras.
        var allCams = GetAllCameras();
        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            cam.gameObject.SetActive(true);
            cam.Priority = 0;
        }

        // Cut instantly to the entry camera.
        var target = entryCamera != null ? entryCamera : _overviewCamera;
        if (target != null)
        {
            SetBlendDuration(0f);
            target.Priority = PuzzleCameraPriority;
            _activeCamera = target;
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
    /// Exits puzzle mode — restores player camera, input, and cursor.
    /// </summary>
    public void ExitPuzzleMode()
    {
        if (!_isActive) return;

        _isActive = false;
        RestoreLensDistortion();

        if (_ownCollider != null)
            _ownCollider.enabled = true;

        var allCams = GetAllCameras();
        SetBlendDuration(_blendDuration);
        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            cam.Priority = 0;
            cam.gameObject.SetActive(false);
        }
        _activeCamera = null;

        if (gameObject.activeInHierarchy)
            StartCoroutine(RestoreBlendAfterTransition());
        else
            SetBlendDuration(_originalBlendTime);

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

    /// <summary>
    /// Smoothly blends to the specified camera during puzzle mode.
    /// Uses <see cref="_blendDuration"/>.
    /// </summary>
    public void SwitchCamera(CinemachineCamera camera)
    {
        if (camera == null) return;

        if (_activeCamera != null)
            _activeCamera.Priority = 0;

        SetBlendDuration(_blendDuration);
        camera.Priority = PuzzleCameraPriority;
        _activeCamera = camera;

        if (_brain != null && gameObject.activeInHierarchy)
            StartCoroutine(RestoreBlendAfterTransition());
    }

    /// <summary>Smoothly blends to the overview camera.</summary>
    public void SwitchToOverview()
    {
        if (_overviewCamera != null)
            SwitchCamera(_overviewCamera);
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

    // ── IInteractable (root fallback) ──────────────────────────────────────────

    public bool CanInteract() => !_isActive && !_isSolved;

    public void Interact()
    {
        if (CanInteract())
        {
            EnterPuzzleMode(null);
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
        // Auto-discover statue cameras from children if not assigned.
        if (_statueCameras == null || _statueCameras.Length == 0 ||
            System.Array.TrueForAll(_statueCameras, c => c == null))
        {
            AutoDiscoverStatueCameras();
        }

        // Auto-find overview camera by name if not assigned.
        if (_overviewCamera == null)
            AutoDiscoverOverviewCamera();

        // Deactivate all cameras on startup.
        foreach (var cam in GetAllCameras())
        {
            if (cam != null)
                cam.gameObject.SetActive(false);
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

    private void Start() => SubscribeToInput();
    private void OnDisable() => UnsubscribeFromInput();
    private void OnEnable()
    {
        if (!_isSubscribed && InputManager.Instance != null)
            SubscribeToInput();
    }

    private void OnDestroy()
    {
        if (_isActive) ExitPuzzleMode();
        UnsubscribeFromInput();
        SaveManager.Instance?.Unregister(this);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Returns all cameras (overview + statue cameras) as a single list.</summary>
    private List<CinemachineCamera> GetAllCameras()
    {
        var list = new List<CinemachineCamera>();
        if (_overviewCamera != null)
            list.Add(_overviewCamera);
        if (_statueCameras != null)
            list.AddRange(_statueCameras);
        return list;
    }

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

    private void AutoDiscoverStatueCameras()
    {
        var allCams = GetComponentsInChildren<CinemachineCamera>(true);
        if (allCams.Length == 0) return;

        var statueCams = new System.Collections.Generic.List<CinemachineCamera>();

        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            string name = cam.gameObject.name;

            // Overview camera is stored separately, not in _statueCameras.
            if (name.Contains("All") || name.Contains("Overview"))
            {
                _overviewCamera = cam;
                continue;
            }

            // Everything else is a statue camera.
            statueCams.Add(cam);
        }

        _statueCameras = statueCams.ToArray();
    }

    private void AutoDiscoverOverviewCamera()
    {
        var allCams = GetComponentsInChildren<CinemachineCamera>(true);
        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            string name = cam.gameObject.name;
            if (name.Contains("All") || name.Contains("Overview"))
            {
                _overviewCamera = cam;
                return;
            }
        }
    }
}
