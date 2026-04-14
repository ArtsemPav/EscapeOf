using System;
using UnityEngine;

/// <summary>
/// The final-stage lever for the electric puzzle (pCube17).
/// The lever becomes interactable only after ElectricPuzzleController signals that
/// all wires are correctly connected (<see cref="SetReady"/>).
/// Pulling the lever rotates it to the opposite position and raises <see cref="OnPulled"/>,
/// which ElectricPuzzleController subscribes to in order to mark the puzzle as fully solved.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ElectricLever : MonoBehaviour, IInteractable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Rotation")]
    [Tooltip("X-axis rotation delta added to the lever's original placement when pulled. " +
             "-180 flips a lever sitting at X=90 to X=-90.")]
    [SerializeField] private float _angleOnDelta = -180f;

    [Tooltip("Lerp speed toward the target rotation (higher = snappier).")]
    [SerializeField] private float _rotationSpeed = 5f;

    [Header("Audio")]
    [SerializeField] private AudioClip _pullClip;

    [SerializeField]
    [Range(0f, 1f)]
    private float _pullVolume = 0.8f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Дернуть рычаг";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised once when the lever completes its rotation.</summary>
    public event Action OnPulled;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _isPulled;  // one-shot: lever was already pulled
    private bool _isPulling; // true while animating toward pulled position, false while returning

    private float       _currentDelta;
    private float       _targetDelta;
    private Vector3     _baseEuler;
    private bool        _animating;
    private AudioSource _audioSource;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract()        => !_isPulled;
    public string GetInteractText()  => _interactText;
    public bool IsPickable()         => false;
    public bool UseLMBClick          => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    /// <summary>Starts the pull animation. One-shot; subsequent calls are ignored.</summary>
    public void Interact()
    {
        if (!CanInteract()) return;

        _isPulled    = true;
        _isPulling   = true;
        _targetDelta = _angleOnDelta;
        _animating   = true;

        if (_pullClip != null)
            _audioSource.PlayOneShot(_pullClip, _pullVolume);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores the pulled state instantly without animation or events.
    /// Used when loading a saved game where the lever was already pulled.
    /// </summary>
    public void SetPulledQuiet()
    {
        _isPulled     = true;
        _currentDelta = _angleOnDelta;
        _targetDelta  = _angleOnDelta;
        ApplyRotation(_currentDelta);
    }

    /// <summary>
    /// Resets the lever back to its unpulled position after a wrong pull.
    /// The lever becomes interactable again once the return animation completes.
    /// </summary>
    public void Reset()
    {
        _isPulled    = false;
        _isPulling   = false;
        _targetDelta = 0f;
        _animating   = true;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _baseEuler = transform.localEulerAngles;

        _audioSource              = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 1f;
        _audioSource.loop         = false;
    }

    private void Update()
    {
        if (!_animating) return;

        _currentDelta = Mathf.Lerp(_currentDelta, _targetDelta, _rotationSpeed * Time.deltaTime);

        if (Mathf.Abs(_currentDelta - _targetDelta) < 0.05f)
        {
            _currentDelta = _targetDelta;
            _animating    = false;

            // Fire only when completing a pull, never when returning after a wrong pull.
            if (_isPulling)
                OnPulled?.Invoke();
        }

        ApplyRotation(_currentDelta);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyRotation(float delta)
    {
        Vector3 euler = _baseEuler;
        euler.x += delta;
        transform.localEulerAngles = euler;
    }
}
