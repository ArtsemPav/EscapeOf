using UnityEngine;

/// <summary>
/// Plays a looping 3D duck sound to lure the player when the duck is NOT visible.
/// Uses distance-based hearing range instead of a trigger zone:
///   - Player within hearing range AND duck not visible → sound fades in (lure).
///   - Duck visible (in frustum and unoccluded) → sound fades out (player found it).
///   - Player outside hearing range → sound stops.
///   - Master power OFF → sound stops entirely.
/// The lure is CONTINUOUSLY occlusion-aware: every check interval the direct
/// path from the duck to the player camera is raycast, and a muffle value
/// (0 = clear, 1 = fully muffled) blends smoothly toward its target. A blocked
/// path barely muffles the sound up close and muffles it progressively with
/// range — the farther the duck is behind walls/floors, the quieter and duller
/// the squeak, without any hard audible on/off steps.
/// Implements IPowerConsumer to receive power state from LightingSystem.
/// The AudioSource follows the duck's position so the sound stays spatially accurate.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DuckSoundZone : MonoBehaviour, IPowerConsumer
{
    private const float FadeDuration = 1.5f;
    private const float CheckInterval = 0.2f;
    private const float MuffleBlendDuration = 0.8f;
    private const float ClearCutoffHz = 20000f;

    [Header("Audio")]
    [Tooltip("Duck sound clip used to lure the player.")]
    [SerializeField] private AudioClip _duckClip;

    [Tooltip("Transform the sound follows (usually the RubberDuck). If empty, uses this transform.")]
    [SerializeField] private Transform _soundTarget;

    [Header("Hearing Range")]
    [Tooltip("Distance within which the duck sound plays at full volume.")]
    [SerializeField] private float _minDistance = 2f;

    [Tooltip("Maximum distance at which the duck sound can be heard. Beyond this, the sound stops entirely.")]
    [SerializeField] private float _maxDistance = 12f;

    [Header("Volume")]
    [Tooltip("Maximum volume of the duck sound.")]
    [SerializeField, Range(0f, 1f)] private float _volume = 0.7f;

    [Header("Occlusion (the farther, the quieter)")]
    [Tooltip("Distance up to which a blocked path barely muffles the lure: right next to the duck a wall hardly matters.")]
    [SerializeField, Min(0f)] private float _muffleStartDistance = 2f;

    [Tooltip("Distance at which a blocked path reaches its full muffle. Between start and this, the muffle ramps up smoothly with range.")]
    [SerializeField, Min(0.1f)] private float _muffleFullDistance = 6f;

    [Tooltip("Volume factor of the fully muffled lure (blocked path at/beyond Muffle Full Distance). 0.1 = barely audible through the floor.")]
    [SerializeField, Range(0f, 1f)] private float _farOccludedVolumeFactor = 0.1f;

    [Tooltip("Low-pass cutoff (Hz) of the fully muffled lure. Low value = a dull muffled squeak, as if through a thick floor.")]
    [SerializeField, Range(100f, 4000f)] private float _farOccludedCutoffHz = 600f;

    [Tooltip("Geometry within this radius of the duck is ignored by the occlusion raycast. " +
             "The duck sits inside the toilet bowl — without this the bowl itself would " +
             "permanently occlude the lure even for a player standing right next to it.")]
    [SerializeField, Min(0f)] private float _sourceIgnoreRadius = 0.5f;

    [Header("Visibility")]
    [Tooltip("If enabled, casts a ray from the camera to the duck to check for walls/obstacles blocking the view.")]
    [SerializeField] private bool _checkOcclusion = true;

    [Tooltip("Layer mask for occlusion raycast. Only these layers can block visibility.")]
    [SerializeField] private LayerMask _occlusionMask = ~0;

    private AudioSource _audioSource;
    private Renderer _duckRenderer;
    private Camera _playerCamera;
    private AudioLowPassFilter _lowPass;
    private Coroutine _checkRoutine;
    private bool _soundActive;
    private bool _isPowered;

    // Fade in/out progress: 0 = silent, 1 = lure volume. Blended every tick,
    // replacing start/stop coroutines so there is a single volume writer.
    private float _fade;

    // Continuous muffle state, 0 = clear path, 1 = fully muffled (blocked far
    // path). Blended toward its target each tick so stepping behind a wall
    // changes the sound gradually instead of snapping.
    private float _muffleMix;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;

        if (_soundTarget == null)
            _soundTarget = transform;

        _duckRenderer = _soundTarget.GetComponentInChildren<Renderer>();
    }

    private void OnEnable()
    {
        EnsureAudioSource();
        LightingSystem.Instance?.RegisterConsumer(this);
        _checkRoutine = StartCoroutine(DistanceCheckLoop());
    }

    private void OnDisable()
    {
        StopSoundImmediate();
        StopCheckRoutine();
        LightingSystem.Instance?.UnregisterConsumer(this);
    }

    /// <summary>
    /// IPowerConsumer: called by LightingSystem when master power changes,
    /// and once immediately on registration with the current state.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        _isPowered = isPowered;
    }

    /// <summary>Creates and configures the AudioSource if it doesn't exist.</summary>
    private void EnsureAudioSource()
    {
        if (_audioSource != null) return;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.clip = _duckClip;
        _audioSource.spatialBlend = 1f;
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.minDistance = _minDistance;
        _audioSource.maxDistance = _maxDistance;
        _audioSource.dopplerLevel = 0f;
        _audioSource.spread = 0f;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.volume = 0f;
        _lowPass = gameObject.GetComponent<AudioLowPassFilter>();
        if (_lowPass == null)
            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        _lowPass.enabled = false;
        AudioManager.Instance?.RouteSourceToSfxGroup(_audioSource);
    }

    /// <summary>
    /// Single writer for the lure's sound state, run every check interval:
    /// raycast the path, blend the muffle mix toward its target, fade in/out,
    /// and write volume + low-pass cutoff. Because everything blends through
    /// the same tick, moving between rooms never snaps the sound.
    /// </summary>
    private System.Collections.IEnumerator DistanceCheckLoop()
    {
        var wait = new WaitForSeconds(CheckInterval);
        float fadeStep = CheckInterval / FadeDuration;
        float muffleStep = CheckInterval / MuffleBlendDuration;

        while (enabled)
        {
            if (_playerCamera == null)
                _playerCamera = Camera.main;

            bool shouldPlay = ShouldPlaySound();

            if (shouldPlay && !_soundActive)
                StartPlayback();
            else if (!shouldPlay && _soundActive)
                StopPlayback();

            // Continuous muffle: blocked path ramps with distance, clear path
            // is fully clear regardless of range.
            float mixTarget = 0f;
            if (_soundActive && _playerCamera != null && _soundTarget != null)
            {
                bool pathBlocked = AudioOcclusionUtility.IsPathOccluded(
                    _soundTarget.position, _playerCamera.transform.position, _sourceIgnoreRadius);
                if (pathBlocked)
                {
                    float distance = Vector3.Distance(
                        _soundTarget.position, _playerCamera.transform.position);
                    mixTarget = Mathf.Clamp01(
                        (distance - _muffleStartDistance) /
                        Mathf.Max(0.01f, _muffleFullDistance - _muffleStartDistance));
                }
            }

            _muffleMix = Mathf.MoveTowards(_muffleMix, mixTarget, muffleStep);
            _fade = Mathf.MoveTowards(_fade, _soundActive ? 1f : 0f, fadeStep);

            if (_audioSource != null)
            {
                float volumeFactor = Mathf.Lerp(1f, _farOccludedVolumeFactor, _muffleMix);
                _audioSource.volume = _volume * volumeFactor * _fade;

                bool muffleAudible = _muffleMix > 0.01f;
                _lowPass.enabled = muffleAudible;
                if (muffleAudible)
                    _lowPass.cutoffFrequency =
                        Mathf.Lerp(ClearCutoffHz, _farOccludedCutoffHz, _muffleMix);

                if (!_soundActive && _fade <= 0f)
                {
                    _audioSource.Stop();
                    _audioSource.enabled = false;
                }
            }

            yield return wait;
        }
    }

    /// <summary>Starts playing the lure and registers it for background ducking.</summary>
    private void StartPlayback()
    {
        _soundActive = true;
        EnsureAudioSource();
        _audioSource.enabled = true;

        if (!_audioSource.isPlaying)
            _audioSource.Play();

        AudioManager.Instance?.RegisterLoopSource(_audioSource, _volume);
    }

    /// <summary>Marks the lure as stopped; the loop fades it out and halts the source.</summary>
    private void StopPlayback()
    {
        _soundActive = false;
        AudioManager.Instance?.UnregisterLoopSource(_audioSource);
    }

    /// <summary>
    /// Returns true if the lure sound should be playing:
    /// power on, duck renderer enabled, player within hearing range, duck not visible.
    /// </summary>
    private bool ShouldPlaySound()
    {
        // Master power must be on.
        if (!_isPowered)
            return false;

        if (_playerCamera == null || _soundTarget == null)
            return false;

        // Duck must be visible (renderer enabled) — power is on but duck hidden = no sound.
        if (_duckRenderer != null && !_duckRenderer.enabled)
            return false;

        Vector3 playerPos = _playerCamera.transform.position;
        float dist = Vector3.Distance(playerPos, _soundTarget.position);

        if (dist > _maxDistance)
            return false;

        // Duck is within hearing range — check visibility.
        return !IsDuckVisible();
    }

    /// <summary>
    /// Returns true if the duck's renderer is within the camera frustum
    /// and (optionally) not occluded by geometry.
    /// </summary>
    private bool IsDuckVisible()
    {
        if (_duckRenderer == null || _playerCamera == null)
            return false;

        if (!_duckRenderer.isVisible)
            return false;

        if (!_checkOcclusion)
            return true;

        Vector3 camPos = _playerCamera.transform.position;
        Vector3 duckPos = _soundTarget.position;
        Vector3 dir = duckPos - camPos;
        float dist = dir.magnitude;

        if (dist < 0.1f) return true;

        if (Physics.Raycast(camPos, dir / dist, out RaycastHit hit, dist, _occlusionMask, QueryTriggerInteraction.Ignore))
        {
            var hitRenderer = hit.collider.GetComponentInParent<Renderer>();
            return hitRenderer == _duckRenderer;
        }

        return true;
    }

    /// <summary>Instantly stops the sound without fading.</summary>
    private void StopSoundImmediate()
    {
        _soundActive = false;
        _fade = 0f;
        _muffleMix = 0f;

        if (_audioSource != null)
        {
            AudioManager.Instance?.UnregisterLoopSource(_audioSource);
            _audioSource.Stop();
            _audioSource.volume = 0f;
            _audioSource.enabled = false;
        }
    }

    private void Update()
    {
        if (_audioSource != null && _soundTarget != null)
            _audioSource.transform.position = _soundTarget.position;
    }

    private void StopCheckRoutine()
    {
        if (_checkRoutine != null)
        {
            StopCoroutine(_checkRoutine);
            _checkRoutine = null;
        }
    }
}
