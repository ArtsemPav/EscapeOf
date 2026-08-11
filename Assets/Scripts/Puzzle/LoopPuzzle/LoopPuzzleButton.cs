using System;
using UnityEngine;

/// <summary>
/// Toggle button with a glowing emission indicator. Used for power switches S1–S6.
/// Fires OnToggled with the new state on each interaction.
/// Supports locking (e.g. S6 master locked until the unlock sequence is entered).
/// </summary>
public class LoopPuzzleButton : MonoBehaviour, IInteractable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionMapId   = Shader.PropertyToID("_EmissionMap");
    private static readonly int BaseMapId       = Shader.PropertyToID("_BaseMap");

    [Header("Interaction")]
    [SerializeField] private string _interactText       = "Нажать";
    [SerializeField] private string _lockedInteractText = "Заблокировано";
    [SerializeField] private string _noPowerText        = "Нет электричества";

    [Header("Indicator")]
    [SerializeField] private Renderer _indicatorRenderer;

    [Header("Emission — ON")]
    [Tooltip("HDR emission color when the button is ON. Values above 1 activate Bloom.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _activeEmissionColor = new Color(0f, 4f, 0.5f, 1f);

    [Header("Emission — Locked")]
    [Tooltip("HDR emission color when the button is locked. Set to black to suppress glow.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _lockedEmissionColor = Color.black;

    private Material _indicatorMaterial;
    private Texture  _albedoTexture;

    /// <summary>Current active state of this button.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When true, the button ignores Interact() calls.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Raised when the button is toggled. Passes the new state.</summary>
    public event Action<bool> OnToggled;

    private void Awake()
    {
        if (_indicatorRenderer != null)
        {
            _indicatorMaterial = _indicatorRenderer.material;
            _indicatorMaterial.EnableKeyword("_EMISSION");
            _albedoTexture = _indicatorMaterial.GetTexture(BaseMapId);
        }

        ApplyVisual();
    }

    // ── State control ──────────────────────────────────────────────────────────

    /// <summary>Sets the button state silently (no event). Used when restoring from save.</summary>
    public void SetStateSilent(bool active)
    {
        IsActive = active;
        ApplyVisual();
    }

    /// <summary>
    /// Toggles the button and updates the visual without firing OnToggled.
    /// Used by LightsOutPanel for cascade neighbor toggling.
    /// </summary>
    public void ToggleSilent()
    {
        IsActive = !IsActive;
        ApplyVisual();
    }

    /// <summary>Locks or unlocks the button. Visual state updates immediately.</summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        ApplyVisual();
    }

    private void Toggle()
    {
        IsActive = !IsActive;
        ApplyVisual();
        OnToggled?.Invoke(IsActive);
    }

    private void ApplyVisual()
    {
        if (_indicatorMaterial == null) return;

        if (IsLocked)
        {
            _indicatorMaterial.SetTexture(EmissionMapId, null);
            _indicatorMaterial.SetColor(EmissionColorId, _lockedEmissionColor);
        }
        else if (IsActive)
        {
            _indicatorMaterial.SetTexture(EmissionMapId, _albedoTexture);
            _indicatorMaterial.SetColor(EmissionColorId, _activeEmissionColor);
        }
        else
        {
            _indicatorMaterial.SetTexture(EmissionMapId, null);
            _indicatorMaterial.SetColor(EmissionColorId, Color.black);
        }
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact()
    {
        // General power off (enabled set by LoopPuzzleController.OnPowerStateChanged).
        // The button press animation still plays so the player sees the button respond.
        if (!enabled) return;
        if (IsLocked) return;
        Toggle();
    }

    public bool IsPickable() => false;
    public bool UseLMBClick  => true;
    public string GetInteractText()
    {
        if (!enabled)       return _noPowerText;
        if (IsLocked)       return _lockedInteractText;
        return _interactText;
    }
}
