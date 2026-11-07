using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Plays a cinematic sequence when a puzzle is solved:
/// disables player input, locks cursor, triggers an animation clip,
/// and lets Animation Events in that clip control camera transitions
/// and cinematic completion.
/// </summary>
public class PuzzleSolvedCinematic : MonoBehaviour, ISaveable
{
    // ── Constants ───────────────────────────────────────────────────────────────

    private const int CinematicCameraPriority = 3000;

    // If PlayCinematic is called within this many frames of Awake, treat it as
    // a load-time restoration rather than a real first-time solve. This covers
    // old save files that don't contain the cinematic's wasAlreadySolved entry.
    private const int LoadWindowFrames = 120;

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Save")]
    [Tooltip("Unique save ID. Must be different from the puzzle manager's SaveId.")]
    [SerializeField] private string _saveId = "cinematic_unique_id";

    [Header("Camera")]
    [Tooltip("CinemachineCamera used for the cinematic shot. Must start inactive in the hierarchy.")]
    [SerializeField] private CinemachineCamera _cinematicCamera;

    [Header("Animation")]
    [Tooltip("Animator that plays the cinematic animation. Auto-found on the same GameObject if not assigned.")]
    [SerializeField] private Animator _animator;

    [Tooltip("Trigger parameter name that starts the cinematic animation.")]
    [SerializeField] private string _animationTrigger = "PlayCinematic";

    [Tooltip("Bool parameter name that transitions the Animator to the solved pose. Set together with the cinematic trigger when the puzzle is solved, or set alone on load.")]
    [SerializeField] private string _puzzleSolvedParam = "PuzzleSolved";

    [Header("Audio")]
    [Tooltip("Controls all audio for this cinematic. Auto-found on the same GameObject if not assigned.")]
    [SerializeField] private CinematicAudioController _audioController;

    [Header("Fade")]
    [Tooltip("Duration of the screen fade to/from black.")]
    [SerializeField, Min(0f)] private float _fadeDuration = 1f;

    [Header("Flashlight")]
    [Tooltip("Zone Id of the room this cinematic takes place in. Empty = the flashlight is not used in this cinematic.")]
    [SerializeField] private string _lightZoneId;

    // ── State ───────────────────────────────────────────────────────────────────

    private CinemachineBrain _brain;
    private float _originalBlendTime;
    private bool _isPlaying;
    private bool _wasAlreadySolved;
    private int _awakeFrame;
    private FlashlightLagFollow _flashlightFollow;
    private Transform _originalFollowTarget;
    private Transform _originalProximityOrigin;
    private bool _flashlightWasOn;

    /// <summary>
    /// True while any PuzzleSolvedCinematic is currently playing.
    /// UI systems check this before restoring the interaction HUD,
    /// so a panel/modal closing mid-cinematic doesn't re-show the crosshair.
    /// </summary>
    public static bool IsAnyCinematicPlaying { get; private set; }

    // ── Unity Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_cinematicCamera != null)
            _cinematicCamera.gameObject.SetActive(false);

        if (_animator == null)
            _animator = GetComponent<Animator>();

        if (_audioController == null)
            _audioController = GetComponent<CinematicAudioController>();

        _brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();

        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;

