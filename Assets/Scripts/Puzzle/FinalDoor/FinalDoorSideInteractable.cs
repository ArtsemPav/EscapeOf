using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Lightweight IInteractable placed on each medallion statue.
/// When the player interacts with a statue, enters puzzle mode and
/// instantly cuts to that statue's camera.
///
/// FPSController finds this via TryGetComponent on the statue's collider —
/// it takes priority over FinalDoorPuzzleController's own IInteractable
/// on the root, so the controller never needs to know which statue was clicked.
/// </summary>
public class FinalDoorSideInteractable : MonoBehaviour, IInteractable
{
    [Header("Controller")]
    [Tooltip("Auto-found via GetComponentInParent if empty.")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Camera")]
    [Tooltip("This statue's CinemachineCamera. The camera will be activated instantly on interact.")]
    [SerializeField] private CinemachineCamera _camera;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Осмотреть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponentInParent<FinalDoorPuzzleController>();

        // Auto-find camera in children if not assigned.
        if (_camera == null)
            _camera = GetComponentInChildren<CinemachineCamera>(includeInactive: true);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract()
    {
        return _controller != null && !_controller.IsActive && !_controller.IsSolved;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        _controller.EnterPuzzleMode(_camera);
    }

    public string GetInteractText()
    {
        return _controller != null && _controller.IsSolved ? string.Empty : _interactText;
    }

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode()
    {
        return _controller != null && _controller.IsSolved ? CrosshairMode.Default : _crosshairMode;
    }
}
