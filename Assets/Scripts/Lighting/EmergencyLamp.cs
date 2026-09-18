using UnityEngine;

/// <summary>
/// Lamp that reacts to power status. Behavior depends on _activeWhenPowered:
/// - _activeWhenPowered = false (default): lamp ON when power OFF (emergency mode).
/// - _activeWhenPowered = true: lamp ON when power ON (normal light).
///
/// Place on the lamp root GameObject. Auto-finds <c>Point Light</c> and
/// <c>LampGlass</c> in children. Registers as an <see cref="IPowerConsumer"/>
/// with <see cref="LightingSystem"/>.
///
/// Emergency lamps (_activeWhenPowered = false) share a single material
/// (lightRed.mat) — emission is toggled on the shared material so every
/// emergency lamp reacts at once. Lamps with _activeWhenPowered = true get
/// an instantiated material to avoid conflicts.
/// </summary>
public class EmergencyLamp : MonoBehaviour, IPowerConsumer
{
    [Header("Behavior")]
    [Tooltip("If true — lamp is active when power is ON (normal light). " +
             "If false — lamp is active when power is OFF (emergency mode, default).")]
    [SerializeField] private bool _activeWhenPowered = false;

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

            // Lamps with _activeWhenPowered = true need their own material instance
            // to avoid conflicting with emergency lamps on the shared material.
            if (_activeWhenPowered)
            {
                _sharedMaterial = Instantiate(_sharedMaterial);
                _lampGlassRenderer.sharedMaterial = _sharedMaterial;
                _originalEmissionColor = _sharedMaterial.GetColor("_EmissionColor");
            }
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
        bool shouldBeActive = _activeWhenPowered ? isPowered : !isPowered;

        if (_emergencyLight != null)
            _emergencyLight.enabled = shouldBeActive;

        if (_sharedMaterial != null)
        {
            if (shouldBeActive)
            {
                _sharedMaterial.EnableKeyword("_EMISSION");
                _sharedMaterial.SetColor("_EmissionColor", _originalEmissionColor);
            }
            else
            {
                _sharedMaterial.DisableKeyword("_EMISSION");
                _sharedMaterial.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
