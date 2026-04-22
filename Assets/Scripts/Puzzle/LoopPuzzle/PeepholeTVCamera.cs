using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders the painting room view to a RenderTexture and displays it on the TV screen.
///
/// Setup:
///  1. Add a Camera component to TVCamera (child of Peephole) and point it into the painting room.
///     Make sure its Tag is NOT "MainCamera".
///  2. Add this script to that same GameObject (or any active object in the hierarchy).
///  3. Assign _camera to the Camera component and _screenRenderer to the screen MeshRenderer.
///  4. Set _materialIndex to the correct material slot on the screen renderer.
/// </summary>
public class PeepholeTVCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera positioned at the peephole, looking into the painting room.")]
    [SerializeField] private Camera   _camera;

    [Tooltip("MeshRenderer of the TV screen object.")]
    [SerializeField] private Renderer _screenRenderer;

    [Tooltip("Material slot index on the screen renderer to override.")]
    [SerializeField] private int _materialIndex = 0;

    [Header("Render Texture")]
    [SerializeField] private int _width  = 1024;
    [SerializeField] private int _height = 576;

    [Header("Screen Appearance")]
    [Tooltip("Brightness multiplier applied to the RT content (1 = neutral, 0 = black screen).")]
    [Range(0f, 10f)]
    [SerializeField] private float _emissionStrength = 2.5f;

    [Tooltip("Amount of static noise overlaid on the screen (0 = none, 1 = full static).")]
    [Range(0f, 1f)]
    [SerializeField] private float _noiseAmount = 0.12f;

    [Tooltip("How many times per second the noise pattern updates.")]
    [Range(1f, 60f)]
    [SerializeField] private float _noiseSpeed = 30f;

    [Tooltip("Additive HDR color emitted by the screen itself, independent of camera feed. Use HDR intensity (> 1) to trigger bloom.")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _emissionColor = Color.black;

    private RenderTexture _renderTexture;
    private Material      _screenMaterial;

    private static readonly int EmissionStrengthID = Shader.PropertyToID("_EmissionStrength");
    private static readonly int EmissionColorID    = Shader.PropertyToID("_EmissionColor");
    private static readonly int NoiseAmountID      = Shader.PropertyToID("_NoiseAmount");
    private static readonly int NoiseSpeedID       = Shader.PropertyToID("_NoiseSpeed");

    /// <summary>The instanced screen material. Used by TVGlitchEffect to drive shader properties.</summary>
    public Material ScreenMaterial => _screenMaterial;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null)
        {
            Debug.LogError("[PeepholeTVCamera] Camera reference is not assigned.", this);
            return;
        }

        if (_screenRenderer == null)
        {
            Debug.LogError("[PeepholeTVCamera] Screen renderer reference is not assigned.", this);
            return;
        }

        CreateRenderTexture();
        ApplyToScreen();
    }

    private void Update()
    {
        SyncMaterialProperties();
    }

    private void OnDestroy()
    {
        if (_screenMaterial != null)
            Destroy(_screenMaterial);

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    // ── Setup ──────────────────────────────────────────────────────────────────

    private void CreateRenderTexture()
    {
        _renderTexture              = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
        _renderTexture.name         = "PeepholeTV_RT";
        _renderTexture.antiAliasing = 1;
        _renderTexture.Create();

        _camera.targetTexture = _renderTexture;
    }

    /// <summary>
    /// Creates a Custom/TVGlitch material instance with the RenderTexture and assigns it
    /// to the specified material slot on the screen renderer.
    /// </summary>
    private void ApplyToScreen()
    {
        var shader = Shader.Find("Custom/TVGlitch");
        if (shader == null)
        {
            Debug.LogError("[PeepholeTVCamera] Shader 'Custom/TVGlitch' not found.", this);
            return;
        }

        _screenMaterial      = new Material(shader);
        _screenMaterial.name = "TVScreen_RT";
        _screenMaterial.SetTexture("_BaseMap", _renderTexture);
        SyncMaterialProperties();

        var mats = _screenRenderer.sharedMaterials;

        if (_materialIndex < 0 || _materialIndex >= mats.Length)
        {
            Debug.LogError($"[PeepholeTVCamera] Material index {_materialIndex} is out of range " +
                           $"(renderer has {mats.Length} material slots).", this);
            return;
        }

        mats[_materialIndex]      = _screenMaterial;
        _screenRenderer.materials = mats;
    }

    /// <summary>Pushes the serialized appearance fields to the runtime material. Called every frame so Inspector changes apply immediately in Play Mode.</summary>
    private void SyncMaterialProperties()
    {
        if (_screenMaterial == null) return;
        _screenMaterial.SetFloat(EmissionStrengthID, _emissionStrength);
        _screenMaterial.SetFloat(NoiseAmountID,      _noiseAmount);
        _screenMaterial.SetFloat(NoiseSpeedID,       _noiseSpeed);
        _screenMaterial.SetColor(EmissionColorID,    _emissionColor);
    }
}
