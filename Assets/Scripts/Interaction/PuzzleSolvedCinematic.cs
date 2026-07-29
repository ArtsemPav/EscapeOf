using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Plays a cinematic sequence when a puzzle is solved:
/// disables player input, locks cursor, triggers an animation clip,
/// and lets Animation Events in that clip control camera transitions
/// and cinematic completion.
/// </summary>
public class PuzzleSolvedCinematic : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────────────────

    private const int CinematicCameraPriority = 3000;

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("CinemachineCamera used for the cinematic shot. Must start inactive in the hierarchy.")]
    [SerializeField] private CinemachineCamera _cinematicCamera;

    [Tooltip("Duration of the blend when switching to and from the cinematic camera.")]
    [SerializeField, Min(0f)] private float _blendDuration = 1f;

    [Header("Animation")]
    [Tooltip("Animator that plays the cinematic animation. Auto-found on the same GameObject if not assigned.")]
    [SerializeField] private Animator _animator;

    [Tooltip("Trigger parameter name that starts the cinematic animation.")]
    [SerializeField] private string _animationTrigger = "PlayCinematic";

    [Header("Audio")]
    [Tooltip("Controls all audio for this cinematic. Auto-found on the same GameObject if not assigned.")]
    [SerializeField] private CinematicAudioController _audioController;

    [Header("Fade")]
    [Tooltip("Duration of the screen fade to/from black.")]
    [SerializeField, Min(0f)] private float _fadeDuration = 1f;

    // ── State ───────────────────────────────────────────────────────────────────

    private CinemachineBrain _brain;
    private float _originalBlendTime;
    private bool _isPlaying;

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
    }

    private void OnDestroy()
    {
        if (!_isPlaying) return;

        // Emergency cleanup — restore everything immediately.
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

        _isPlaying = false;
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

        _isPlaying = true;
        StartCoroutine(PlayCinematicRoutine());
    }

    private IEnumerator PlayCinematicRoutine()
    {
        InputManager.Instance?.SetPlayerInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // ── Launch animation ────────────────────────────────────────────────────
        if (_animator != null)
        {
            _animator.SetTrigger(_animationTrigger);
        }
        else
        {
            OnCinematicEnd();
            yield break;
        }

    }

    // ── Animation Event callbacks ───────────────────────────────────────────────

    /// <summary>
    /// Called from an Animation Event to activate the cinematic camera
    /// and blend away from the player camera.
    /// Pass blendDuration from the Animation Event; uses _blendDuration if 0.
    /// </summary>
    public void OnCinematicCameraActivate(float blendDuration = 0f)
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(blendDuration > 0f ? blendDuration : _blendDuration);
        _cinematicCamera.Priority = CinematicCameraPriority;
        _cinematicCamera.gameObject.SetActive(true);
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
    /// Pass blendDuration from the Animation Event; uses _blendDuration if 0.
    /// </summary>
    public void OnCinematicCameraDeactivate(float blendDuration = 0f)
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(blendDuration > 0f ? blendDuration : _blendDuration);
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
        SetBlendDuration(_originalBlendTime);
        _isPlaying = false;
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
