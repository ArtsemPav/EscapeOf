using UnityEngine;

/// <summary>
/// Plays the fall sound through the AudioManager when the player falls
/// through the floor. Listens to the GameEvent raised by
/// <see cref="FallThroughFloorEvent"/> and calls
/// <c>AudioManager.PlaySFX</c> (2D) or <c>Play3DOneShot</c> (3D, at the
/// stored impact position).
/// </summary>
public class FallSoundListener : MonoBehaviour
{
    [Header("Event")]
    [Tooltip("GameEvent raised when the player falls through the floor.")]
    [SerializeField] private GameEvent _fallEvent;

    [Header("Sound")]
    [Tooltip("Fall sound profile with the clips and volume. Swap the asset to change the sound.")]
    [SerializeField] private FallSoundProfile _soundProfile;

    [Header("Playback")]
    [Tooltip("Play the sound as a spatial 3D one-shot at the impact point instead of a 2D one-shot.")]
    [SerializeField] private bool _spatial3D = false;
    [Tooltip("Volume of the fall sound.")]
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [Tooltip("3D rolloff start distance (spatial mode only).")]
    [SerializeField] private float _minDistance = 1f;
    [Tooltip("3D rolloff end distance (spatial mode only).")]
    [SerializeField] private float _maxDistance = 15f;

    private const float PositionCaptureRadius = 5f;
    private static readonly Vector3 ImpactPosition = new Vector3(9.6f, -0.03f, -12.1f);

    private void OnEnable()
    {
        if (_fallEvent != null)
            _fallEvent.RegisterAction(OnFall);
    }

    private void OnDisable()
    {
        if (_fallEvent != null)
            _fallEvent.UnregisterAction(OnFall);
    }

    private void OnFall()
    {
        if (_soundProfile == null)
            return;

        AudioClip clip = _soundProfile.GetRandomClip();
        if (clip == null)
            return;

        float volume = _soundProfile.Volume;

        if (_spatial3D)
            AudioManager.Instance.Play3DOneShot(clip, ImpactPosition, volume, _minDistance, _maxDistance);
        else
            AudioManager.Instance.PlaySFX(clip, volume);
    }
}
