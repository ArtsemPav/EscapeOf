using UnityEngine;

/// <summary>
/// Binds a lamp mesh's material emission to a lighting zone managed by
/// <see cref="LightingSystem"/>. The glow is on when the zone is lit
/// (master power ON and the zone switch ON) and off otherwise.
///
/// Place on the lamp mesh GameObject (or assign renderers manually).
/// Works without a Light component — pure emissive glow.
///
/// Each assigned renderer gets an instantiated material so lamps sharing
/// NeonLamp.mat (or any other shared material) react independently.
/// Use <see cref="_materialIndices"/> to pick which material slot of each
/// renderer is toggled (e.g. the emissive slot on multi-material lamps).
/// Registers as an <see cref="IPowerConsumer"/> with <see cref="LightingSystem"/>,
/// so it also survives save-load and power restoration automatically.
/// </summary>
public class ZoneEmission : MonoBehaviour, IPowerConsumer
{
    [Header("Zone")]
    [Tooltip("Zone ID to follow. Must match LightZone.ZoneId / LightSwitch.ZoneId. " +
             "Case-sensitive. If empty, the ZoneId of a LightZone in parents is used.")]
    [SerializeField] private string _zoneId;

    [Header("References")]
    [Tooltip("Renderers whose emission follows the zone state. " +
             "Auto-found on this GameObject if not assigned.")]
    [SerializeField] private Renderer[] _renderers;

    [Tooltip("Index of the material slot on each renderer to control. " +
             "Element i corresponds to _renderers[i]. If omitted or out of " +
             "range, slot 0 is used.")]
    [SerializeField] private int[] _materialIndices;

    [Header("Emission")]
    [Tooltip("Emission color multiplied into the emissive map.")]
    [SerializeField] private Color _emissionColor = Color.white;

    [Tooltip("HDR intensity of the glow. Values above 1 bloom when a volume is present.")]
    [SerializeField, Min(0f)] private float _emissionIntensity = 4f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    private LightingSystem _lightingSystem;
    private Material[] _materials;

    /// <summary>Resolved zone ID this component follows.</summary>
    public string ZoneId => _zoneId;

    private void Awake()
    {
        _lightingSystem = LightingSystem.Instance;

        if (string.IsNullOrEmpty(_zoneId))
        {
            var zone = GetComponentInParent<LightZone>();
            if (zone != null)
                _zoneId = zone.ZoneId;
        }

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponents<Renderer>();

        InstantiateMaterials();
    }

    private void Start()
    {
        // RegisterConsumer delivers the current power state immediately,
        // which also performs the initial emission sync.
        _lightingSystem?.RegisterConsumer(this);

        if (_lightingSystem != null)
        {
            _lightingSystem.OnZoneSwitchChanged += HandleZoneSwitchChanged;
            UpdateEmission(_lightingSystem.IsZoneLit(_zoneId));
        }
    }

    private void OnDestroy()
    {
        if (_lightingSystem != null)
        {
            _lightingSystem.OnZoneSwitchChanged -= HandleZoneSwitchChanged;
            _lightingSystem.UnregisterConsumer(this);
        }
    }

    /// <summary>
    /// Called by LightingSystem when master power changes.
    /// Emission reflects the combined power + switch state of the zone.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        UpdateEmission(_lightingSystem != null && _lightingSystem.IsZoneLit(_zoneId));
    }

    private void HandleZoneSwitchChanged(string zoneId, bool isOn)
    {
        if (zoneId != _zoneId) return;
        UpdateEmission(_lightingSystem.IsZoneLit(_zoneId));
    }

    private void InstantiateMaterials()
    {
        if (_renderers == null) return;

        _materials = new Material[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            int slotIndex = GetMaterialIndex(i, renderer);
            var sourceMaterial = renderer.sharedMaterials[slotIndex];
            if (sourceMaterial == null) continue;

            // Instance the material so shared assets (lamps_emit.mat) stay untouched
            // and lamps glow independently of each other.
            var instance = new Material(sourceMaterial);
            var materials = renderer.sharedMaterials;
            materials[slotIndex] = instance;
            renderer.sharedMaterials = materials;
            _materials[i] = instance;
        }
    }

    /// <summary>
    /// Resolved material slot for renderer i (clamped to the renderer's valid range).
    /// </summary>
    private int GetMaterialIndex(int rendererIndex, Renderer renderer)
    {
        if (_materialIndices != null &&
            rendererIndex < _materialIndices.Length &&
            _materialIndices[rendererIndex] >= 0)
        {
            return Mathf.Min(_materialIndices[rendererIndex], renderer.sharedMaterials.Length - 1);
        }
        return 0;
    }

    private void UpdateEmission(bool lit)
    {
        if (_materials == null) return;

        foreach (var material in _materials)
        {
            if (material == null || !material.HasProperty(EmissionColorId)) continue;

            if (lit)
            {
                material.EnableKeyword(EmissionKeyword);
                material.SetColor(EmissionColorId, _emissionColor * _emissionIntensity);
            }
            else
            {
                material.DisableKeyword(EmissionKeyword);
                material.SetColor(EmissionColorId, Color.black);
            }
        }
    }
}
