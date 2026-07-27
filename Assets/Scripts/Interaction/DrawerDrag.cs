using System.Collections;
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
///   5. Set _isLocked = true and optionally _lockedHint for locked drawers used in puzzles.
///      Call Unlock() from puzzle logic to enable dragging.
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

    [Header("Lock")]
    [Tooltip("If true, the drawer is locked and cannot be dragged open until Unlock() is called.")]
    [SerializeField] private bool _isLocked = false;
    [Tooltip("Hint shown when the player tries to drag a locked drawer.")]
    [SerializeField] private string _lockedHint = "Заперто";
    [Tooltip("How far a locked drawer can be pulled before it stops (fraction of full open).")]
    [SerializeField] [Range(0f, 0.15f)] private float _lockedJiggleAmount = 0.05f;
    [Tooltip("Speed at which a locked drawer slides back after a jiggle attempt.")]
    [SerializeField] private float _lockedSnapBackSpeed = 10f;
    [Tooltip("Sound played when the player tries to pull a locked drawer.")]
    [SerializeField] private AudioClip _lockedClip;
    [Tooltip("Sound played when the drawer is unlocked via Unlock().")]
    [SerializeField] private AudioClip _unlockClip;

    [Header("Audio")]
    [SerializeField] private AudioClip _openClip;
    [SerializeField] [Range(0f, 1f)] private float _openVolume = 0.8f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Потянуть";

    private Vector3 _closedLocalPosition;
    private bool _closedPositionCaptured;

    // 0 = fully closed, 1 = fully open
    private float _openAmount;
    private float _targetOpenAmount;
    private bool _isDragging;
    private bool _wasOpen;

    // Locked-drag state
    private bool _isLockedDrag;
    private bool _snappingBack;

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

    private Coroutine _autoOpenCoroutine;

    private void Start()
    {
        EnsureClosedPosition();
        _parentAnimator = GetComponentInParent<Animator>();

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 1f;
        _audioSource.loop         = false;
    }

    private void EnsureClosedPosition()
    {
        if (_closedPositionCaptured) return;
        _closedLocalPosition = transform.localPosition;
        _closedPositionCaptured = true;
    }

    private void Update()
    {
        if (_directionChangeCooldown > 0f)
            _directionChangeCooldown -= Time.deltaTime;

        if (_isDragging) return;

        // ── Locked drawer snap-back ──────────────────────────────────────────
        if (_snappingBack)
        {
            _openAmount = Mathf.Lerp(_openAmount, 0f, _lockedSnapBackSpeed * Time.deltaTime);
            ApplyPosition();
            if (_openAmount < 0.001f)
            {
                _openAmount   = 0f;
                _snappingBack = false;
                ApplyPosition();
            }
            return;
        }

        // Smoothly snap to fully open or closed after the player releases LMB.
        _openAmount = Mathf.Lerp(_openAmount, _targetOpenAmount, _snapSpeed * Time.deltaTime);
        ApplyPosition();
    }

    // ── IDraggable ──────────────────────────────────────────────────────────────

    /// <summary>Called by FPSController when LMB is pressed while the player looks at the drawer.</summary>
    public void OnDragStart(Vector3 hitPoint, Camera cam)
    {
        _isDragging  = true;
        _lastDragSign = 0;
        _directionChangeCooldown = 0f;
        _snappingBack = false;

        // Locked drawer: play locked sound and allow only a small jiggle.
        _isLockedDrag = _isLocked;

        if (_isLockedDrag)
        {
            if (_lockedClip != null)
                _audioSource.PlayOneShot(_lockedClip, _openVolume);
        }
        else
        {
            if (_openClip != null)
                _audioSource.PlayOneShot(_openClip, _openVolume);
        }

        if (_parentAnimator != null)
            _parentAnimator.enabled = false;

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

        // Locked drawer: clamp to a small jiggle range instead of full open.
        float maxAmount = _isLockedDrag ? _lockedJiggleAmount : 1f;
        _openAmount = Mathf.Clamp(_openAmount + input * _computedSensitivity, 0f, maxAmount);
        ApplyPosition();

        // Detect direction reversal and retrigger sound with cooldown.
        if (Mathf.Abs(input) > DragInputThreshold)
        {
            int currentSign = input > 0f ? 1 : -1;
            if (_lastDragSign != 0 && currentSign != _lastDragSign && _directionChangeCooldown <= 0f)
            {
                if (!_isLockedDrag && _openClip != null)
                    _audioSource.PlayOneShot(_openClip, _openVolume);
                _directionChangeCooldown = DirectionChangeCooldownDuration;
            }
            _lastDragSign = currentSign;
        }
    }

    /// <summary>Called by FPSController when LMB is released. Snaps to nearest rest position.</summary>
    public void OnDragEnd()
    {
        bool wasLockedDrag = _isLockedDrag;
        _isDragging   = false;
        _isLockedDrag = false;

        // Locked drawer: snap back to closed position.
        if (wasLockedDrag)
        {
            _snappingBack    = true;
            _targetOpenAmount = 0f;
            return;
        }

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
    public string GetInteractText()
    {
        if (_isLocked)
            return _lockedHint;
        return _interactText;
    }

    public bool IsPickable() => false;

    /// <summary>Grab icon for unlocked drawers, Locked icon for locked ones.</summary>
    public CrosshairMode GetCrosshairMode()
    {
        return _isLocked ? CrosshairMode.Locked : CrosshairMode.Grab;
    }

    /// <summary>Returns the locked hint when the drawer is locked.</summary>
    public string GetBlockedHint()
    {
        if (_isLocked)
            return _lockedHint;
        return string.Empty;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Unlocks the drawer programmatically. Wire to puzzle OnSolved events.</summary>
    public void Unlock()
    {
        _isLocked = false;
        if (_unlockClip != null)
            _audioSource.PlayOneShot(_unlockClip, _openVolume);
    }

    /// <summary>Locks the drawer programmatically.</summary>
    public void Lock() => _isLocked = true;

    /// <summary>True when the drawer is locked.</summary>
    public bool IsLocked => _isLocked;

    /// <summary>
    /// Unlocks the drawer and smoothly slides it to the fully open position.
    /// Used by puzzle logic when the puzzle is solved.
    /// </summary>
    public void AutoOpen()
    {
        EnsureClosedPosition();
        _isLocked = false;
        _targetOpenAmount = 1f;

        if (_openClip != null && _audioSource != null)
            _audioSource.PlayOneShot(_openClip, _openVolume);

        if (_autoOpenCoroutine != null) StopCoroutine(_autoOpenCoroutine);
        _autoOpenCoroutine = StartCoroutine(AutoOpenRoutine());
    }

    /// <summary>
    /// Instantly unlocks and snaps the drawer to fully open without animation.
    /// Used for save restoration on load.
    /// </summary>
    public void SnapOpen()
    {
        EnsureClosedPosition();
        _isLocked = false;
        _openAmount = 1f;
        _targetOpenAmount = 1f;
        ApplyPosition();
    }

    private IEnumerator AutoOpenRoutine()
    {
        float startAmount = _openAmount;
        float elapsed = 0f;
        float duration = 1f / _snapSpeed;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            _openAmount = Mathf.Lerp(startAmount, 1f, t);
            ApplyPosition();
            yield return null;
        }

        _openAmount = 1f;
        ApplyPosition();
        _autoOpenCoroutine = null;
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private void ApplyPosition()
    {
        Vector3 openLocalPos = _closedLocalPosition + _openDirection.normalized * _openDistance;
        transform.localPosition = Vector3.Lerp(_closedLocalPosition, openLocalPos, _openAmount);
    }
}
