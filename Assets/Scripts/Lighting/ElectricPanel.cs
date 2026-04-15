using UnityEngine;

/// <summary>
/// Interactable electric panel (щиток) that controls master power for all lights.
/// When power is cut — all zones go dark regardless of their individual switch states.
/// When power is restored — each zone returns to its own switch state.
/// 
/// Saving is handled by LightingSystem (which persists both power state and per-zone switches).
/// </summary>
public class ElectricPanel : MonoBehaviour, IInteractable
{
    [Tooltip("Text shown when looking at the panel while power is ON.")]
    [SerializeField] private string _hintPoweredOn = "Щиток [Питание ВКЛ]";

    [Tooltip("Text shown when looking at the panel while power is OFF.")]
    [SerializeField] private string _hintPoweredOff = "Щиток [Питание ВЫКЛ]";

    [Header("Visuals")]
    [Tooltip("Indicator light that glows when power is active (e.g. a small emissive mesh renderer).")]
    [SerializeField] private Renderer _powerIndicator;

    [Tooltip("Material used for the indicator when power is ON.")]
    [SerializeField] private Material _indicatorOnMaterial;

    [Tooltip("Material used for the indicator when power is OFF.")]
    [SerializeField] private Material _indicatorOffMaterial;

    [Header("Audio")]
    [SerializeField] private AudioClip _powerOnClip;
    [SerializeField] private AudioClip _powerOffClip;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract() => LightingSystem.Instance != null;

    public void Interact()
    {
        if (LightingSystem.Instance == null) return;

        LightingSystem.Instance.TogglePower();
        bool isPowered = LightingSystem.Instance.IsPowered;
        UpdateIndicator(isPowered);
        PlayAudio(isPowered);
    }

    public string GetInteractText()
    {
        if (LightingSystem.Instance == null) return _hintPoweredOn;
        return LightingSystem.Instance.IsPowered ? _hintPoweredOn : _hintPoweredOff;
    }

    public bool IsPickable() => false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (LightingSystem.Instance != null)
        {
            UpdateIndicator(LightingSystem.Instance.IsPowered);
            LightingSystem.Instance.OnPowerChanged += OnPowerChanged;
        }
    }

    private void OnDestroy()
    {
        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnPowerChanged -= OnPowerChanged;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void OnPowerChanged(bool isPowered)
    {
        UpdateIndicator(isPowered);
        // Audio is played from Interact() directly; here we just sync visuals
        // in case power state was changed programmatically (e.g. scripted event).
    }

    private void UpdateIndicator(bool isPowered)
    {
        if (_powerIndicator == null) return;
        var mat = isPowered ? _indicatorOnMaterial : _indicatorOffMaterial;
        if (mat != null)
            _powerIndicator.material = mat;
    }

    private void PlayAudio(bool isPowered)
    {
        var clip = isPowered ? _powerOnClip : _powerOffClip;
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip);
    }
}
