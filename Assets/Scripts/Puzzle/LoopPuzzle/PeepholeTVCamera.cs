using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Cycles through a list of cameras and renders the active one to a shared RenderTexture
/// displayed on the TV screen. Only the active camera renders — all others are disabled.
///
/// Setup:
///  1. Assign the TVGlitch shader asset to <see cref="_shader"/>.
///  2. Add all TV cameras to the <see cref="_cameras"/> list in the Inspector.
///  3. Assign <see cref="_screenRenderer"/> to the TV screen MeshRenderer.
///  4. Set <see cref="_materialIndex"/> to the correct material slot on the screen renderer.
///  5. Call <see cref="NextCamera"/> from a button's IInteractable.Interact().
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
    [SerializeField] private int _width  = 512;
    [SerializeField] private int _height = 288;

    [Header("Shader")]
    [Tooltip("The TVGlitch shader asset. Assign to prevent shader stripping in builds.")]
    [SerializeField] private Shader _shader;

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
        ConfigureCameras();
        CreateRenderTexture();
        ApplyToScreen();
        ActivateCamera(_currentIndex);
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

    private void OnDisable()
    {
        BlackoutScreen();
    }

    private void OnEnable()
    {
        // Re-activate the current camera after being disabled.
        // Skips on first Awake call (cameras not yet set up).
        if (_cameras == null || _cameras.Count == 0 || _renderTexture == null) return;
        ActivateCamera(_currentIndex);
        RestoreScreen();
    }

    /// <summary>
    /// Swaps the screen material to a plain black material with no emission
    /// and stops all TV cameras. The screen goes fully dark.
    /// </summary>
    private void BlackoutScreen()
    {
        // Stop all cameras.
        if (_cameras != null)
        {
            foreach (var cam in _cameras)
            {
                if (cam == null) continue;
                cam.targetTexture = null;
                cam.enabled = false;
            }
        }

        // Replace the screen material with a black one — no texture, no emission.
        if (_screenRenderer != null)
        {
            var blackMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            blackMat.name = "TVScreen_Blackout";
            blackMat.SetColor("_BaseColor", Color.black);
            blackMat.SetColor("_EmissionColor", Color.black);
            blackMat.DisableKeyword("_EMISSION");

            var mats = _screenRenderer.sharedMaterials;
            if (_materialIndex >= 0 && _materialIndex < mats.Length)
            {
                mats[_materialIndex] = blackMat;
                _screenRenderer.materials = mats;
            }
        }
    }

    /// <summary>
    /// Restores the runtime RT material on the screen and re-enables the active camera.
    /// </summary>
    private void RestoreScreen()
    {
        if (_screenRenderer != null && _screenMaterial != null)
        {
            var mats = _screenRenderer.sharedMaterials;
            if (_materialIndex >= 0 && _materialIndex < mats.Length)
            {
                mats[_materialIndex] = _screenMaterial;
                _screenRenderer.materials = mats;
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Sync shader properties in real time when Inspector values change in Play Mode.
        if (_screenMaterial != null)
            SyncMaterialProperties();
    }
#endif

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Advances to the next camera in the list, wrapping around.</summary>
    public void NextCamera()
    {
        if (_cameras == null || _cameras.Count == 0) return;

        int next = (_currentIndex + 1) % _cameras.Count;
        ActivateCamera(next);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Disables unnecessary URP intermediate buffers on all TV cameras.
    /// Color and depth textures are only needed for screen-space effects,
    /// which these cameras don't use.
    /// </summary>
    private void ConfigureCameras()
    {
        foreach (var cam in _cameras)
        {
            if (cam == null) continue;

            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;

            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
        }
    }

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
        // Use the serialized shader reference. Fall back to Shader.Find only as a last resort.
        Shader shader = _shader != null ? _shader : Shader.Find("Custom/TVGlitch");
        if (shader == null)
        {
            Debug.LogError("[PeepholeTVCamera] TVGlitch shader not found — stripped from build. " +
                           "Add 'Custom/TVGlitch' to Project Settings > Graphics > Always Included Shaders " +
                           "or assign it to the _shader field on this component.", this);

            // Fallback to a built-in unlit shader so the screen at least shows the camera feed.
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[PeepholeTVCamera] Fallback URP/Unlit shader also not found.", this);
                return;
            }
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

    /// <summary>
    /// Pushes the current Inspector values to the screen material.
    /// Called once on Awake and via OnValidate when values change in the Editor.
    /// Not called every frame.
    /// </summary>
    private void SyncMaterialProperties()
    {
        _screenMaterial.SetFloat(EmissionStrengthID, _emissionStrength);
        _screenMaterial.SetFloat(NoiseAmountID,      _noiseAmount);
        _screenMaterial.SetFloat(NoiseSpeedID,       _noiseSpeed);
        _screenMaterial.SetColor(EmissionColorID,    _emissionColor);
    }
}
