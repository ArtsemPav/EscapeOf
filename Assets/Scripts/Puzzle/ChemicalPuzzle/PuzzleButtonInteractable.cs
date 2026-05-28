using UnityEngine;

/// <summary>
/// Minimal IInteractable for physical puzzle buttons (button1, button2).
/// Supports both FPSController (which calls Play() separately) and PuzzleCursor
/// (which only calls Interact()), so Play() is triggered from Interact() directly.
/// </summary>
public class PuzzleButtonInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string _hintText = "Нажать";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Point;

    private ButtonPressAnimation _pressAnimation;

    private void Awake()
    {
        _pressAnimation = GetComponent<ButtonPressAnimation>();
    }

    /// <summary>Always interactable — device guard logic lives in the device controller.</summary>
    public bool CanInteract() => true;

    /// <summary>
    /// Triggers the button press animation. Called by PuzzleCursor directly.
    /// FPSController also calls Play() separately, which is a no-op if already playing.
    /// </summary>
    public void Interact()
    {
        _pressAnimation?.Play();
    }

    public string GetInteractText() => _hintText;
    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => _crosshairMode;
}
