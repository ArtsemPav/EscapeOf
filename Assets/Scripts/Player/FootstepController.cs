using UnityEngine;

/// <summary>
/// Plays footstep audio synchronized with the head-bob cycle.
/// Each full bob cycle (sin wave period = 2π) fires one footstep at the bottom of the swing.
/// </summary>
[RequireComponent(typeof(FPSController))]
public class FootstepController : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip   jumpClip;

    [Header("Volume")]
    [SerializeField] [Range(0f, 1f)] private float walkVolume   = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float runVolume    = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float crouchVolume = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float jumpVolume   = 0.6f;

    [Header("Sync")]
    [Tooltip("bobTimer value at which a step fires (bottom of the bob swing = π/2 + n*2π).")]
    [SerializeField] private float stepPhase = Mathf.PI * 0.5f;
    [Tooltip("Tolerance window around stepPhase to detect a crossing.")]
    [SerializeField] private float stepWindow = 0.25f;

    // Reflected bob timer from FPSController
    private FPSController _fps;
    private AudioSource _audioSource;

    private float _lastBobTimer;
    private bool  _stepFired;

    private const float MovingThreshold = 0.5f;

    private bool _subscribed;

    private void Awake()
    {
        _fps = GetComponent<FPSController>();

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 0f; // 2D — first-person sound
        _audioSource.loop         = false;
    }

    private void Start()
    {
        Subscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnJumpPerformed -= OnJump;
        _subscribed = false;
    }

    private void Subscribe()
    {
        if (_subscribed || InputManager.Instance == null) return;
        InputManager.Instance.OnJumpPerformed += OnJump;
        _subscribed = true;
    }

    private void OnJump()
    {
        // FPSController already guards against jumping in mid-air,
        // so no redundant IsGrounded check needed here.
        if (jumpClip != null)
            _audioSource.PlayOneShot(jumpClip, jumpVolume);
    }

    private void Update()
    {
        float bobTimer = _fps.BobTimer;
        bool  isMoving = _fps.IsGrounded && _fps.HorizontalSpeed > MovingThreshold;

        if (!isMoving)
        {
            _lastBobTimer = bobTimer;
            _stepFired    = false;
            return;
        }

        // Use half-cycle (π) so each foot fires once per bob swing — two steps per full cycle
        float halfCycle    = Mathf.PI;
        float cyclePos     = bobTimer % halfCycle;
        float lastCyclePos = _lastBobTimer % halfCycle;

        bool crossedPhase = !_stepFired &&
                            lastCyclePos < stepPhase &&
                            cyclePos     >= stepPhase &&
                            cyclePos     <  stepPhase + stepWindow;

        if (crossedPhase)
        {
            PlayStep();
            _stepFired = true;
        }

        // Reset fired flag once we've moved past the trigger window
        if (_stepFired && cyclePos > stepPhase + stepWindow)
            _stepFired = false;

        _lastBobTimer = bobTimer;
    }

    private void PlayStep()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        float volume = _fps.IsCrouching ? crouchVolume : _fps.IsRunning ? runVolume : walkVolume;

        _audioSource.PlayOneShot(clip, volume);
    }
}
