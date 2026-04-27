using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies an emission fade to the clock hand's material instance while the cursor hovers over it.
/// Uses a per-instance material so the shared material asset is never modified.
/// Attach alongside <see cref="ClockHand"/> on the same GameObject.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(ClockHand))]
public class ClockHandHighlight : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Tooltip("Emission color applied while the cursor hovers over this hand.")]
    [SerializeField] private Color _emissionColor = new Color(1f, 0.85f, 0.4f);

    [Tooltip("Emission HDR intensity multiplier (applied via ColorSpaceAdjusted trick).")]
    [SerializeField, Min(0f)] private float _emissionIntensity = 0.6f;

    [Tooltip("Speed of the fade in / fade out in units per second.")]
    [SerializeField, Min(0.01f)] private float _fadeSpeed = 8f;

    // ── State ──────────────────────────────────────────────────────────────────

    private MeshRenderer _renderer;
    private ClockHand    _hand;
    private Material     _materialInstance;
    private float        _currentWeight;   // 0 = off, 1 = fully lit
    private bool         _isActive;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _hand     = GetComponent<ClockHand>();

        // Create a per-instance material so the shared asset is not touched.
        _materialInstance = _renderer.material; // Unity auto-instantiates on first access via .material
        _materialInstance.EnableKeyword(EmissionKeyword);
        ApplyEmission(0f);
    }

    private void OnEnable()
    {
        _hand.OnHoverChanged += HandleHoverChanged;
    }

    private void OnDisable()
    {
        _hand.OnHoverChanged -= HandleHoverChanged;
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
            Destroy(_materialInstance);
    }

    private void Update()
    {
        float target = _isActive ? 1f : 0f;

        if (Mathf.Approximately(_currentWeight, target)) return;

        _currentWeight = Mathf.MoveTowards(_currentWeight, target, _fadeSpeed * Time.deltaTime);
        ApplyEmission(_currentWeight);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void HandleHoverChanged(bool hovered)
    {
        _isActive = hovered;
    }

    private void ApplyEmission(float weight)
    {
        Color final = _emissionColor * (_emissionIntensity * weight);
        _materialInstance.SetColor(EmissionColorId, final);
    }
}
