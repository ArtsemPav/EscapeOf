using UnityEngine;

/// <summary>
/// Component placed on individual cylinders to allow clicking them while in puzzle mode.
/// </summary>
public class CylinderInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private BoardPuzzlePipe _pipe;

    private void Awake()
    {
        if (_pipe == null) _pipe = GetComponent<BoardPuzzlePipe>();
    }

    // Only allow interaction if the puzzle mode is active
    public bool CanInteract() => true;

    public void Interact()
    {
        Debug.Log($"[{gameObject.name}] CylinderInteractable.Interact() triggered.");
        if (_pipe != null)
        {
            _pipe.Rotate();
        }
        else
        {
            Debug.LogError($"[{gameObject.name}] CylinderInteractable: BoardPuzzlePipe reference is NULL!");
        }
    }

    public string GetInteractText() => ""; // No text needed inside the puzzle view
    public bool IsPickable() => false;
    public bool UseLMBClick => true; // Essential for puzzle clicking
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
}
