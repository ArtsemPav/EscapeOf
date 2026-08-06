using UnityEngine;

/// <summary>
/// Interactable button that cycles through cameras on the TV screen.
/// Attach to any GameObject on the Interactable Layer with a collider.
/// Assign <see cref="_controller"/> to the PeepholeTVCamera that manages the cameras.
/// </summary>
public class TVChannelButton : MonoBehaviour, IInteractable
{
    [SerializeField] private string            _interactText = "Переключить камеру";
    [SerializeField] private PeepholeTVCamera  _controller;

    /// <summary>
    /// Returns false when the component is disabled (e.g. by ElectricDevice
    /// when power is off) — prevents interaction while power is cut.
    /// </summary>
    public bool CanInteract() => enabled;

    /// <summary>Advances to the next camera on the TV.</summary>
    public void Interact()
    {
        _controller?.NextCamera();
    }

    public bool   IsPickable()    => false;
    public bool   UseLMBClick     => true;
    public string GetInteractText() => _interactText;
}
