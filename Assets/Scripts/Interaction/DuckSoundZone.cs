using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a looping 3D duck sound to lure the player when the duck is NOT visible.
/// Uses distance-based hearing range instead of a trigger zone:
///   - Player within hearing range AND duck not visible → sound fades in (lure).
///   - Duck visible (in frustum and unoccluded) → sound fades out (player found it).
///   - Player outside hearing range → sound stops.
///   - Master power OFF → sound stops entirely.
/// Implements IPowerConsumer to receive power state from LightingSystem.
/// The AudioSource follows the duck's position so the sound stays spatially accurate.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DuckSoundZone : MonoBehaviour, IPowerConsumer
{
    private const float FadeDuration = 1.5f;
    private const float CheckInterval = 0.2f;

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

    [Header("Visibility")]
    [Tooltip("If enabled, casts a ray from the camera to the duck to check for walls/obstacles blocking the view.")]
    [SerializeField] private bool _checkOcclusion = true;

    [Tooltip("Layer mask for occlusion raycast. Only these layers can block visibility.")]
    [SerializeField] private LayerMask _occlusionMask = ~0;

    private AudioSource _audioSource;
    private Renderer _duckRenderer;
    private Camera _playerCamera;
    private Coroutine _fadeCoroutine;
    private Coroutine _checkRoutine;
    private bool _soundActive;
    private bool _isPowered;

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

        if (!isPowered)
            FadeOutSound();
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
    }

    /// <summary>
    /// Periodically checks distance and visibility. Plays sound when player
    /// is within hearing range but cannot see the duck, and power is on.
    /// </summary>
    private IEnumerator DistanceCheckLoop()
    {
        var wait = new WaitForSeconds(CheckInterval);

        while (enabled)
        {
            if (_playerCamera == null)
                _playerCamera = Camera.main;

            bool shouldPlay = ShouldPlaySound();

            if (shouldPlay && !_soundActive)
                FadeInSound();
            else if (!shouldPlay && _soundActive)
                FadeOutSound();

            yield return wait;
        }
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

    /// <summary>Fades in the duck sound and starts playback.</summary>
    private void FadeInSound()
    {
        _soundActive = true;
        EnsureAudioSource();
        _audioSource.enabled = true;

        if (!_audioSource.isPlaying)
            _audioSource.Play();

        AudioManager.Instance?.RegisterLoopSource(_audioSource, _volume);

        StopFadeCoroutine();
        _fadeCoroutine = StartCoroutine(FadeVolume(_audioSource.volume, _volume, FadeDuration));
    }

    /// <summary>Fades out the duck sound and stops playback after fade.</summary>
    private void FadeOutSound()
    {
        if (!_soundActive) return;

        _soundActive = false;
        AudioManager.Instance?.UnregisterLoopSource(_audioSource);

        StopFadeCoroutine();
        _fadeCoroutine = StartCoroutine(FadeVolume(_audioSource.volume, 0f, FadeDuration, () =>
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.enabled = false;
            }
        }));
    }

    /// <summary>Instantly stops the sound without fading.</summary>
    private void StopSoundImmediate()
    {
        _soundActive = false;
        StopFadeCoroutine();

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

    /// <summary>Smoothly transitions volume over time.</summary>
    private IEnumerator FadeVolume(float from, float to, float duration, System.Action onComplete = null)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (_audioSource != null)
                _audioSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (_audioSource != null)
            _audioSource.volume = to;

        onComplete?.Invoke();
        _fadeCoroutine = null;
    }

    private void StopFadeCoroutine()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
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
