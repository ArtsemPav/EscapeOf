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
        if (_controller.IsActive) return false;
        // Solved puzzles are usually closed — unless re-entry is allowed
        // (e.g. the paint puzzle TV with hints for the next puzzle).
        return _controller.AllowEnterWhenSolved || !_controller.IsSolved;
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
        if (_controller == null) return _interactText;
        if (_controller.IsSolved && !_controller.AllowEnterWhenSolved) return string.Empty;
        return _interactText;
    }

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode()
    {
        if (_controller == null) return _crosshairMode;
        if (_controller.IsSolved && !_controller.AllowEnterWhenSolved) return CrosshairMode.Default;
        return _crosshairMode;
    }
}
