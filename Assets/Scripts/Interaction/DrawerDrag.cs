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
    [Tooltip("How many units of open-fraction the drawer moves per pixel of mouse movement. Tune this to taste.")]
    [SerializeField] private float _dragSensitivity = 0.003f;
    [Tooltip("Invert the drag axis if the drawer moves in the wrong direction.")]
    [SerializeField] private bool _invertAxis = false;
    [Tooltip("Speed at which the drawer snaps to fully open or closed after release.")]
    [SerializeField] private float _snapSpeed = 8f;
    [Tooltip("If open fraction exceeds this on release, drawer snaps fully open; otherwise it closes.")]
    [SerializeField] [Range(0f, 1f)] private float _snapThreshold = 0.5f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Потянуть";

    private Vector3 _closedLocalPosition;

    // 0 = fully closed, 1 = fully open
    private float _openAmount;
    private float _targetOpenAmount;
    private bool _isDragging;

    // Screen-space direction of the drawer's opening, computed once at drag start.
    // Projecting mouse delta onto this vector handles all approach angles correctly.
    private Vector2 _screenOpenDir;

    // The Animator driving the parent (disabled while the player manually drags)
    private Animator _parentAnimator;

    private void Start()
    {
        _closedLocalPosition = transform.localPosition;
        _parentAnimator = GetComponentInParent<Animator>();
    }

    private void Update()
    {
        if (_isDragging) return;

        // Smoothly snap to fully open or closed after the player releases LMB.
        _openAmount = Mathf.Lerp(_openAmount, _targetOpenAmount, _snapSpeed * Time.deltaTime);
        ApplyPosition();
    }

    // ── IDraggable ──────────────────────────────────────────────────────────────

    /// <summary>Called by FPSController when LMB is pressed while the player looks at the drawer.</summary>
    public void OnDragStart()
    {
        _isDragging = true;

        // Project the drawer's world-space open direction onto screen space.
        // This makes the drag gesture work correctly from any angle:
        // moving the mouse in the direction the drawer visually moves = opening it.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 worldOpenDir = transform.TransformDirection(_openDirection.normalized);
            Vector3 screenOrigin = cam.WorldToScreenPoint(transform.position);
            Vector3 screenTip    = cam.WorldToScreenPoint(transform.position + worldOpenDir * 0.5f);

            Vector2 dir = new Vector2(screenTip.x - screenOrigin.x, screenTip.y - screenOrigin.y);
            _screenOpenDir = dir.sqrMagnitude > 1f ? dir.normalized : Vector2.down;
        }
        else
        {
            _screenOpenDir = Vector2.down;
        }

        if (_parentAnimator != null)
            _parentAnimator.enabled = false;
    }

    /// <summary>Called every frame while LMB is held. Mouse delta is in screen-space pixels.</summary>
    public void OnDrag(Vector2 mouseDelta)
    {
        // Dot product: mouse moving in the same direction the drawer visually opens → positive → opens.
        float input = Vector2.Dot(mouseDelta, _screenOpenDir);
        if (_invertAxis) input = -input;
        _openAmount = Mathf.Clamp01(_openAmount + input * _dragSensitivity);
        ApplyPosition();
    }

    /// <summary>Called by FPSController when LMB is released. Snaps to nearest rest position.</summary>
    public void OnDragEnd()
    {
        _isDragging       = false;
        _targetOpenAmount = _openAmount >= _snapThreshold ? 1f : 0f;
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
