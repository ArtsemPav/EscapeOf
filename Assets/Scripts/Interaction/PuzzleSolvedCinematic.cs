using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Plays a cinematic sequence when a puzzle is solved:
/// disables player input, locks cursor, blends to a dedicated Cinemachine camera,
/// triggers an animation, then restores player control and camera.
/// </summary>
public class PuzzleSolvedCinematic : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────────────────

    private const int CinematicCameraPriority = 3000;
    private const float DefaultAnimationTimeout = 15f;

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

    [Tooltip("Animator state name to poll for animation completion.")]
    [SerializeField] private string _animationStateName = "Cinematic";

    [Tooltip("Maximum seconds to wait for the animation before forcing restore.")]
    [SerializeField, Min(0f)] private float _animationTimeout = DefaultAnimationTimeout;

    [Header("Audio")]
    [Tooltip("Optional sound played at the start of the cinematic.")]
    [SerializeField] private AudioClip _cinematicClip;
    [SerializeField, Range(0f, 1f)] private float _cinematicVolume = 1f;

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
        _isPlaying = false;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the cinematic sequence. Wire this to OnPuzzleSolved or call directly.
    /// </summary>
    public void PlayCinematic()
    {
        if (_isPlaying) return;
        if (!gameObject.activeInHierarchy) return;

        StartCoroutine(CinematicSequence());
    }

    // ── Cinematic Sequence ──────────────────────────────────────────────────────

    private IEnumerator CinematicSequence()
    {
        _isPlaying = true;

        // ── Phase 1: Seize control ──────────────────────────────────────────────
        InputManager.Instance?.SetPlayerInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (_cinematicClip != null)
            AudioManager.Instance?.PlaySFX(_cinematicClip, _cinematicVolume);

        // ── Phase 2: Blend to cinematic camera ──────────────────────────────────
        if (_cinematicCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _cinematicCamera.Priority = CinematicCameraPriority;
            _cinematicCamera.gameObject.SetActive(true);

            yield return null;
            while (_brain != null && _brain.IsBlending)
                yield return null;
        }

        // ── Phase 3: Play animation and wait for completion ─────────────────────
        if (_animator != null)
        {
            _animator.SetTrigger(_animationTrigger);
            yield return null;

            // Wait until the Animator enters the cinematic state (with timeout).
            float elapsed = 0f;
            while (_animator != null &&
                   !_animator.GetCurrentAnimatorStateInfo(0).IsName(_animationStateName) &&
                   elapsed < _animationTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Wait until the cinematic animation has fully played.
            while (_animator != null &&
                   _animator.GetCurrentAnimatorStateInfo(0).IsName(_animationStateName) &&
                   _animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            {
                yield return null;
            }
        }

        // ── Phase 4: Blend back to player camera ────────────────────────────────
        if (_cinematicCamera != null)
        {
            SetBlendDuration(_blendDuration);
            _cinematicCamera.Priority = 0;

            yield return null;
            while (_brain != null && _brain.IsBlending)
                yield return null;

            _cinematicCamera.gameObject.SetActive(false);
        }

        SetBlendDuration(_originalBlendTime);

        // ── Phase 5: Restore control ────────────────────────────────────────────
        InputManager.Instance?.SetPlayerInputEnabled(true);

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
