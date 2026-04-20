using System;
using UnityEngine;

/// <summary>
/// Toggle button with a glowing emission indicator. Used for power switches S1–S6.
/// Fires OnToggled with the new state on each interaction.
/// </summary>
public class LoopPuzzleButton : MonoBehaviour, IInteractable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");

    private static readonly Color ActiveBase    = new Color(0f,    1f,   0.3f, 1f);
    private static readonly Color InactiveBase  = new Color(0.05f, 0.05f, 0.05f, 1f);

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Нажать";

    [Header("Indicator")]
    [SerializeField] private Renderer _indicatorRenderer;
    [Tooltip("HDR emission color when the button is ON. Values above 1 activate Bloom.")]
    [SerializeField] private Color _activeEmissionColor = new Color(0f, 4f, 0.5f);

    private Material _indicatorMaterial;

    /// <summary>Current active state of this button.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Raised when the button is toggled. Passes the new state.</summary>
    public event Action<bool> OnToggled;

    private void Awake()
    {
        if (_indicatorRenderer != null)
        {
            _indicatorMaterial = _indicatorRenderer.material;
            _indicatorMaterial.EnableKeyword("_EMISSION");
        }

        ApplyEmission(IsActive);
    }

    /// <summary>Sets the button state silently (no event). Used when restoring from save.</summary>
    public void SetStateSilent(bool active)
    {
        IsActive = active;
        ApplyEmission(active);
    }

    private void Toggle()
    {
        IsActive = !IsActive;
        ApplyEmission(IsActive);
        OnToggled?.Invoke(IsActive);
    }

    private void ApplyEmission(bool active)
    {
        if (_indicatorMaterial == null) return;
        _indicatorMaterial.SetColor(BaseColorId,     active ? ActiveBase                : InactiveBase);
        _indicatorMaterial.SetColor(EmissionColorId, active ? _activeEmissionColor      : Color.black);
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact() => Toggle();
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public string GetInteractText() => _interactText;
}

