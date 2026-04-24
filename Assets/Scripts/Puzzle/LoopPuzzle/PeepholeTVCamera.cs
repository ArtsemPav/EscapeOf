using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Cycles through a list of cameras and renders the active one to a shared RenderTexture
/// displayed on the TV screen. Only the active camera renders — all others are disabled.
///
/// Setup:
///  1. Add all TV cameras to the <see cref="_cameras"/> list in the Inspector.
///  2. Assign <see cref="_screenRenderer"/> to the TV screen MeshRenderer.
///  3. Set <see cref="_materialIndex"/> to the correct material slot on the screen renderer.
///  4. Call <see cref="NextCamera"/> from a button's IInteractable.Interact().
/// </summary>
public class PeepholeTVCamera : MonoBehaviour
{
    [Header("Cameras")]
    [Tooltip("All TV cameras in cycle order. The first one is active at start.")]
    [SerializeField] private List<Camera> _cameras = new();

    [Header("Screen")]
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
    private int           _currentIndex;

    private static readonly int EmissionStrengthID = Shader.PropertyToID("_EmissionStrength");
    private static readonly int EmissionColorID    = Shader.PropertyToID("_EmissionColor");
    private static readonly int NoiseAmountID      = Shader.PropertyToID("_NoiseAmount");
    private static readonly int NoiseSpeedID       = Shader.PropertyToID("_NoiseSpeed");

    /// <summary>The instanced screen material. Used by TVGlitchEffect to drive shader properties.</summary>
    public Material ScreenMaterial => _screenMaterial;

    /// <summary>Index of the currently active camera in the <see cref="_cameras"/> list.</summary>
    public int CurrentCameraIndex => _currentIndex;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_cameras == null || _cameras.Count == 0)
        {
            Debug.LogError("[PeepholeTVCamera] No cameras assigned.", this);
            return;
        }

        if (_screenRenderer == null)
        {
            Debug.LogError("[PeepholeTVCamera] Screen renderer reference is not assigned.", this);
            return;
        }

        HideSymbolsFromMainCamera();
        CreateRenderTexture();
        ApplyToScreen();
        ActivateCamera(_currentIndex);
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

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Advances to the next camera in the list, wrapping around.</summary>
    public void NextCamera()
    {
        if (_cameras == null || _cameras.Count == 0) return;

        int next = (_currentIndex + 1) % _cameras.Count;
        ActivateCamera(next);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>Enables only the camera at <paramref name="index"/>, assigning it the shared RT. Disables all others.</summary>
    private void ActivateCamera(int index)
    {
        for (int i = 0; i < _cameras.Count; i++)
        {
            var cam = _cameras[i];
            if (cam == null) continue;

            bool isActive = i == index;
            cam.targetTexture = isActive ? _renderTexture : null;
            cam.enabled       = isActive;
        }

        _currentIndex = index;
    }

    /// <summary>Removes the TVOnly layer from the main camera so symbols are invisible to the player directly.</summary>
    private static void HideSymbolsFromMainCamera()
    {
        int tvOnlyLayer = LayerMask.NameToLayer("TVOnly");
        if (tvOnlyLayer == -1)
        {
            Debug.LogWarning("[PeepholeTVCamera] Layer 'TVOnly' not found. Symbols will be visible to the main camera.");
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            mainCamera.cullingMask &= ~(1 << tvOnlyLayer);
    }

    private void CreateRenderTexture()
    {
        _renderTexture              = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
        _renderTexture.name         = "PeepholeTV_RT";
        _renderTexture.antiAliasing = 1;
        _renderTexture.Create();
    }

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

    private void SyncMaterialProperties()
    {
        if (_screenMaterial == null) return;
        _screenMaterial.SetFloat(EmissionStrengthID, _emissionStrength);
        _screenMaterial.SetFloat(NoiseAmountID,      _noiseAmount);
        _screenMaterial.SetFloat(NoiseSpeedID,       _noiseSpeed);
        _screenMaterial.SetColor(EmissionColorID,    _emissionColor);
    }
}
