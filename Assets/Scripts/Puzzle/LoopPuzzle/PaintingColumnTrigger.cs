using UnityEngine;

/// <summary>
/// Button in the player's control room that advances the height of two linked paintings.
/// Each press: primaryColumn advances by one step, linkedColumn advances by one step.
/// Assign columns Q[n] and Q[n+1] in the Inspector to form the cyclic chain.
/// </summary>
public class PaintingColumnTrigger : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private string _interactText       = "Нажать";
    [SerializeField] private string _lockedInteractText = "Заблокировано";

    [Header("Linked Columns")]
    [Tooltip("The primary painting column this button controls.")]
    [SerializeField] private PaintingColumn _primaryColumn;
    [Tooltip("The next column in the cycle that also advances on press.")]
    [SerializeField] private PaintingColumn _linkedColumn;

    private bool _isLocked;

    /// <summary>Locks or unlocks the button. When locked, Interact() is ignored.</summary>
    public void SetLocked(bool locked) => _isLocked = locked;

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact()
    {
        if (_isLocked) return;
        _primaryColumn?.AdvanceHeight();
        _linkedColumn?.AdvanceHeight();
    }

    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public string GetInteractText() => _isLocked ? _lockedInteractText : _interactText;
}
