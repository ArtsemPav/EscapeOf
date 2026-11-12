using UnityEngine;

/// <summary>
/// Activates the GameObject and plays its ParticleSystem from the beginning.
/// Designed for one-shot effects that must stay disabled at scene start
/// (otherwise Play On Awake fires on load) and be triggered from UnityEvents,
/// because GameObject.SetActive cannot be wired as a persistent call.
/// </summary>
public class ParticleSystemActivator : MonoBehaviour
{
    /// <summary>
    /// Activates the GameObject and restarts the attached ParticleSystem.
    /// Safe to call repeatedly: each call replays the effect from the start.
    /// </summary>
    public void ActivateAndPlay()
    {
        gameObject.SetActive(true);

        ParticleSystem particleSystem = GetComponent<ParticleSystem>();
        if (particleSystem != null)
        {
            particleSystem.Play();
        }
    }
}
