using UnityEngine;

/// <summary>
/// A simple implementation of IInteractable that always allows interaction.
/// Useful for objects inside puzzles that only need to show a hint and trigger an event.
/// </summary>
public class SimpleInteractable : MonoBehaviour, IInteractable
{
    [Header("Settings")]
    [SerializeField] private string _interactText = "Взаимодействовать";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;
    [SerializeField] private bool _isPickable = false;

    [Header("Events")]
    [SerializeField] private UnityEngine.Events.UnityEvent _onInteract;

    public bool CanInteract() => enabled;

    public void Interact()
    {
        _onInteract?.Invoke();
    }

    public string GetInteractText() => _interactText;

    public bool IsPickable() => _isPickable;

    public CrosshairMode GetCrosshairMode() => _crosshairMode;

    /// <summary>
    /// If true, FPSController will use LMB instead of E. 
    /// PuzzleCursor always uses LMB regardless of this setting.
    /// </summary>
    public bool UseLMBClick => true;
}
