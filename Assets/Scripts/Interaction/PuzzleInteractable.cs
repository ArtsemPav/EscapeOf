using UnityEngine;

/// <summary>
/// Handles player interaction to trigger PuzzleModeController.
/// Decouples the interaction logic from the puzzle management.
/// </summary>
public class PuzzleInteractable : MonoBehaviour, IInteractable
{
    [Header("Interaction Settings")]
    [SerializeField] private string _interactText = "Осмотреть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    [Header("References")]
    [SerializeField] private PuzzleModeController _controller;

    private void Awake()
    {
        if (_controller == null)
        {
            _controller = GetComponent<PuzzleModeController>();
        }

        if (_controller == null)
        {
            Debug.LogError($"[{nameof(PuzzleInteractable)}] PuzzleModeController not found on {gameObject.name}.", this);
        }
    }

    public bool CanInteract()
    {
        if (_controller == null) return false;
        return !_controller.IsActive && !_controller.IsSolved;
    }

    public void Interact()
    {
        if (CanInteract())
        {
            _controller.EnterPuzzleMode();
        }
    }

    public string GetInteractText()
    {
        if (_controller != null && _controller.IsSolved) return string.Empty;
        return _interactText;
    }

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode()
    {
        if (_controller != null && _controller.IsSolved) return CrosshairMode.Default;
        return _crosshairMode;
    }
}