        _awakeFrame = Time.frameCount;

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);

        if (!_isPlaying) return;

        // Emergency cleanup — restore everything immediately.
        RestoreFlashlightAfterCinematic();

        if (_cinematicCamera != null)
        {
            _cinematicCamera.Priority = 0;
            _cinematicCamera.gameObject.SetActive(false);
        }
        SetBlendDuration(_originalBlendTime);
        InputManager.Instance?.SetPlayerInputEnabled(true);

        if (ScreenFader.Instance != null)
        {
            ScreenFader.Instance.FadeOut(0f);
        }

        InteractionUI.Instance?.SetVisible(true);

        _isPlaying = false;
        IsAnyCinematicPlaying = false;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the cinematic sequence. Wire this to OnPuzzleSolved or call directly.
    /// The animation clip is expected to call OnCinematicCameraActivate,
    /// OnCinematicCameraDeactivate, OnFadeIn, OnFadeOut and OnCinematicEnd
    /// via Animation Events.
    /// Audio is handled by CinematicAudioController — call PlayByIndex
    /// or PlayByName from code or Animation Events.
    /// </summary>
    public void PlayCinematic()
    {
        if (_isPlaying) return;
        if (!gameObject.activeInHierarchy) return;

        StartCoroutine(PlayCinematicOrSnap());
    }

    private IEnumerator PlayCinematicOrSnap()
    {
        // Wait one frame so all ISaveable.LoadSaveData calls from SaveManager.Load()
        // have completed before checking _wasAlreadySolved. This ensures the flag
        // is set correctly regardless of the iteration order in SaveManager.
        yield return null;

        // Primary check: the flag was set by LoadSaveData (new saves).
        // Fallback: if called within the load window after Awake, this is a
        // restoration from an old save that lacks the cinematic's entry.
        bool isLoadTime = _wasAlreadySolved
                          || (Time.frameCount - _awakeFrame) < LoadWindowFrames;

        if (isLoadTime)
        {
            _wasAlreadySolved = true;
            SnapToSolvedState();
            yield break;
        }

        // Set the flag before playing so that any debounced save from the
        // puzzle manager's FireSolvedEvents() captures wasAlreadySolved = true.
        // The initial Save() call in FireSolvedEvents runs in the same frame as
        // PlayCinematic — before this coroutine executes — so it would otherwise
        // snapshot false. Calling Save() here merges the updated flag into the
        // pending snapshot.
        _wasAlreadySolved = true;
        SaveManager.Instance?.Save();

        _isPlaying = true;
        IsAnyCinematicPlaying = true;
        StartCoroutine(PlayCinematicRoutine());
    }

    private IEnumerator PlayCinematicRoutine()
    {
        InputManager.Instance?.SetPlayerInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        InteractionUI.Instance?.SetVisible(false);
        SetupFlashlightForCinematic();

        // ── Launch animation ────────────────────────────────────────────────────
        if (_animator != null)
        {
            _animator.SetTrigger(_animationTrigger);
            _animator.SetBool(_puzzleSolvedParam, true);
        }
        else
        {
            OnCinematicEnd();
            yield break;
        }

    }

    // ── Animation Event callbacks ───────────────────────────────────────────────

    /// <summary>
    /// Instantly transitions the Animator to the Puzzle Solved state
    /// without playing the cinematic. Call this on load when the puzzle is already solved.
    /// </summary>
    public void SnapToSolvedState()
    {
        if (_animator == null) return;
        _animator.SetBool(_puzzleSolvedParam, true);
    }

    /// <summary>
    /// Called from an Animation Event to activate the cinematic camera
    /// and blend away from the player camera.
    /// Pass blendDuration from the Animation Event.
    /// </summary>
    public void OnCinematicCameraActivate(float blendDuration = 0f)
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(blendDuration);
        _cinematicCamera.Priority = CinematicCameraPriority;
        _cinematicCamera.gameObject.SetActive(true);
    }

    /// <summary>
    /// Locks the flashlight input and redirects its beam to follow the cinematic camera
    /// when the room's lights are off, so the shot is lit by the player's flashlight.
    /// </summary>
    private void SetupFlashlightForCinematic()
    {
        if (string.IsNullOrEmpty(_lightZoneId) || LightingSystem.Instance == null
            || LightingSystem.Instance.IsZoneLit(_lightZoneId))
            return;

        var flash = FlashlightController.Instance;
        if (flash != null)
        {
            flash.IsLocked = true;
            _flashlightWasOn = flash.IsOn;
            if (!_flashlightWasOn) flash.ForceOn();

            // Dimming must measure from the cinematic camera — the player's frozen
            // camera would dim the beam based on geometry around the player instead.
            _originalProximityOrigin = flash.ProximityRayOrigin;
            flash.SetProximityRayOrigin(_cinematicCamera != null ? _cinematicCamera.transform : null);
        }

        if (_cinematicCamera == null) return;
        _flashlightFollow = FindFirstObjectByType<FlashlightLagFollow>();
        if (_flashlightFollow != null)
        {
            _originalFollowTarget = _flashlightFollow.FollowTarget;
            _flashlightFollow.SetFollowTarget(_cinematicCamera.transform);
        }
    }

    /// <summary>Restores the flashlight's follow target, input lock, and on/off state after the cinematic.</summary>
    private void RestoreFlashlightAfterCinematic()
    {
        var flash = FlashlightController.Instance;
        if (flash != null)
        {
            flash.IsLocked = false;
            if (!_flashlightWasOn && flash.IsOn) flash.ForceOff();
            if (_originalProximityOrigin != null)
                flash.SetProximityRayOrigin(_originalProximityOrigin);
            _originalProximityOrigin = null;
        }

        if (_flashlightFollow != null && _originalFollowTarget != null)
            _flashlightFollow.SetFollowTarget(_originalFollowTarget);
        _flashlightFollow = null;
        _originalFollowTarget = null;
    }

    /// <summary>
    /// Called from an Animation Event to fade the screen to black.
    /// </summary>
    public void OnFadeIn()
    {
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeIn(_fadeDuration);
    }

    /// <summary>
    /// Called from an Animation Event to fade the screen from black to clear.
    /// </summary>
    public void OnFadeOut()
    {
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeOut(_fadeDuration);
    }

    /// <summary>
    /// Called from an Animation Event to deactivate the cinematic camera
    /// and blend back to the player camera.
    /// Pass blendDuration from the Animation Event.
    /// </summary>
    public void OnCinematicCameraDeactivate(float blendDuration = 0f)
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(blendDuration);
        _cinematicCamera.Priority = 0;
        StartCoroutine(WaitForBlendAndDeactivate());
    }

    private IEnumerator WaitForBlendAndDeactivate()
    {
        if (_brain != null)
        {
            yield return null;
            while (_brain.IsBlending)
                yield return null;
        }

        if (_cinematicCamera != null)
            _cinematicCamera.gameObject.SetActive(false);
    }

    /// <summary>
    /// Called from an Animation Event at the end of the cinematic clip
    /// to restore player input and original camera blend.
    /// </summary>
    public void OnCinematicEnd()
    {
        InputManager.Instance?.SetPlayerInputEnabled(true);
        InteractionUI.Instance?.SetVisible(true);
        RestoreFlashlightAfterCinematic();
        SetBlendDuration(_originalBlendTime);
        _isPlaying = false;
        IsAnyCinematicPlaying = false;
    }

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new CinematicSaveData { wasAlreadySolved = _wasAlreadySolved });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<CinematicSaveData>(json);
        _wasAlreadySolved = data.wasAlreadySolved;
    }

    [Serializable]
    private struct CinematicSaveData
    {
        public bool wasAlreadySolved;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend = _brain.DefaultBlend;
        blend.Time = duration;
        _brain.DefaultBlend = blend;
    }
}
