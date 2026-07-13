using UnityEngine;

/// <summary>
/// Makes a SpriteRenderer visible only inside the flashlight's spotlight cone.
/// Uses a custom URP shader (Custom/HiddenWallSign) that:
///   - masks pixels outside the cone,
///   - fades brightness from beam center toward edges,
///   - fades alpha with distance (barely visible beyond MaxVisibleDistance),
///   - outputs HDR emission so URP Bloom creates a glow around the inscription.
/// The sprite is invisible by default and appears only when the flashlight
/// is on and set to the matching mode.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class HiddenWallSign : MonoBehaviour
{
    private const string ShaderName = "Custom/HiddenWallSign";

    private static readonly int FlashlightPosId     = Shader.PropertyToID("_FlashlightPos");
    private static readonly int FlashlightDirId     = Shader.PropertyToID("_FlashlightDir");
    private static readonly int SpotAngleCosId      = Shader.PropertyToID("_SpotAngleCos");
    private static readonly int EdgeSoftnessId      = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int RadialFalloffId     = Shader.PropertyToID("_RadialFalloff");
    private static readonly int MaxVisibleDistId    = Shader.PropertyToID("_MaxVisibleDist");
    private static readonly int EmissionColorId     = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

    [Tooltip("The flashlight mode that makes this sign visible.")]
    [SerializeField] private FlashlightMode visibleInMode = FlashlightMode.UV;

    [Tooltip("Reference to the scene's FlashlightController. " +
             "If not assigned, automatically found via FlashlightController.Instance at runtime.")]
    [SerializeField] private FlashlightController flashlight;

    [Header("Beam Shape")]
    [Tooltip("How soft the cone boundary is (cosine space). 0 = hard cut, 0.12 = very soft.")]
    [SerializeField] [Range(0f, 0.12f)] private float edgeSoftness = 0.05f;

    [Tooltip("How quickly brightness drops from the beam center toward its edge. " +
             "1 = linear, 2 = quadratic, higher = tighter bright center.")]
    [SerializeField] [Range(0.5f, 4f)] private float radialFalloff = 1.5f;

    [Header("Distance Fade")]
    [Tooltip("Distance in metres at which the sign becomes barely visible.")]
    [SerializeField] [Min(0.1f)] private float maxVisibleDistance = 2f;

    [Header("Emission / Glow")]
    [Tooltip("HDR color of the inscription glow. Requires Bloom in the scene's Volume profile.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color emissionColor = new Color(0f, 0.5f, 2f);

    [Tooltip("Emission multiplier. Values above 1 feed URP Bloom. " +
             "Raise Bloom Threshold in the Volume profile if glow is too wide.")]
    [SerializeField] [Range(0f, 10f)] private float emissionIntensity = 2f;

    private SpriteRenderer _renderer;
    private Light _flashlightLight;
    private MaterialPropertyBlock _propertyBlock;
    private bool _isVisible;
    private bool _subscribed;

    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _renderer.enabled = false;

        Shader shader = Shader.Find(ShaderName);
        if (shader != null)
            _renderer.material = new Material(shader);
        else
            Debug.LogError($"[HiddenWallSign] Shader '{ShaderName}' not found.", this);

        _propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (!_subscribed || flashlight == null) return;
        flashlight.OnModeChanged -= HandleModeChanged;
        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        if (flashlight == null && FlashlightController.Instance != null)
            flashlight = FlashlightController.Instance;

        if (flashlight == null) return;

        _flashlightLight = flashlight.GetComponent<Light>();
        flashlight.OnModeChanged += HandleModeChanged;
        _subscribed = true;

        UpdateVisibility();
    }

    private void HandleModeChanged(FlashlightMode newMode)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (flashlight == null) return;

        _isVisible = flashlight.IsOn && flashlight.CurrentMode == visibleInMode;
        _renderer.enabled = _isVisible;
    }

    private void LateUpdate()
    {
        if (!_isVisible || _flashlightLight == null || flashlight == null)
            return;

        Transform lt = flashlight.transform;
        float halfAngleRad = _flashlightLight.spotAngle * 0.5f * Mathf.Deg2Rad;

        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetVector(FlashlightPosId, lt.position);
        _propertyBlock.SetVector(FlashlightDirId, lt.forward);
        _propertyBlock.SetFloat(SpotAngleCosId, Mathf.Cos(halfAngleRad));
        _propertyBlock.SetFloat(EdgeSoftnessId, edgeSoftness);
        _propertyBlock.SetFloat(RadialFalloffId, radialFalloff);
        _propertyBlock.SetFloat(MaxVisibleDistId, maxVisibleDistance);
        _propertyBlock.SetColor(EmissionColorId, emissionColor);
        _propertyBlock.SetFloat(EmissionIntensityId, emissionIntensity);
        _renderer.SetPropertyBlock(_propertyBlock);
    }
}
