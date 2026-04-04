using UnityEngine;

/// <summary>
/// Placed on the gauge GameObject (e.g. "screen").
/// When the player interacts with it, commits the current lever state to the arrow
/// and checks whether the puzzle is solved.
///
/// Only meaningful when PressurePuzzle._confirmOnInteract is enabled.
/// In real-time mode Confirm() is a harmless no-op.
/// </summary>
public class PressureGauge : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private string _interactText = "Проверить давление";

    private PressurePuzzle _puzzle;

    private void Awake()
    {
        _puzzle = GetComponentInParent<PressurePuzzle>();
    }

    /// <summary>
    /// The gauge is only interactable when confirm mode is active and the puzzle is not yet solved.
    /// With _confirmOnInteract disabled the arrow tracks levers in real-time, so interacting
    /// with the gauge is meaningless and should produce no UI response.
    /// </summary>
    public bool CanInteract() => _puzzle != null && _puzzle.ConfirmOnInteract && !_puzzle.IsSolved;

    /// <summary>Commits lever states and checks for a solution.</summary>
    public void Interact()
    {
        _puzzle?.Confirm();
    }

    public string GetInteractText() => _puzzle != null && _puzzle.IsSolved
        ? string.Empty
        : _interactText;

    public bool IsPickable() => false;
    public bool UseLMBClick => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
}
