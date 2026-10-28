using UnityEngine;

/// <summary>
/// Plays sounds triggered by Animation Events on this object's Animator.
/// One shared AudioSource: starting a new sound cuts off the previous one.
/// Attach to the GameObject that has the Animator.
/// </summary>
public class AnimationEventSoundPlayer : MonoBehaviour
{
    [Tooltip("Громкость фазовых звуков.")]
    [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;

    private AudioSource _source;

    /// <summary>Animation Event target: plays the clip, cutting off the previous sound.</summary>
    public void PlaySound(AudioClip clip)
    {
        if (clip == null || AudioManager.Instance == null) return;

        if (_source == null)
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.minDistance = 1f;
            _source.maxDistance = 12f;
            AudioManager.Instance.RouteSourceToSfxGroup(_source);
        }

        _source.Stop();          // обрываем предыдущий фазовый звук
        _source.clip = clip;
        _source.volume = _volume;
        _source.Play();
    }
}
