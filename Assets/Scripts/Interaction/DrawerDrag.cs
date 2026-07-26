using UnityEngine;

/// <summary>
/// Attach to a drawer Transform. The player holds LMB while looking at this object
/// and moves the mouse to physically slide the drawer open or closed.
///
/// Setup:
///   1. Set _openDirection to the local axis the drawer slides along (e.g. (0,0,1) for forward).
///   2. Set _openDistance to how far the drawer travels when fully open (in metres).
///   3. The closed position is captured automatically from localPosition at Start.
///   4. Optionally set _snapThreshold: above it the drawer snaps open on release, below it closes.
/// </summary>
public class DrawerDrag : MonoBehaviour, IInteractable, IDraggable
{
    [Header("Drawer Motion")]
    [Tooltip("Local-space direction the drawer slides toward when opening.")]
    [SerializeField] private Vector3 _openDirection = Vector3.forward;
    [Tooltip("Distance (metres) from closed to fully open position.")]
    [SerializeField] private float _openDistance = 0.4f;

    [Header("Drag Feel")]
    [Tooltip("Sensitivity multiplier on top of auto-computed screen tracking. " +
             "1 = drawer handle follows cursor exactly. 2 = twice as fast.")]
    [SerializeField] private float _dragSensitivity = 1f;
    [Tooltip("Invert the drag axis if the drawer moves in the wrong direction.")]
    [SerializeField] private bool _invertAxis = false;
    [Tooltip("If true, the drawer snaps to fully open or closed after release. " +
             "If false, it stays exactly where the player left it.")]
    [SerializeField] private bool _snapOnRelease = true;
    [Tooltip("Speed at which the drawer snaps to fully open or closed after release.")]
    [SerializeField] private float _snapSpeed = 8f;
    [Tooltip("If open fraction exceeds this on release, drawer snaps fully open; otherwise it closes.")]
    [SerializeField] [Range(0f, 1f)] private float _snapThreshold = 0.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip _openClip;
    [SerializeField] [Range(0f, 1f)] private float _openVolume = 0.8f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Потянуть";

    private Vector3 _closedLocalPosition;

    // 0 = fully closed, 1 = fully open
    private float _openAmount;
    private float _targetOpenAmount;
    private bool _isDragging;
    private bool _wasOpen;

    // Direction tracking for mid-drag sound retrigger
    private int   _lastDragSign;
    private float _directionChangeCooldown;
    private const float DirectionChangeCooldownDuration = 0.4f;
    private const float DragInputThreshold = 0.5f;

    // Screen-space direction of the drawer's opening, computed once at drag start.
    // Projecting mouse delta onto this vector handles all approach angles correctly.
    private Vector2 _screenOpenDir;

    // Pixels-per-full-open-fraction ratio, auto-computed from drawer's screen size at drag start.
    // Ensures the drawer handle follows the cursor at 1:1 regardless of distance or FOV.
    private float _computedSensitivity;

    // The Animator driving the parent (disabled while the player manually drags)
    private Animator _parentAnimator;
    private AudioSource _audioSource;

    private void Start()
    {
        _closedLocalPosition = transform.localPosition;
        _parentAnimator = GetComponentInParent<Animator>();

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 1f;
        _audioSource.loop         = false;
    }

    private void Update()
    {
        if (_directionChangeCooldown > 0f)
            _directionChangeCooldown -= Time.deltaTime;

        if (_isDragging) return;

        // Smoothly snap to fully open or closed after the player releases LMB.
        _openAmount = Mathf.Lerp(_openAmount, _targetOpenAmount, _snapSpeed * Time.deltaTime);
        ApplyPosition();
    }

    // ── IDraggable ──────────────────────────────────────────────────────────────

