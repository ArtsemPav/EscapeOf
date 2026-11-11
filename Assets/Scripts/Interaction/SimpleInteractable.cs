using UnityEngine;

/// <summary>
/// A simple implementation of IInteractable that always allows interaction.
/// Useful for objects inside puzzles that only need to show a hint and trigger an event.
/// </summary>
public class SimpleInteractable : MonoBehaviour, IInteractable
{
    [Header("UI Feedback")]
    [Tooltip("Localization key for the idle hint. Override per instance for a specific label (e.g. 'Свернуть').")]
    [SerializeField] private string _idleHintKey = LocalizationKeys.InteractHorrorDefault;
    [Tooltip("Localization key for the pressed hint. Override per instance for a specific label (e.g. 'Вращать').")]
    [SerializeField] private string _activeHintKey = LocalizationKeys.InteractPress;
    [SerializeField] private CrosshairMode _idleCrosshair = CrosshairMode.Hand;
    [SerializeField] private CrosshairMode _activeCrosshair = CrosshairMode.Grab;
    private bool _isPickable = false;

    [Header("Events")]
    [SerializeField] private UnityEngine.Events.UnityEvent _onInteract;

    public bool CanInteract() => true;

    public void Interact()
    {
        _onInteract?.Invoke();
    }

    /// <summary>Returns the localized idle/active hint.</summary>
    public string GetInteractText()
    {
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
        {
            return Localization.Get(string.IsNullOrEmpty(_activeHintKey)
                ? LocalizationKeys.InteractPress
                : _activeHintKey);
        }
        return Localization.Get(string.IsNullOrEmpty(_idleHintKey)
            ? LocalizationKeys.InteractHorrorDefault
            : _idleHintKey);
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
