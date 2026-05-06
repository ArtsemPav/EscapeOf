using UnityEngine;

/// <summary>
/// A simple implementation of IInteractable that always allows interaction.
/// Useful for objects inside puzzles that only need to show a hint and trigger an event.
/// </summary>
public class SimpleInteractable : MonoBehaviour, IInteractable
{
    [Header("UI Feedback")]
    [SerializeField] private string _idleHintText = "Взаимодействовать";
    [SerializeField] private string _activeHintText = "Нажать";
    [SerializeField] private CrosshairMode _idleCrosshair = CrosshairMode.Hand;
    [SerializeField] private CrosshairMode _activeCrosshair = CrosshairMode.Grab;
    [SerializeField] private bool _isPickable = false;

    [Header("Events")]
    [SerializeField] private UnityEngine.Events.UnityEvent _onInteract;

    public bool CanInteract() => true;

    public void Interact()
    {
        _onInteract?.Invoke();
    }

    public string GetInteractText()
    {
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
        {
            return _activeHintText;
        }
        return _idleHintText;
    }

    public bool IsPickable() => _isPickable;

    public CrosshairMode GetCrosshairMode()
    {
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
        {
            return _activeCrosshair;
        }
        return _idleCrosshair;
    }

    /// <summary>
    /// If true, FPSController will use LMB instead of E. 
    /// PuzzleCursor always uses LMB regardless of this setting.
    /// </summary>
    public bool UseLMBClick => true;
}
