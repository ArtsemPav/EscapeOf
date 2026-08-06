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

    [Header("Hints")]
    [Tooltip("Text shown when looking at the panel before the electric puzzle has been solved.")]
    [SerializeField] private string _hintNotActivated = "Щиток [Нет питания]";

    // ── IInteractable ─────────────────────────────────────────────────────────

    /// <summary>
    /// The breaker can only be toggled after the electric panel puzzle has
    /// activated power at least once AND the generator is still running.
    /// </summary>
    public bool CanInteract()
    {
        var ls = LightingSystem.Instance;
        return ls != null && ls.IsPowerActivated && ls.IsGeneratorReady;
    }

    public void Interact()
    {
        if (LightingSystem.Instance == null) return;
        if (!LightingSystem.Instance.IsPowerActivated) return;
        if (!LightingSystem.Instance.IsGeneratorReady) return;

        LightingSystem.Instance.TogglePower();
        bool isPowered = LightingSystem.Instance.IsPowered;
        UpdateIndicator(isPowered);
        PlayAudio(isPowered);
    }

    public string GetInteractText()
    {
        var ls = LightingSystem.Instance;
        if (ls == null) return _hintPoweredOn;

        if (!ls.IsPowerActivated || !ls.IsGeneratorReady)
            return _hintNotActivated;

        return ls.IsPowered ? _hintPoweredOn : _hintPoweredOff;
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