    /// <summary>Called by FPSController when LMB is pressed while the player looks at the drawer.</summary>
    public void OnDragStart(Vector3 hitPoint)
    {
        _isDragging  = true;
        _lastDragSign = 0;
        _directionChangeCooldown = 0f;

        if (_openClip != null)
            _audioSource.PlayOneShot(_openClip, _openVolume);

        if (_parentAnimator != null)
            _parentAnimator.enabled = false;

        Camera cam = Camera.main;
        if (cam == null)
        {
            _screenOpenDir       = Vector2.right;
            _computedSensitivity = _dragSensitivity * 0.003f;
            return;
        }

        // World positions of the closed and fully-open endpoints.
        // Both endpoints are computed via parent.TransformPoint to match ApplyPosition(),
        // which sets localPosition (parent space). Using transform.TransformDirection here
        // would incorrectly apply the drawer's own local rotation and ignore parent scale,
        // producing a wrong screen-space open direction when the drawer has a non-identity
        // rotation (e.g. FBX axis-correction rotation).
        Vector3 openLocalPos = _closedLocalPosition + _openDirection.normalized * _openDistance;
        Vector3 closedWorld = transform.parent != null
            ? transform.parent.TransformPoint(_closedLocalPosition)
            : _closedLocalPosition;
        Vector3 openWorld = transform.parent != null
            ? transform.parent.TransformPoint(openLocalPos)
            : openLocalPos;

        // Project both endpoints onto screen → screen-space open direction + pixel length.
        Vector3 screenA = cam.WorldToScreenPoint(closedWorld);
        Vector3 screenB = cam.WorldToScreenPoint(openWorld);
        Vector2 screenDelta = new Vector2(screenB.x - screenA.x, screenB.y - screenA.y);
        float   screenLength = screenDelta.magnitude;

        if (screenLength > 1f)
        {
            // Auto-sensitivity: 1 pixel moves the drawer by (1 / screenLength) of its full range.
            // _dragSensitivity acts as a multiplier (1 = exact cursor tracking).
            _screenOpenDir       = screenDelta / screenLength;
            _computedSensitivity = _dragSensitivity / screenLength;
        }
        else
        {
            // Drawer axis is almost parallel to camera view — use a raw fallback.
            _screenOpenDir       = Vector2.right;
            _computedSensitivity = _dragSensitivity * 0.003f;
        }
    }

    /// <summary>Called every frame while LMB is held. Mouse delta is in screen-space pixels.</summary>
    public void OnDrag(Vector2 mouseDelta)
    {
        float input = Vector2.Dot(mouseDelta, _screenOpenDir);
        if (_invertAxis) input = -input;
        _openAmount = Mathf.Clamp01(_openAmount + input * _computedSensitivity);
        ApplyPosition();

        // Detect direction reversal and retrigger sound with cooldown.
        if (Mathf.Abs(input) > DragInputThreshold)
        {
            int currentSign = input > 0f ? 1 : -1;
            if (_lastDragSign != 0 && currentSign != _lastDragSign && _directionChangeCooldown <= 0f)
            {
                if (_openClip != null)
                    _audioSource.PlayOneShot(_openClip, _openVolume);
                _directionChangeCooldown = DirectionChangeCooldownDuration;
            }
            _lastDragSign = currentSign;
        }
    }

    /// <summary>Called by FPSController when LMB is released. Snaps to nearest rest position.</summary>
    public void OnDragEnd()
    {
        _isDragging = false;
        if (_snapOnRelease)
            _targetOpenAmount = _openAmount >= _snapThreshold ? 1f : 0f;
        else
            _targetOpenAmount = _openAmount;
        _wasOpen = _targetOpenAmount >= 1f;
    }

    // ── IInteractable ───────────────────────────────────────────────────────────

    /// <summary>Drawer is drag-only. E press has no effect.</summary>
    public void Interact() { }

    /// <summary>Returns the hint label shown when the player looks at the drawer.</summary>
    public string GetInteractText() => _interactText;

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode() => CrosshairMode.Grab;

    // ── Private helpers ─────────────────────────────────────────────────────────

    private void ApplyPosition()
    {
        Vector3 openLocalPos = _closedLocalPosition + _openDirection.normalized * _openDistance;
        transform.localPosition = Vector3.Lerp(_closedLocalPosition, openLocalPos, _openAmount);
    }
}
