using UnityEngine;

/// <summary>
/// Emergency indicator lamp that visually signals power status.
/// - Red light ON + emission ON  → power is OFF (no electricity).
/// - Red light OFF + emission OFF → power is ON  (electricity available).
///
/// Place on the lamp root GameObject. Auto-finds <c>Point Light</c> and
/// <c>LampGlass</c> in children. Registers as an <see cref="IPowerConsumer"/>
/// with <see cref="LightingSystem"/>.
///
/// All lamps share a single material (lightRed.mat). The emission is toggled
/// directly on the shared material — every lamp reacts at once.
/// </summary>
public class EmergencyLamp : MonoBehaviour, IPowerConsumer
{
    [Header("References")]
    [Tooltip("Red emergency light. Auto-found in children if not assigned.")]
    [SerializeField] private Light _emergencyLight;

    [Tooltip("Renderer on the LampGlass mesh. Auto-found in children if not assigned.")]
    [SerializeField] private Renderer _lampGlassRenderer;

    private Material _sharedMaterial;
    private Color _originalEmissionColor;

    private void Awake()
    {
        if (_emergencyLight == null)
            _emergencyLight = GetComponentInChildren<Light>(true);

        if (_lampGlassRenderer == null)
        {
            var lampGlass = transform.Find("LampGlass");
            if (lampGlass != null)
                _lampGlassRenderer = lampGlass.GetComponent<Renderer>();
        }

        if (_lampGlassRenderer != null && _lampGlassRenderer.sharedMaterial != null)
        {
            _sharedMaterial = _lampGlassRenderer.sharedMaterial;
            _originalEmissionColor = _sharedMaterial.GetColor("_EmissionColor");
        }

        LightingSystem.Instance?.RegisterConsumer(this);
    }

    private void OnDestroy()
    {
        LightingSystem.Instance?.UnregisterConsumer(this);
    }

    /// <summary>
    /// Called by LightingSystem when master power changes.
    /// Emergency lamp lights up when power is OFF; goes dark when power is ON.
    /// Modifies the shared material directly so all lamps react simultaneously.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        if (_emergencyLight != null)
            _emergencyLight.enabled = !isPowered;

        if (_sharedMaterial != null)
        {
            if (isPowered)
            {
                _sharedMaterial.DisableKeyword("_EMISSION");
                _sharedMaterial.SetColor("_EmissionColor", Color.black);
            }
            else
            {
                _sharedMaterial.EnableKeyword("_EMISSION");
                _sharedMaterial.SetColor("_EmissionColor", _originalEmissionColor);
            }
        }
    }
}
