using UnityEngine;

/// <summary>
/// Component placed on individual cylinders to allow clicking them while in puzzle mode.
/// </summary>
public class CylinderInteractable : MonoBehaviour, IInteractable
{
    [Header("Interaction Settings")]
    [SerializeField] private string _interactText = "Повернуть";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    private BoardPuzzlePipe _pipe;

    private void Awake()
    {
        if (_pipe == null) _pipe = GetComponent<BoardPuzzlePipe>();

        if (_pipe == null)
            Debug.LogError($"[{nameof(CylinderInteractable)}] BoardPuzzlePipe not found on {gameObject.name}.", this);
    }

    /// <summary>Rotates the cylinder when the player clicks it.</summary>
    public void Interact()
    {
        if (_pipe != null && !_pipe.IsLocked)
        {
            _pipe.Rotate();
        }
        else if (_pipe == null)
        {
            Debug.LogError($"[{nameof(CylinderInteractable)}] BoardPuzzlePipe reference is NULL on {gameObject.name}!");
        }
    }

    /// <summary>
    /// Returns false if the pipe is locked (puzzle solved), 
    /// preventing any interaction and hiding UI prompts.
    /// </summary>
    public bool CanInteract() => _pipe != null && !_pipe.IsLocked;

    /// <summary>Returns the hint text shown when hovering over this cylinder in puzzle mode.</summary>
    public string GetInteractText() => _interactText;

    public bool IsPickable() => false;

    /// <summary>LMB click triggers Interact() in addition to the E key.</summary>
    public bool UseLMBClick => true;

    /// <summary>Returns the crosshair icon to display when hovering over this cylinder.</summary>
    public CrosshairMode GetCrosshairMode() => _crosshairMode;
}
