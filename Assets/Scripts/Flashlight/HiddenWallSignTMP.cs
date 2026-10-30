using UnityEngine;
using TMPro;

/// <summary>
/// Makes a 3D TextMeshPro object visible only inside the flashlight's spotlight cone.
/// Uses a copy of the TMP SDF shader (Custom/HiddenWallSignTMP) that adds the same
/// cone / radial / distance masking and HDR emission glow as Custom/HiddenWallSign.
/// The text is invisible by default and appears only when the flashlight
/// is on and set to the matching mode.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class HiddenWallSignTMP : MonoBehaviour
{
    private const string ShaderName = "Custom/HiddenWallSignTMP";

    private static readonly int FlashlightPosId     = Shader.PropertyToID("_FlashlightPos");
    private static readonly int FlashlightDirId     = Shader.PropertyToID("_FlashlightDir");
    private static readonly int SpotAngleCosId      = Shader.PropertyToID("_SpotAngleCos");
    private static readonly int EdgeSoftnessId      = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int RadialFalloffId     = Shader.PropertyToID("_RadialFalloff");
    private static readonly int MaxVisibleDistId    = Shader.PropertyToID("_MaxVisibleDist");
    private static readonly int EmissionColorId     = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int FloodAmountId       = Shader.PropertyToID("_FloodAmount");

    [Tooltip("The flashlight mode that makes this text visible.")]
    [SerializeField] private FlashlightMode visibleInMode = FlashlightMode.UV;

    [Header("Beam Shape")]
    [Tooltip("How soft the cone boundary is (cosine space). 0 = hard cut, 0.12 = very soft.")]
    [SerializeField] [Range(0f, 0.12f)] private float edgeSoftness = 0.05f;

    [Tooltip("How quickly brightness drops from the beam center toward its edge. " +
             "1 = linear, 2 = quadratic, higher = tighter bright center.")]
    [SerializeField] [Range(0.5f, 4f)] private float radialFalloff = 1.5f;

    [Header("Distance Fade")]
    [Tooltip("Distance in metres at which the text becomes barely visible.")]
    [SerializeField] [Min(0.1f)] private float maxVisibleDistance = 2f;

    [Header("Emission / Glow")]
    [Tooltip("HDR color of the text glow. Requires Bloom in the scene's Volume profile.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color emissionColor = new Color(0f, 0.5f, 2f);

    [Tooltip("Emission multiplier. Values above 1 feed URP Bloom. " +
             "Raise Bloom Threshold in the Volume profile if glow is too wide.")]
    [SerializeField] [Range(0f, 10f)] private float emissionIntensity = 2f;

    private TextMeshPro _tmp;
    private MeshRenderer _meshRenderer;
    private Light _flashlightLight;
    private MaterialPropertyBlock _propertyBlock;
    private bool _isVisible;
    private bool _subscribed;
    private bool _shaderFound;
    private bool _materialAssigned;
    private float _forcedAmount;

    /// <summary>
    /// Makes the text visible without the flashlight (0 = off, 1 = fully visible).
    /// While active the cone / radial / distance masks are ignored, so the whole text
    /// glows as if lit by a room-wide UV source (e.g. UV lamp flashes in a cutscene).
    /// The normal flashlight behaviour keeps working when the amount is 0.
    /// </summary>
    public void SetForcedVisible(float amount)
    {
        _forcedAmount = Mathf.Clamp01(amount);
        UpdateVisibility();
    }

    private void Awake()
    {
        _tmp = GetComponent<TextMeshPro>();
        _meshRenderer = _tmp.GetComponent<MeshRenderer>();
        _tmp.enabled = false;

        _shaderFound = Shader.Find(ShaderName) != null;
        if (!_shaderFound)
            Debug.LogError($"[HiddenWallSignTMP] Shader '{ShaderName}' not found. " +
                           "Add it to Project Settings > Graphics > Always Included Shaders.", this);

        _propertyBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        AssignCustomMaterial();
        TrySubscribe();
    }

    /// <summary>
    /// Swaps the TMP material to the cone-masked shader. Must run after TextMeshPro's own
    /// Awake (LoadFontAsset), which overwrites m_sharedMaterial with the font asset's default
    /// material whenever _MainTex doesn't match the font atlas — so Awake is too early.
    /// TMP can also swap the material later (e.g. font asset changes), hence the deferred retry.
    /// </summary>
    private void AssignCustomMaterial()
    {
        if (_materialAssigned || !_shaderFound || _tmp == null)
            return;

        Material source = _tmp.fontSharedMaterial;
        if (source == null || source.shader == null || source.shader.name != ShaderName)
        {
            // Start from a copy of the current TMP material so the font atlas,
            // _GradientScale and other SDF properties carry over, then swap the shader.
            // Creating a bare Material(shader) would leave _MainTex white and every
            // glyph quad would render as a solid block.
            Material instance = source != null ? new Material(source) : new Material(Shader.Find(ShaderName));
            instance.shader = Shader.Find(ShaderName);
            _tmp.fontSharedMaterial = instance;
        }

        _materialAssigned = _tmp.fontSharedMaterial != null &&
                            _tmp.fontSharedMaterial.shader.name == ShaderName;
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        var controller = FlashlightController.Instance;
        if (!_subscribed || controller == null) return;
        controller.OnModeChanged -= HandleModeChanged;
        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        var controller = FlashlightController.Instance;
        if (controller == null) return;

        _flashlightLight = controller.GetComponent<Light>();
        controller.OnModeChanged += HandleModeChanged;
        _subscribed = true;

        UpdateVisibility();
    }

    private void HandleModeChanged(FlashlightMode _)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_tmp == null) return; // SetForcedVisible called before Awake

        var controller = FlashlightController.Instance;
        bool byFlashlight = controller != null && controller.IsOn && controller.CurrentMode == visibleInMode;

        _isVisible = _shaderFound && (byFlashlight || _forcedAmount > 0f);
        _tmp.enabled = _isVisible;
    }

    private void LateUpdate()
    {
        // TMP can reset the material after ours was assigned (font asset reload, inspector
        // changes); retry quietly until it sticks.
        if (_isVisible && !_materialAssigned)
            AssignCustomMaterial();

        if (!_isVisible)
            return;

        var controller = FlashlightController.Instance;
        bool haveFlashlight = controller != null && _flashlightLight != null;
        if (!haveFlashlight && _forcedAmount <= 0f)
            return;

        _meshRenderer.GetPropertyBlock(_propertyBlock);

        if (haveFlashlight)
        {
            Transform lt = controller.transform;
            float halfAngleRad = _flashlightLight.spotAngle * 0.5f * Mathf.Deg2Rad;
            _propertyBlock.SetVector(FlashlightPosId, lt.position);
            _propertyBlock.SetVector(FlashlightDirId, lt.forward);
            _propertyBlock.SetFloat(SpotAngleCosId, Mathf.Cos(halfAngleRad));
        }

        _propertyBlock.SetFloat(FloodAmountId, _forcedAmount);
        _propertyBlock.SetFloat(EdgeSoftnessId, edgeSoftness);
        _propertyBlock.SetFloat(RadialFalloffId, radialFalloff);
        _propertyBlock.SetFloat(MaxVisibleDistId, maxVisibleDistance);
        _propertyBlock.SetColor(EmissionColorId, emissionColor);
        _propertyBlock.SetFloat(EmissionIntensityId, emissionIntensity);
        _meshRenderer.SetPropertyBlock(_propertyBlock);
    }
}
