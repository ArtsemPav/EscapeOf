using UnityEngine;

/// <summary>
/// Placed on the gauge GameObject (e.g. "screen").
/// Now purely visual — the arrow tracks pressure in real-time.
/// Kept for backward compatibility but no longer interactive.
/// </summary>
public class PressureGauge : MonoBehaviour, IInteractable
{
    public bool CanInteract() => false;

    public void Interact() { }

    public string GetInteractText() => string.Empty;

    public bool IsPickable() => false;
    public bool UseLMBClick => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
}
