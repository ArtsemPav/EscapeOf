using UnityEngine;

/// <summary>
/// Interaction for a bell: plays its sound on LMB click and prevents spam —
/// the bell cannot be rung again until the current sound finishes playing.
/// </summary>
public class BellRingInteraction : MonoBehaviour, IInteractable
{
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;

    [Header("UI Feedback")]
    [Tooltip("Localization key for the idle hint (e.g. 'Ring the bell').")]
    [SerializeField] private string _hintKey = "ui.interact.bell_ring";
    [SerializeField] private CrosshairMode _crosshair = CrosshairMode.Point;

    /// <summary>True while the bell sound is still playing.</summary>
    public bool IsRinging => _audioSource != null && _audioSource.isPlaying;

    public bool CanInteract() => !IsRinging;

    public void Interact()
    {
        if (IsRinging)
        {
            return;
        }
        _audioSource?.Play();
    }

    /// <summary>Returns the localized hint for the bell.</summary>
    public string GetInteractText() => Localization.Get(string.IsNullOrEmpty(_hintKey) ? "ui.interact.bell_ring" : _hintKey);

    public bool IsPickable() => false;

    public bool UseLMBClick => true;

    public CrosshairMode GetCrosshairMode() => _crosshair;
}
