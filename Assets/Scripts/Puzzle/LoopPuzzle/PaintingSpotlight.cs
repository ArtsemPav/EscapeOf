using System;
using UnityEngine;

/// <summary>
/// Ceiling spotlight above a painting niche. Power state is driven by LoopPuzzlePowerCircuit.
///
/// Normal spotlights (L1, L2, L4) have a lens slot — SpotlightLensButton calls SetLens().
/// Synthesis spotlight (L3) has no lens; assign L2 and L4 in _synthesisInputs.
///   Blue + Yellow = Green effective color; any other combination = None (dirty white).
/// The optional _beamRenderer (cylinder/cone child) is tinted with the lens color via
/// MaterialPropertyBlock so the player can see the beam color at a glance.
/// Light color is fixed � configured via the Light component in the Inspector.
/// </summary>
public class PaintingSpotlight : MonoBehaviour
{
    // lightRayR.mat uses a custom beam shader with "_Color" as the HDR beam tint (e.g. r:8 = bright red).
    private static readonly int BeamColorId = Shader.PropertyToID("_Color");

    [Header("Light")]
    [SerializeField] private Light _spotLight;
    [SerializeField] private float _poweredIntensity = 3f;

    [Header("Beam Visual")]
    [Tooltip("Renderer on a cylinder/cone child. Tinted with the active lens color when powered.")]
    [SerializeField] private Renderer _beamRenderer;

    [Header("Lens Colors")]
    [SerializeField] private Color _redColor    = new Color(1f, 0.15f, 0.1f);
    [SerializeField] private Color _blueColor   = new Color(0.1f, 0.4f, 1f);
    [SerializeField] private Color _yellowColor = new Color(1f, 0.92f, 0f);
    [SerializeField] private Color _greenColor  = new Color(0.1f, 1f, 0.3f);
    [SerializeField] private Color _dirtyColor  = new Color(0.9f, 0.85f, 0.7f);

    [Header("Synthesis (L3 only)")]
    [Tooltip("Leave empty for normal spotlights (L1, L2, L4). " +
             "For L3 assign L2 and L4 — Blue + Yellow = Green; anything else = None.")]
    [SerializeField] private PaintingSpotlight[] _synthesisInputs = Array.Empty<PaintingSpotlight>();

    /// <summary>Fired whenever the effective color of this spotlight changes.</summary>
    public event Action OnLensChanged;

    /// <summary>The lens currently installed on this spotlight (always None for synthesis spotlights).</summary>
    public LensColor CurrentLens { get; private set; }

    /// <summary>Whether this spotlight is currently powered on.</summary>
    public bool IsPowered { get; private set; }

    private MaterialPropertyBlock _beamPropertyBlock;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _beamPropertyBlock = new MaterialPropertyBlock();

        if (_spotLight == null)
            _spotLight = GetComponentInChildren<Light>();

        ApplyPowerState(false);
    }

    private void OnEnable()
    {
        foreach (var input in _synthesisInputs)
            if (input != null)
                input.OnLensChanged += OnSynthesisInputChanged;
    }

    private void OnDisable()
    {
        foreach (var input in _synthesisInputs)
            if (input != null)
                input.OnLensChanged -= OnSynthesisInputChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Installs a new lens and updates the light color if powered.
    /// Do not call on synthesis spotlights (L3) — their color is computed automatically.
    /// </summary>
    public void SetLens(LensColor color)
    {
        if (CurrentLens == color) return;
        CurrentLens = color;
        if (IsPowered) ApplyEffectiveColor();
        OnLensChanged?.Invoke();
    }

    /// <summary>Sets the powered state. Called by LoopPuzzlePowerCircuit.</summary>
    public void SetPowered(bool powered)
    {
        if (IsPowered == powered) return;
        IsPowered = powered;
        ApplyPowerState(powered);
    }

    /// <summary>
    /// Returns the effective color emitted by this spotlight.
    /// For synthesis spotlights (L3): Blue + Yellow from inputs = Green; otherwise None.
    /// For normal spotlights: returns CurrentLens directly.
    /// </summary>
    public LensColor GetEffectiveColor()
    {
        if (_synthesisInputs == null || _synthesisInputs.Length == 0)
            return CurrentLens;

        bool hasBlue   = false;
        bool hasYellow = false;

        foreach (var input in _synthesisInputs)
        {
            if (input == null) continue;
            var c = input.GetEffectiveColor();
            if (c == LensColor.Blue)   hasBlue   = true;
            if (c == LensColor.Yellow) hasYellow = true;
        }

        return (hasBlue && hasYellow) ? LensColor.Green : LensColor.None;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void OnSynthesisInputChanged()
    {
        if (IsPowered) ApplyEffectiveColor();
        OnLensChanged?.Invoke();   // propagate — LoopPuzzleController re-evaluates conditions
    }

    private void ApplyPowerState(bool powered)
    {
        if (_spotLight == null) return;

        if (!powered)
        {
            _spotLight.intensity = 0f;
            if (_beamRenderer != null)
                _beamRenderer.enabled = false;
            return;
        }

        _spotLight.intensity = _poweredIntensity;
        if (_beamRenderer != null)
            _beamRenderer.enabled = true;

        ApplyEffectiveColor();
    }

    private void ApplyEffectiveColor()
    {
        if (_spotLight == null) return;
        var color = LensColorToUnityColor(GetEffectiveColor());
        _spotLight.color = color;
        UpdateBeamColor(color);
    }

    /// <summary>Tints the beam cone with HDR color matching the lens (x8 to match shader's original intensity).</summary>
    private void UpdateBeamColor(Color color)
    {
        if (_beamRenderer == null) return;
        _beamRenderer.GetPropertyBlock(_beamPropertyBlock);
        _beamPropertyBlock.SetColor(BeamColorId, color * 8f);
        _beamRenderer.SetPropertyBlock(_beamPropertyBlock);
    }

    private Color LensColorToUnityColor(LensColor lens) => lens switch
    {
        LensColor.Red    => _redColor,
        LensColor.Blue   => _blueColor,
        LensColor.Yellow => _yellowColor,
        LensColor.Green  => _greenColor,
        _                => _dirtyColor
    };
}
