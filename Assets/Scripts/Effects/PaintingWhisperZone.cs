using UnityEngine;

/// <summary>
/// Whisper trigger of a painting. While the player stands inside this trigger collider AND is turned
/// away from the painting, a quiet looping whisper (one voice) plays from the painting.
/// It fades in slowly, but the moment the player sees the painting (it is in the camera view, or the
/// player turns back towards it) the whisper dies out almost instantly.
///
/// Put this component on an object with a trigger BoxCollider - the collider is used exactly as you set it.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class PaintingWhisperZone : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [Header("Painting")]
    [Tooltip("The painting the whisper comes from and that the player must not see. Empty = this object.")]
    [SerializeField] private Transform _painting;

    [Header("Whisper")]
    [Tooltip("Looping whisper.")]
    [SerializeField] private AudioClip _whisperClip;

    [Tooltip("Whisper volume at full fade-in. Keep it low: the whisper should be barely noticed.")]
    [SerializeField, Range(0f, 1f)] private float _volume = 0.35f;

    [Tooltip("Seconds to fade in after the player turned away.")]
    [SerializeField, Min(0.05f)] private float _fadeInTime = 2f;

    [Tooltip("Seconds to fade out when the player sees the painting. Very short = the whisper just cuts off.")]
    [SerializeField, Min(0.02f)] private float _fadeOutTime = 0.12f;

    [Header("Facing")]
    [Tooltip("The whisper may start when the angle between the player's view and the painting is larger than this (180 = back to the painting).")]
    [SerializeField, Range(60f, 175f)] private float _turnedAwayAngle = 110f;

    [Tooltip("The whisper stops at once when the angle gets smaller than this (the painting is at the edge of the view).")]
    [SerializeField, Range(30f, 170f)] private float _turnedBackAngle = 90f;

    [Tooltip("Extra margin around the screen (0 = exact screen edge). The painting counts as seen a little before it fully enters the screen.")]
    [SerializeField, Range(0f, 0.5f)] private float _screenMargin = 0.15f;

    [Header("3D sound")]
    [SerializeField, Range(0f, 1f)] private float _spatialBlend = 0.8f;
    [SerializeField, Min(0.1f)] private float _minDistance = 1.5f;
    [SerializeField, Min(0.2f)] private float _maxDistance = 9f;

    private AudioSource _source;
    private Transform _player;
    private bool _playerInside;
    private bool _awayLatch;
    private float _level;
    private float _resumeTime;   // position in the clip where the whisper was stopped
    private Camera _camera;

    /// <summary>True while the whisper is audible.</summary>
    public bool IsWhispering => _level > 0.001f;

    private Transform Painting => _painting != null ? _painting : transform;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;   // the size / position are left as set in the editor

        // The sound comes from the painting itself
        _source = Painting.gameObject.AddComponent<AudioSource>();
        _source.clip = _whisperClip;
        _source.loop = true;
        _source.playOnAwake = false;
        _source.volume = 0f;
        _source.spatialBlend = _spatialBlend;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = _minDistance;
        _source.maxDistance = _maxDistance;
        _source.dopplerLevel = 0f;
        AudioManager.Instance?.RouteSourceToSfxGroup(_source);
    }

    private void OnDisable()
    {
        // Rooms get switched off when the player is away: always end silent and reset
        _playerInside = false;
        _awayLatch = false;
        _level = 0f;
        _player = null;
        StopWhisper();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        _player = other.transform;
        _playerInside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        _playerInside = false;
        _awayLatch = false;
    }

    private void Update()
    {
        if (_source == null || _whisperClip == null) return;

        bool shouldPlay = _playerInside && IsTurnedAway();
        float target = shouldPlay ? _volume : 0f;
        float time = shouldPlay ? _fadeInTime : _fadeOutTime;
        _level = Mathf.MoveTowards(_level, target, _volume / time * Time.deltaTime);

        if (_level > 0.001f)
        {
            if (!_source.isPlaying)
                StartWhisper();
            _source.volume = _level;
        }
        else if (_source.isPlaying)
        {
            StopWhisper();
        }
    }

    /// <summary>Continues the text from the place where it was stopped last time.</summary>
    private void StartWhisper()
    {
        _source.time = Mathf.Clamp(_resumeTime, 0f, Mathf.Max(0f, _whisperClip.length - 0.05f));
        _source.Play();
    }

    /// <summary>Stops the sound and remembers where it was, so the next start does not begin from the first word.</summary>
    private void StopWhisper()
    {
        if (_source == null) return;
        if (_source.isPlaying)
            _resumeTime = _source.time;
        _source.volume = 0f;
        _source.Stop();
    }

    /// <summary>
    /// True when the player has his back to the painting. Becomes false at once as soon as the painting
    /// is on the screen or the view turns towards it (the start angle is larger than the stop angle).
    /// </summary>
    private bool IsTurnedAway()
    {
        Transform view = ViewTransform();
        if (view == null) return false;

        // Seen: the painting is in the camera view
        if (IsInView())
        {
            _awayLatch = false;
            return false;
        }

        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        Vector3 toPainting = Vector3.ProjectOnPlane(Painting.position - view.position, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f || toPainting.sqrMagnitude < 1e-4f)
            return _awayLatch;

        float angle = Vector3.Angle(forward, toPainting);
        _awayLatch = _awayLatch ? angle > _turnedBackAngle : angle > _turnedAwayAngle;
        return _awayLatch;
    }

    private bool IsInView()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return false;

        Vector3 v = _camera.WorldToViewportPoint(Painting.position);
        float lo = -_screenMargin;
        float hi = 1f + _screenMargin;
        return v.z > 0f && v.x > lo && v.x < hi && v.y > lo && v.y < hi;
    }

    private Transform ViewTransform()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null) return _camera.transform;
        return _player;
    }
}
