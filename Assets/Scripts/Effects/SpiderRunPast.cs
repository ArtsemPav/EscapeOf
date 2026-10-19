using UnityEngine;

/// <summary>
/// Spider horror effect: the spider crawls in a straight line and disappears.
/// Attach to a spider GameObject that is initially inactive.
/// A <see cref="HorrorEvent"/> with <c>AppearAndStay</c> activates the GameObject;
/// this script handles the crawl in <see cref="OnEnable"/> and deactivates the
/// GameObject when the crawl ends (so no HorrorEvent delay is needed).
///
/// The crawl direction is the object's own forward axis (transform.forward) —
/// rotate the spider in the editor to aim its crawl route across the player's path.
/// Unlike <see cref="RatRunAway"/>, the animation state name is configurable
/// because the spider's Animator Controller uses prefixed state names
/// (e.g. "rig|walk"), which cannot be hardcoded.
/// </summary>
public class SpiderRunPast : MonoBehaviour
{
    [Header("Crawl")]
    [Tooltip("Name of the crawl/walk animation state in the Animator Controller.")]
    [SerializeField] private string _crawlStateName = "rig|walk";

    [Tooltip("Crawl speed (units per second).")]
    [SerializeField] private float _speed = 3f;

    [Tooltip("Crawl duration before the spider disappears (seconds).")]
    [SerializeField] private float _lifetime = 1.5f;

    private Animator _animator;
    private float _elapsed;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        _elapsed = 0f;
        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.Play(_crawlStateName, 0, 0f);
        }
        else
        {
            Debug.LogWarning($"[SpiderRunPast '{name}'] No Animator found in children — spider will crawl without animation.", this);
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (_elapsed >= _lifetime)
        {
            gameObject.SetActive(false);
            return;
        }

        // Move along the object's forward axis — rotate in the editor to aim.
        transform.position += transform.forward * (_speed * Time.deltaTime);
    }
}
