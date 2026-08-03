using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays footstep audio synchronized with the head-bob cycle.
/// Each full bob cycle (sin wave period = 2π) fires one footstep at the bottom of the swing.
/// Clips are selected from a FootstepProfile — either the default profile or the
/// highest-priority active FootstepZone the player is currently inside.
/// Left and right foot clips alternate per step.
/// </summary>
[RequireComponent(typeof(FPSController))]
public class FootstepController : MonoBehaviour
{
    [Header("Default Profile")]
    [Tooltip("Footstep profile used when the player is not inside any FootstepZone.")]
    [SerializeField] private FootstepProfile defaultProfile;

    [Header("Jump")]
    [SerializeField] private AudioClip jumpClip;

    [Header("Volume")]
    [SerializeField] [Range(0f, 1f)] private float walkVolume   = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float runVolume    = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float crouchVolume = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float jumpVolume   = 0.6f;

    [Header("Sync")]
    [Tooltip("bobTimer value at which a step fires. 3π/4 ≈ 2.356 = bottom of the bob (foot lands). Adjust to fine-tune sync with the visual bob.")]
    [SerializeField] private float stepPhase = Mathf.PI * 0.75f;
    [Tooltip("Tolerance window around stepPhase to detect a crossing.")]
    [SerializeField] private float stepWindow = 0.25f;

    [Header("Crossfade")]
    [Tooltip("Duration in seconds to crossfade between footstep profiles when the surface changes.")]
    [SerializeField] private float transitionDuration = 0.5f;

    // Reflected bob timer from FPSController
    private FPSController _fps;
    private AudioSource[] _audioPool;
    private int _poolIndex;

    private const int PoolSize = 4;

    private float _lastBobTimer;
    private bool  _stepFired;

    // Alternates between left (even) and right (odd) foot
    private int _stepCount;

    // Active zones ordered by entry — ResolveCurrentProfile picks the highest priority
    private readonly List<FootstepZone> _activeZones = new();

    // Crossfade state
    private FootstepProfile _currentProfile;
    private FootstepProfile _previousProfile;
    private float _transitionTimer;
    private bool  _isTransitioning;

    private const float MovingThreshold = 0.5f;

    private bool _subscribed;

    private void Awake()
    {
        _fps = GetComponent<FPSController>();

        _audioPool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            _audioPool[i] = gameObject.AddComponent<AudioSource>();
            _audioPool[i].playOnAwake  = false;
            _audioPool[i].spatialBlend = 0f; // 2D — first-person sound
            _audioPool[i].loop         = false;
        }
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
            PlayClip(jumpClip, jumpVolume, 0f);
    }

    private void Update()
    {
        UpdateProfileTransition();

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

    /// <summary>Adds a zone to the active set. Called by FootstepZone.OnTriggerEnter.</summary>
    public void RegisterZone(FootstepZone zone)
    {
        if (zone == null || _activeZones.Contains(zone)) return;
        _activeZones.Add(zone);
    }

    /// <summary>Removes a zone from the active set. Called by FootstepZone.OnTriggerExit.</summary>
    public void UnregisterZone(FootstepZone zone)
    {
        if (zone == null) return;
        _activeZones.Remove(zone);
    }

    /// <summary>
    /// Returns the highest-priority active zone's profile, or the default profile
    /// when no zones are active. Ties are resolved by most recently entered.
    /// </summary>
    private FootstepProfile ResolveCurrentProfile()
    {
        if (_activeZones.Count == 0)
            return defaultProfile;

        FootstepZone best = null;
        for (int i = 0; i < _activeZones.Count; i++)
        {
            var z = _activeZones[i];
            if (best == null || z.Priority >= best.Priority)
                best = z;
        }
        return best != null ? best.Profile : defaultProfile;
    }

    /// <summary>
    /// Detects profile changes and advances the crossfade timer.
    /// Called every frame from Update.
    /// </summary>
    private void UpdateProfileTransition()
    {
        var resolved = ResolveCurrentProfile();

        if (resolved != _currentProfile)
        {
            // Crossfade only makes sense between two Replace-mode profiles.
            // Additive mode layers on top of default, so switching to/from it
            // just adds or removes the extra layer — no fade needed.
            bool canCrossfade = _currentProfile != null && resolved != null &&
                                _currentProfile.Mode  == FootstepProfile.BlendMode.Replace &&
                                resolved.Mode         == FootstepProfile.BlendMode.Replace;

            if (canCrossfade)
            {
                _previousProfile = _currentProfile;
                _isTransitioning  = true;
            }
            else
            {
                _previousProfile = null;
                _isTransitioning  = false;
            }

            _currentProfile   = resolved;
            _transitionTimer  = 0f;
        }

        if (_isTransitioning)
        {
            _transitionTimer += Time.deltaTime;
            if (_transitionTimer >= transitionDuration)
            {
                _isTransitioning  = false;
                _previousProfile  = null;
            }
        }
    }

    private void PlayStep()
    {
        if (_currentProfile == null) return;

        bool  isLeft      = (_stepCount % 2) == 0;
        float baseVolume  = _fps.IsCrouching ? crouchVolume : _fps.IsRunning ? runVolume : walkVolume;

        if (_currentProfile.Mode == FootstepProfile.BlendMode.Additive)
        {
            // Default footsteps always play; zone sounds layer on top
            PlayClipFromProfile(defaultProfile, isLeft, baseVolume);
            PlayClipFromProfile(_currentProfile, isLeft, baseVolume);
        }
        else if (_isTransitioning && _previousProfile != null)
        {
            // Equal-power crossfade (Replace mode only)
            float t           = Mathf.Clamp01(_transitionTimer / transitionDuration);
            float currentAmt  = Mathf.Sqrt(t);
            float previousAmt = Mathf.Sqrt(1f - t);
            PlayClipFromProfile(_currentProfile,  isLeft, baseVolume * currentAmt);
            PlayClipFromProfile(_previousProfile, isLeft, baseVolume * previousAmt);
        }
        else
        {
            PlayClipFromProfile(_currentProfile, isLeft, baseVolume);
        }

        _stepCount++;
    }

    /// <summary>Plays a clip through the AudioSource pool, skipping startOffset seconds of silence.</summary>
    private void PlayClipFromProfile(FootstepProfile profile, bool isLeft, float volume)
    {
        if (profile == null || volume <= 0f) return;

        AudioClip clip = profile.GetRandomClip(isLeft);
        if (clip == null)
        {
            clip = profile.GetRandomClip(!isLeft);
            if (clip == null && profile != defaultProfile && defaultProfile != null)
                clip = defaultProfile.GetRandomClip(isLeft);
        }
        if (clip == null) return;

        PlayClip(clip, volume * profile.VolumeMultiplier, profile.StartOffset);
    }

    /// <summary>Plays an audio clip on the next available AudioSource in the pool.</summary>
    private void PlayClip(AudioClip clip, float volume, float startOffset)
    {
        AudioSource source = _audioPool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % PoolSize;

        source.clip   = clip;
        source.volume = Mathf.Clamp01(volume);
        source.time   = startOffset;
        source.Play();
    }
}
