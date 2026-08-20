using UnityEngine;

/// <summary>
/// Lightweight IInteractable placed on the LeftSide and RightSide colliders
/// of the final door. When the player interacts with a side, enters puzzle
/// mode and switches to the corresponding camera.
///
/// FPSController finds this via TryGetComponent on the side's collider — it
/// takes priority over FinalDoorPuzzleController's own IInteractable on the
/// root, so the controller never needs to know which side was clicked.
/// </summary>
public class FinalDoorSideInteractable : MonoBehaviour, IInteractable
{
    [Header("Controller")]
    [Tooltip("Auto-found via GetComponentInParent if empty.")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Side")]
    [Tooltip("Which camera to switch to when this side is interacted with.")]
    [SerializeField] private FinalDoorPuzzleController.CameraId _cameraId;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Осмотреть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponentInParent<FinalDoorPuzzleController>();
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract()
    {
        return _controller != null && !_controller.IsActive && !_controller.IsSolved;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        _controller.EnterPuzzleMode();
        _controller.SwitchCamera(_cameraId);
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
