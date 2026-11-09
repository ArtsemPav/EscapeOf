using UnityEngine;

/// <summary>
/// Occlusion-aware playback for a 3D AudioSource: while the clip is playing,
/// the direct path from the source to the player camera is raycast at intervals.
/// When the path is blocked (floor slab, wall, closed door), the source plays
/// quieter and low-pass filtered — a muffled sound "through the wall" instead
/// of a clear one. When the path is clear, full volume and full brightness are
/// restored. Attach to the GameObject with the AudioSource that must not be
/// heard through floors or walls.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioSourceOcclusion : MonoBehaviour
{
    [Header("Occlusion")]
    [Tooltip("When the direct path to the player is blocked (floor slab, wall, closed door), the sound plays this much quieter. 0 = fully silent through walls.")]
    [SerializeField, Range(0f, 1f)] private float _occludedVolumeFactor = 0.1f;

    [Tooltip("Low-pass cutoff (Hz) for the occluded sound. Low value = a dull muffled sound, as if through a thick floor.")]
    [SerializeField, Range(100f, 4000f)] private float _occludedCutoffHz = 400f;

    [Tooltip("Seconds between occlusion raycasts while the sound is playing.")]
    [SerializeField, Range(0.05f, 1f)] private float _occlusionCheckInterval = 0.15f;

    [Tooltip("Geometry within this radius of the source is ignored by the occlusion raycast. " +
             "Raise it when the source sits inside a recess (bowl, shelf, box) that would otherwise " +
             "permanently occlude it.")]
    [SerializeField, Min(0f)] private float _sourceIgnoreRadius = 0.3f;

    private AudioSource _source;
    private AudioLowPassFilter _lowPass;
    private Camera _playerCamera;
    private float _baseVolume;
    private float _nextOcclusionCheckTime;
    private bool _isOccluded;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _baseVolume = _source.volume;
        _lowPass = GetComponent<AudioLowPassFilter>();
        if (_lowPass == null)
            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        _lowPass.enabled = false;
    }

    private void Update()
    {
        if (!_source.isPlaying)
        {
            if (_isOccluded)
                RestoreFullSound();
            return;
        }

        if (Time.unscaledTime >= _nextOcclusionCheckTime)
        {
            _nextOcclusionCheckTime = Time.unscaledTime + _occlusionCheckInterval;
            if (_playerCamera == null)
                _playerCamera = Camera.main;
            _isOccluded = _playerCamera != null &&
                AudioOcclusionUtility.IsPathOccluded(
                    transform.position, _playerCamera.transform.position, _sourceIgnoreRadius);
        }

        if (_isOccluded)
        {
            _source.volume = _baseVolume * _occludedVolumeFactor;
            _lowPass.cutoffFrequency = _occludedCutoffHz;
            _lowPass.enabled = true;
        }
        else
        {
            RestoreFullSound();
        }
    }

    /// <summary>Restores the source's original volume and disables the low-pass filter.</summary>
    private void RestoreFullSound()
    {
        _isOccluded = false;
        _source.volume = _baseVolume;
        _lowPass.enabled = false;
    }
}
