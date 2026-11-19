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

    [Header("Puzzle Mode")]
    [Tooltip("PuzzleModeController this button belongs to. The button only responds while that puzzle mode is active. If not assigned, found automatically via LoopPuzzleController in parents.")]
    [SerializeField] private PuzzleModeController _puzzleMode;

    [Header("Interaction")]
    [Tooltip("Localization key for the idle hint. Override per instance for a specific label (e.g. 'Рубильник S1'). Empty value uses the generic press hint.")]
    [SerializeField] private string _interactTextKey = LocalizationKeys.InteractPress;

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
    private bool     _puzzleModeResolved;

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

    private void Start()
    {
        ResolvePuzzleMode();
    }

    /// <summary>
    /// Finds the owning PuzzleModeController (lazy, cached). The controller sits on
    /// the ControlPanel — a sibling of the button parents — so it is resolved via
    /// LoopPuzzleController on the PaintPuzzle root, not via GetComponentInParent.
    /// </summary>
    private bool ResolvePuzzleMode()
    {
        if (_puzzleMode == null)
        {
            var controller = GetComponentInParent<LoopPuzzleController>(true);
            _puzzleMode = controller != null ? controller.PuzzleMode : null;
        }
        if (_puzzleMode == null)
            _puzzleMode = GetComponentInParent<PuzzleModeController>(true);
        _puzzleModeResolved = _puzzleMode != null;
        return _puzzleModeResolved;
    }

    /// <summary>
    /// True when the owning puzzle mode is active. Buttons without a
    /// PuzzleModeController in parents are always allowed (backwards compatible).
    /// </summary>
    private bool PuzzleModeAllowsInteraction
    {
        get
        {
            if (_puzzleMode == null && !_puzzleModeResolved)
                ResolvePuzzleMode();
            return _puzzleMode == null || _puzzleMode.IsActive;
        }
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
    /// Used by LoopPuzzlePowerCircuit for Lights Out cascade neighbor toggling.
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
        // Puzzle mode gate: the physical buttons only respond while the player
        // is inside the puzzle view (PuzzleModeController.IsActive).
        if (!PuzzleModeAllowsInteraction) return;

        // General power off (enabled set by LoopPuzzleController.OnPowerStateChanged).
        // The button press animation still plays so the player sees the button respond.
        if (!enabled) return;
        if (IsLocked) return;
        Toggle();
    }

    public bool IsPickable() => false;
    public bool UseLMBClick  => true;

    /// <summary>Returns localized hints for power/lock/idle states.</summary>
    public string GetInteractText()
    {
        if (!enabled)       return Localization.Get(LocalizationKeys.InteractNoPower);
        if (IsLocked)       return Localization.Get(LocalizationKeys.InteractLocked);
        if (!PuzzleModeAllowsInteraction) return Localization.Get(LocalizationKeys.InteractInspect);
        return Localization.Get(string.IsNullOrEmpty(_interactTextKey)
            ? LocalizationKeys.InteractPress
            : _interactTextKey);
    }
}
