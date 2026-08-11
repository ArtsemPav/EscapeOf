using UnityEngine;

/// <summary>
/// Interactable button that cycles through cameras on the TV screen.
/// Attach to any GameObject on the Interactable Layer with a collider.
/// Assign <see cref="_controller"/> to the PeepholeTVCamera that manages the cameras.
/// </summary>
public class TVChannelButton : MonoBehaviour, IInteractable
{
    [SerializeField] private string            _interactText  = "Переключить камеру";
    [SerializeField] private string            _noPowerText   = "Нет электричества";
    [SerializeField] private PeepholeTVCamera  _controller;

    /// <summary>Advances to the next camera on the TV.</summary>
    public void Interact()
    {
        // General power off (enabled set by LoopPuzzleController.OnPowerStateChanged).
        // The button press animation still plays so the player sees the button respond.
        if (!enabled) return;
        _controller?.NextCamera();
    }

    public bool   IsPickable()      => false;
    public bool   UseLMBClick       => true;
    public string GetInteractText() => enabled ? _interactText : _noPowerText;
}
