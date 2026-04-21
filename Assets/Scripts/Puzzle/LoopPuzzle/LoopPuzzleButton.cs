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
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");

    private static readonly Color ActiveBase   = new Color(0f,    1f,    0.3f,  1f);
    private static readonly Color InactiveBase = new Color(0.05f, 0.05f, 0.05f, 1f);
    private static readonly Color LockedBase   = new Color(0.25f, 0.1f,  0f,    1f);

    [Header("Interaction")]
    [SerializeField] private string _interactText       = "Нажать";
    [SerializeField] private string _lockedInteractText = "Заблокировано";

    [Header("Indicator")]
    [SerializeField] private Renderer _indicatorRenderer;
    [Tooltip("HDR emission color when the button is ON. Values above 1 activate Bloom.")]
    [SerializeField] private Color _activeEmissionColor = new Color(0f, 4f, 0.5f);
    [Tooltip("HDR emission color when the button is locked.")]
    [SerializeField] private Color _lockedEmissionColor = new Color(2f, 0.4f, 0f);

    private Material _indicatorMaterial;

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

    /// <summary>Locks or unlocks the button. Locked buttons ignore Interact() and glow orange.</summary>
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
            _indicatorMaterial.SetColor(BaseColorId,     LockedBase);
            _indicatorMaterial.SetColor(EmissionColorId, _lockedEmissionColor);
        }
        else if (IsActive)
        {
            _indicatorMaterial.SetColor(BaseColorId,     ActiveBase);
            _indicatorMaterial.SetColor(EmissionColorId, _activeEmissionColor);
        }
        else
        {
            _indicatorMaterial.SetColor(BaseColorId,     InactiveBase);
            _indicatorMaterial.SetColor(EmissionColorId, Color.black);
        }
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact()
    {
        if (IsLocked) return;
        Toggle();
    }

    public bool IsPickable()        => false;
    public bool UseLMBClick         => true;
    public string GetInteractText() => IsLocked ? _lockedInteractText : _interactText;
}

