using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a GameObject as physics-draggable by the player.
/// Requires a Rigidbody — its mass controls how slowly the object responds to force.
/// Assign the GameObject to the "Draggable" layer.
/// Also owns drag/drop audio: a looping scrape sound while the body slides along
/// a surface, and one-shot impact sounds on collisions (including being put down).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsDraggable : MonoBehaviour
{
    // ── Audio constants ────────────────────────────────────────────────────────

    private const float ScrapeFullVolumeSpeed = 3f;
    private const float ScrapeFadeSpeed = 6f;
    private const float ImpactMinSpeed = 0.5f;
    private const float ImpactMaxSpeed = 5f;
    private const float ImpactCooldown = 0.1f;
    private const float AudioSourceMinDistance = 1f;
    private const float AudioSourceMaxDistance = 15f;

    [Header("Drag Behavior")]
    [Tooltip("Hint shown in the interaction UI when the player looks at this object.")]
    [SerializeField] private string dragHint = "Тянуть";

    [Tooltip("When enabled, the object cannot be tipped over while dragging. X and Z rotation axes are frozen.")]
    [SerializeField] private bool preventTipping = false;

    [Tooltip("Max distance from the player at which this object can be grabbed. " +
             "0 = use the controller's global interact distance. Only shortens the global value — " +
             "a value larger than the global distance has no effect.")]
    [SerializeField] private float _maxDragDistance = 0f;

    [Tooltip("Distance in front of the camera where this object is HELD while dragging. " +
             "Smaller = the object hangs closer to the player. 0 = use the grabber's global hold distance.")]
    [SerializeField] private float _holdDistance = 0f;

    [Header("Drag Audio (loop while sliding)")]
    [Tooltip("Scrape loop clips; a random one is picked per drag session.")]
    [SerializeField] private List<AudioClip> _scrapeClips = new List<AudioClip>();

    [Tooltip("Base scrape volume at full sliding speed.")]
    [SerializeField, Range(0f, 1f)] private float _scrapeVolume = 0.7f;

    [Tooltip("Sliding speed (m/s) at which the scrape loop reaches full volume.")]
    [SerializeField] private float _scrapeFullVolumeSpeed = ScrapeFullVolumeSpeed;

    [Tooltip("How fast the scrape loop fades in/out (volume units per second).")]
    [SerializeField] private float _scrapeFadeSpeed = ScrapeFadeSpeed;

    [Tooltip("Minimum tangential speed (m/s) along the contact surface for the loop to start.")]
    [SerializeField] private float _scrapeStartSpeed = 0.05f;

    [Header("Impact Audio (one-shot on collision)")]
    [Tooltip("Impact clips; a random one is picked per collision. Plays when the object is put down, dropped or knocked over.")]
    [SerializeField] private List<AudioClip> _impactClips = new List<AudioClip>();

    [Tooltip("Volume at the minimum impact speed (soft touch).")]
    [SerializeField, Range(0f, 1f)] private float _impactVolumeAtMin = 0.15f;

    [Tooltip("Volume at the maximum impact speed (hard slam).")]
    [SerializeField, Range(0f, 1f)] private float _impactVolumeAtMax = 1f;

    [Tooltip("Impact speed (m/s) considered a hard slam; anything above is clamped.")]
    [SerializeField] private float _impactMaxSpeed = ImpactMaxSpeed;

    [Tooltip("Minimum relative speed (m/s) for a collision to produce any sound at all.")]
    [SerializeField] private float _impactMinSpeed = ImpactMinSpeed;

    [Tooltip("Minimum seconds between two impact sounds — prevents machine-gun spam while resting.")]
    [SerializeField] private float _impactCooldown = ImpactCooldown;

    [Tooltip("Random pitch variation applied to impacts (±).")]
    [SerializeField, Range(0f, 0.3f)] private float _impactPitchVariation = 0.05f;

    /// <summary>Cached Rigidbody reference.</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>UI hint text displayed while the player hovers over this object.</summary>
    public string DragHint => dragHint;

    /// <summary>Whether rotation on X and Z axes should be frozen while dragging.</summary>
    public bool PreventTipping => preventTipping;

    /// <summary>True when this object defines its own max grab distance.</summary>
    public bool HasCustomDragDistance => _maxDragDistance > 0f;

    /// <summary>Custom max grab distance (only meaningful when HasCustomDragDistance).</summary>
    public float MaxDragDistance => _maxDragDistance;

    /// <summary>True when this object defines its own hold distance while dragged.</summary>
    public bool HasCustomHoldDistance => _holdDistance > 0f;

    /// <summary>Custom hold distance while dragged (only meaningful when HasCustomHoldDistance).</summary>
    public float HoldDistance => _holdDistance;

    private AudioSource _scrapeSource;
    private AudioSource _impactSource;
    private bool _isScraping;
    private float _scrapeCurrentVolume;
    private float _lastImpactTime = -Mathf.Infinity;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        _scrapeSource = CreateSfxSource(loop: true);
        _impactSource = CreateSfxSource(loop: false);

        // The impact source's volume stays at 1 — per-shot loudness is passed as the
        // volumeScale argument of PlayOneShot. (The scrape source's volume is driven
        // manually in code and starts at 0.)
        _impactSource.volume = 1f;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (_scrapeClips.Count == 0 || Body.isKinematic) return;

        // Fastest contact-point speed along the surface plane (tangential velocity
        // ignores motion straight into/away from the surface). The other collider may
        // be static (no Rigidbody) — treat its point velocity as zero.
        float maxTangentialSpeed = 0f;
        foreach (ContactPoint contact in collision.contacts)
        {
            Rigidbody otherBody = contact.otherCollider.attachedRigidbody;
            Vector3 velocity = Body.GetPointVelocity(contact.point)
                             - (otherBody != null ? otherBody.GetPointVelocity(contact.point) : Vector3.zero);
            Vector3 tangential = velocity - Vector3.Project(velocity, contact.normal);
            float speed = tangential.magnitude;
            if (speed > maxTangentialSpeed) maxTangentialSpeed = speed;
        }

        if (maxTangentialSpeed >= _scrapeStartSpeed && !_isScraping)
            StartScrape();

        if (_isScraping)
        {
            float speedT = Mathf.Clamp01(maxTangentialSpeed / Mathf.Max(_scrapeFullVolumeSpeed, 0.01f));
            float targetVolume = _scrapeVolume * Mathf.Lerp(0.25f, 1f, speedT);
            _scrapeSource.pitch = Mathf.Lerp(0.85f, 1.15f, speedT);
            _scrapeCurrentVolume = Mathf.MoveTowards(_scrapeCurrentVolume, targetVolume, _scrapeFadeSpeed * Time.deltaTime);
            _scrapeSource.volume = _scrapeCurrentVolume;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        StopScrape();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_impactClips.Count == 0 || Body.isKinematic) return;
        if (Time.time - _lastImpactTime < _impactCooldown) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < _impactMinSpeed) return;

        _lastImpactTime = Time.time;

        // Louder and lower-pitched for hard impacts, quiet and higher-pitched for gentle touches.
        float strength = Mathf.Clamp01((impactSpeed - _impactMinSpeed) /
                                       Mathf.Max(_impactMaxSpeed - _impactMinSpeed, 0.01f));
        float volume = Mathf.Lerp(_impactVolumeAtMin, _impactVolumeAtMax, strength);
        float pitch  = Mathf.Lerp(1.1f, 0.9f, strength) + Random.Range(-_impactPitchVariation, _impactPitchVariation);

        AudioClip clip = _impactClips[Random.Range(0, _impactClips.Count)];
        _impactSource.pitch = pitch;
        _impactSource.PlayOneShot(clip, volume);
    }

    private void Update()
    {
        if (!_isScraping) return;

        // Fade out if the body stops moving while contact persists.
        if (Body.linearVelocity.magnitude < _scrapeStartSpeed)
            _scrapeCurrentVolume = Mathf.MoveTowards(_scrapeCurrentVolume, 0f, _scrapeFadeSpeed * Time.deltaTime);
        _scrapeSource.volume = _scrapeCurrentVolume;

        if (_scrapeCurrentVolume <= 0.001f)
            StopScrape();
    }

    private void StartScrape()
    {
        _isScraping = true;
        _scrapeCurrentVolume = 0f;

        _scrapeSource.clip = _scrapeClips[Random.Range(0, _scrapeClips.Count)];
        _scrapeSource.pitch = 1f;
        _scrapeSource.volume = 0f;
        _scrapeSource.Play();
    }

    private void StopScrape()
    {
        if (!_isScraping) return;
        _isScraping = false;
        _scrapeSource.Stop();
        _scrapeSource.volume = 0f;
    }

    /// <summary>Creates a 3D AudioSource routed to the SFX mixer group.</summary>
    private AudioSource CreateSfxSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f; // fully 3D
        source.minDistance = AudioSourceMinDistance;
        source.maxDistance = AudioSourceMaxDistance;
        source.volume = 0f;

        if (AudioManager.Instance != null)
            AudioManager.Instance.RouteSourceToSfxGroup(source);
        return source;
    }

    private void OnDisable() => StopScrape();
}
