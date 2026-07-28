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
    [Tooltip("Optional sound played at the start of the cinematic.")]
    [SerializeField] private AudioClip _cinematicClip;
    [SerializeField, Range(0f, 1f)] private float _cinematicVolume = 1f;

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
    /// OnCinematicCameraDeactivate and OnCinematicEnd via Animation Events.
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
        // ── Fade to black before seizing control ────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);

        InputManager.Instance?.SetPlayerInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (_cinematicClip != null)
            AudioManager.Instance?.PlaySFX(_cinematicClip, _cinematicVolume);

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

        // ── Fade back from black after animator trigger is set ──────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
    }

    // ── Animation Event callbacks ───────────────────────────────────────────────

    /// <summary>
    /// Called from an Animation Event to activate the cinematic camera
    /// and blend away from the player camera.
    /// </summary>
    public void OnCinematicCameraActivate()
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(_blendDuration);
        _cinematicCamera.Priority = CinematicCameraPriority;
        _cinematicCamera.gameObject.SetActive(true);
    }

    /// <summary>
    /// Called from an Animation Event to deactivate the cinematic camera
    /// and blend back to the player camera.
    /// </summary>
    public void OnCinematicCameraDeactivate()
    {
        if (_cinematicCamera == null) return;

        SetBlendDuration(_blendDuration);
        _cinematicCamera.Priority = 0;
    }

    /// <summary>
    /// Called from an Animation Event at the end of the cinematic clip
    /// to fully restore player control and camera state.
    /// </summary>
    public void OnCinematicEnd()
    {
        StartCoroutine(EndRoutine());
    }

    private IEnumerator EndRoutine()
    {
        // ── Fade to black before restoring camera ───────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);

        if (_cinematicCamera != null)
            _cinematicCamera.gameObject.SetActive(false);

        SetBlendDuration(_originalBlendTime);
        InputManager.Instance?.SetPlayerInputEnabled(true);

        // ── Fade back from black ────────────────────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);

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
