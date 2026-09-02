using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the liquid surface in sinks, baths, and other drainable containers.
///
/// Uses the "Custom/LiquidBath" shader.  Manages a material instance,
/// caches mesh bounds, pushes visual properties from the Inspector, and
/// provides <see cref="AnimateFillTo"/> for drain animations.
///
/// The transform is uniformly scaled by fillFraction to animate the water
/// level.  Set up the mesh at full size (fillFraction = 1) in the Inspector,
/// then right-click → "Capture Full Transform" to cache the base state.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class BathLiquidController : MonoBehaviour
{
    private const string ShaderName = "Custom/LiquidBath";

    [Header("Fill")]
    [Tooltip("Доля заполнения: 0 = пусто, 1 = полная. Используется в Edit Mode.")]
    [Range(0f, 1f)]
    public float fillFraction = 0.8f;

    /// <summary>Runtime fill level used in Play Mode. Not serialized — does not persist after exit.</summary>
    private float _runtimeFill = -1f;

    /// <summary>Current effective fill: runtime in Play Mode, design-time in Edit Mode.</summary>
    public float CurrentFill => Application.isPlaying ? _runtimeFill : fillFraction;

    /// <summary>Sets the runtime fill level (Play Mode only). Does not affect the serialized fillFraction.</summary>
    public void SetRuntimeFill(float value) => _runtimeFill = Mathf.Clamp01(value);

    [Tooltip("If true, the script pushes all visual properties to the material every frame. " +
             "If false, only runtime properties (fill, pivot, mesh bounds) are pushed.")]
    [SerializeField] private bool _overrideVisualProps = true;

    [Header("Color")]
    [SerializeField] private Color _liquidColor   = new Color(0.24f, 0.25f, 0.23f, 1f);
    [SerializeField] private Color _surfaceColor  = new Color(0.30f, 0.32f, 0.28f, 1f);
    [SerializeField] private Color _emissionColor = Color.black;
    [SerializeField] private float _emissionPower = 0f;

    [Header("Turbidity & Noise")]
    [Range(0f, 1f)]   [SerializeField] private float _turbidity   = 1f;
    [Range(0.1f, 10f)] [SerializeField] private float _noiseScale  = 8.3f;
    [Range(0f, 5f)]    [SerializeField] private float _noiseSpeed  = 2.3f;

    [Header("Transparency & Refraction")]
    [Range(0f, 1f)]    [SerializeField] private float _opacity            = 0.768f;
    [Range(0f, 0.2f)]  [SerializeField] private float _refractionStrength = 0.164f;
    [Range(0f, 0.02f)] [SerializeField] private float _chromaticAberration = 0.0121f;

    [Header("Distortion & Lens")]
    [Range(0f, 0.3f)] [SerializeField] private float _distortionStrength = 0.177f;
    [Range(0f, 5f)]   [SerializeField] private float _distortionSpeed    = 3.81f;
    [Range(0f, 1f)]   [SerializeField] private float _lensStrength       = 0.575f;
    [Range(0f, 3f)]   [SerializeField] private float _lensPower          = 1.75f;

    [Header("Depth & Blur")]
    [Range(0f, 1f)]    [SerializeField] private float _depthDarken  = 0.441f;
    [Range(0f, 0.05f)] [SerializeField] private float _blurStrength = 0.0092f;

    [Header("Cap (surface above water)")]
    [Range(0f, 1f)] [SerializeField] private float _capOpacity    = 0.479f;
    [Range(1f, 5f)] [SerializeField] private float _capDistortion = 1.7f;

    [Header("Lighting")]
    [Range(0f, 1f)] [SerializeField] private float _minLightFloor = 0.463f;

    [Header("Shader")]
    [Tooltip("Assign the LiquidBath shader asset to prevent shader stripping in builds.")]
    [SerializeField] private Shader _shader;

    [Header("Transform Cache (auto)")]
    [Tooltip("Cached full-scale (fillFraction = 1) localScale. Captured via context menu or automatically on first run.")]
    [SerializeField] private Vector3 _fullScale = Vector3.one;

    [Tooltip("Cached full-scale localPosition.")]
    [SerializeField] private Vector3 _fullLocalPos = Vector3.zero;

    [Tooltip("Mesh bounds center in local space — used for position compensation.")]
    [SerializeField] private Vector3 _meshCenterLocal = Vector3.zero;

    [Tooltip("Whether the full transform has been captured. Right-click → 'Capture Full Transform' to re-capture.")]
    [SerializeField] private bool _transformCaptured;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private Renderer   _renderer;
    private MeshFilter _meshFilter;
    private Material   _materialInstance;
    private Material   _sharedMaterialAsset;
    private bool       _ownsMaterialInstance;
    private float      _localMeshMin;
    private float      _localMeshHeight;
    private Coroutine  _fillCoroutine;

    // Track last applied fillFraction to distinguish user edits from script edits.
    private float      _lastAppliedFill = -1f;

    // Shader property IDs
    private static readonly int FillAmountId          = Shader.PropertyToID("_FillAmount");
    private static readonly int LocalMeshMinId        = Shader.PropertyToID("_LocalMeshMin");
    private static readonly int LocalMeshMaxId        = Shader.PropertyToID("_LocalMeshMax");
    private static readonly int PivotWSId             = Shader.PropertyToID("_PivotWS");
    private static readonly int LiquidColorId         = Shader.PropertyToID("_LiquidColor");
    private static readonly int SurfaceColorId        = Shader.PropertyToID("_SurfaceColor");
    private static readonly int EmissionColorId       = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionPowerId       = Shader.PropertyToID("_EmissionPower");
    private static readonly int TurbidityId           = Shader.PropertyToID("_Turbidity");
    private static readonly int NoiseScaleId          = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedId          = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int OpacityId             = Shader.PropertyToID("_Opacity");
    private static readonly int RefractionStrengthId  = Shader.PropertyToID("_RefractionStrength");
    private static readonly int ChromaticAberrationId = Shader.PropertyToID("_ChromaticAberration");
    private static readonly int DistortionStrengthId  = Shader.PropertyToID("_DistortionStrength");
    private static readonly int DistortionSpeedId     = Shader.PropertyToID("_DistortionSpeed");
    private static readonly int LensStrengthId        = Shader.PropertyToID("_LensStrength");
    private static readonly int LensPowerId           = Shader.PropertyToID("_LensPower");
    private static readonly int DepthDarkenId         = Shader.PropertyToID("_DepthDarken");
    private static readonly int MinLightFloorId       = Shader.PropertyToID("_MinLightFloor");
    private static readonly int BlurStrengthId        = Shader.PropertyToID("_BlurStrength");
    private static readonly int CapOpacityId          = Shader.PropertyToID("_CapOpacity");
    private static readonly int CapDistortionId       = Shader.PropertyToID("_CapDistortion");

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        _renderer   = GetComponent<Renderer>();
        _meshFilter = GetComponent<MeshFilter>();

        CacheMeshBounds();

        // Auto-capture on first run if not yet captured.
        if (!_transformCaptured)
            CaptureFullTransform();

        // Clean up previous instance if re-enabling.
        if (_ownsMaterialInstance && _materialInstance != null)
        {
            if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
                _renderer.sharedMaterial = _sharedMaterialAsset;
            DestroyImmediate(_materialInstance);
            _materialInstance = null;
            _ownsMaterialInstance = false;
        }

        EnsureMaterialInstance();
        ApplyMaterialProperties();
    }

    private void Start()
    {
        CacheMeshBounds();

        if (!_transformCaptured)
            CaptureFullTransform();

        if (_materialInstance == null)
            EnsureMaterialInstance();

        ApplyMaterialProperties();
    }

    private void Update()
    {
        if (_renderer == null || _materialInstance == null) return;
        ApplyMaterialProperties();

    #if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
    #endif
    }

    private void OnDisable()
    {
        if (_materialInstance == null) return;
        if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
            _renderer.sharedMaterial = _sharedMaterialAsset;
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying && _renderer != null && _sharedMaterialAsset != null)
            _renderer.sharedMaterial = _sharedMaterialAsset;

        if (_ownsMaterialInstance && _materialInstance != null)
        {
            DestroyImmediate(_materialInstance);
            _materialInstance = null;
            _ownsMaterialInstance = false;
        }
    }

    // ── Transform cache ──────────────────────────────────────────────────────

    /// <summary>
    /// Caches mesh bounds from the MeshFilter.
    /// Called every frame to handle late mesh loading.
    /// </summary>
    private void CacheMeshBounds()
    {
        if (_meshFilter != null && _meshFilter.sharedMesh != null)
        {
            Bounds b         = _meshFilter.sharedMesh.bounds;
            _localMeshMin    = b.min.y;
            _localMeshHeight = b.size.y;
            _meshCenterLocal = b.center;
        }
        else
        {
            _localMeshMin    = -0.5f;
            _localMeshHeight = 1.0f;
            _meshCenterLocal = Vector3.zero;
        }
    }

    /// <summary>
    /// Captures the current transform as the "full" (fillFraction = 1) state.
    /// To use: set fillFraction = 1 in the Inspector, adjust the mesh to fit
    /// the sink, then right-click → "Capture Full Transform".
    /// On first run (auto-capture), the current transform is stored as-is.
    /// </summary>
    private void CaptureFullTransform()
    {
        _fullScale    = transform.localScale;
        _fullLocalPos = transform.localPosition;
        _transformCaptured = true;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>
    /// Editor-only: re-captures the current transform as the full state.
    /// Set fillFraction = 1 and adjust the mesh to fit the sink, then
    /// right-click the component → "Capture Full Transform".
    /// </summary>
    [ContextMenu("Capture Full Transform")]
    private void ContextMenuCapture()
    {
        _transformCaptured = false;
        CacheMeshBounds();
        CaptureFullTransform();
        ApplyMaterialProperties();
        Debug.Log($"[BathLiquidController] Captured full transform: scale={_fullScale} pos={_fullLocalPos} center={_meshCenterLocal}", this);
    }

    /// <summary>
    /// Editor-only: resets the cache so it re-captures on next OnEnable.
    /// </summary>
    [ContextMenu("Reset Transform Cache")]
    private void ContextMenuReset()
    {
        _transformCaptured = false;
        _fullScale = Vector3.one;
        _fullLocalPos = Vector3.zero;
    }

    // ── Material management ──────────────────────────────────────────────────

    private void EnsureMaterialInstance()
    {
        if (_renderer == null) return;

        Material shared = _renderer.sharedMaterial;

        if (shared == null)
        {
            Shader shader = _shader != null ? _shader : Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BathLiquidController] Shader '{ShaderName}' not found.", this);
                return;
            }

            _materialInstance = new Material(shader);
            _materialInstance.hideFlags = HideFlags.HideAndDontSave;
            _renderer.sharedMaterial = _materialInstance;
            _ownsMaterialInstance = true;
            return;
        }

        if (Application.isPlaying)
        {
            _materialInstance = _renderer.material;
            _ownsMaterialInstance = true;
        }
        else
        {
            _sharedMaterialAsset = shared;
            _materialInstance = new Material(shared);
            _materialInstance.hideFlags = HideFlags.HideAndDontSave;
            _renderer.sharedMaterial = _materialInstance;
            _ownsMaterialInstance = true;
        }
    }

    /// <summary>
    /// Pushes properties to the material instance.
    /// Scales the transform uniformly by the effective fill level.  The mesh
    /// pivot is at the bottom (local y ≈ 0), so scaling from the pivot
    /// naturally keeps the bottom anchored while the top surface descends.
    ///
    /// In Edit Mode, detects manual position/scale edits from the Inspector
    /// or Scene view and adopts them as the new full-scale baseline — so the
    /// user can freely move and resize the object without the script
    /// fighting back.
    /// </summary>
    private void ApplyMaterialProperties()
    {
        if (_materialInstance == null) return;

        CacheMeshBounds();

        // If not yet captured (e.g. fresh component), capture now.
        if (!_transformCaptured)
            CaptureFullTransform();

        // In Play Mode, initialise runtime fill from the design-time value once.
        if (Application.isPlaying && _runtimeFill < 0f)
            _runtimeFill = fillFraction;

        float effectiveFill = CurrentFill;
        float s = Mathf.Max(effectiveFill, 0.001f);

        // In Edit Mode: detect manual transform edits and adopt them.
        if (!Application.isPlaying)
        {
            // If fillFraction hasn't changed since last frame but the
            // transform has, the user is editing it manually — adopt.
            if (!Mathf.Approximately(fillFraction, _lastAppliedFill))
            {
                // fillFraction changed from Inspector — we'll apply it below.
            }
            else
            {
                // fillFraction unchanged — any transform diff is a user edit.
                bool changed = false;
                if (transform.localPosition != _fullLocalPos)
                {
                    _fullLocalPos = transform.localPosition;
                    changed = true;
                }

                Vector3 expectedScale = _fullScale * s;
                if (transform.localScale != expectedScale)
                {
                    _fullScale = transform.localScale / s;
                    changed = true;
                }

                if (changed)
                {
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(this);
#endif
                }
            }
        }

        // Apply uniform scale by effective fill.
        Vector3 newScale = _fullScale * s;
        if (transform.localScale != newScale)
            transform.localScale = newScale;

        // Apply position.
        if (transform.localPosition != _fullLocalPos)
            transform.localPosition = _fullLocalPos;

        _lastAppliedFill = fillFraction;

        // Runtime properties — always pushed.
        _materialInstance.SetFloat(FillAmountId,    effectiveFill);
        _materialInstance.SetFloat(LocalMeshMinId,  _localMeshMin);
        _materialInstance.SetFloat(LocalMeshMaxId,  _localMeshMin + _localMeshHeight);
        _materialInstance.SetVector(PivotWSId,      transform.position);

        // Visual properties — only when overriding is enabled.
        if (!_overrideVisualProps) return;

        _materialInstance.SetColor(LiquidColorId,          _liquidColor);
        _materialInstance.SetColor(SurfaceColorId,         _surfaceColor);
        _materialInstance.SetColor(EmissionColorId,        _emissionColor);
        _materialInstance.SetFloat(EmissionPowerId,        _emissionPower);
        _materialInstance.SetFloat(TurbidityId,            _turbidity);
        _materialInstance.SetFloat(NoiseScaleId,           _noiseScale);
        _materialInstance.SetFloat(NoiseSpeedId,           _noiseSpeed);
        _materialInstance.SetFloat(OpacityId,              _opacity);
        _materialInstance.SetFloat(RefractionStrengthId,   _refractionStrength);
        _materialInstance.SetFloat(ChromaticAberrationId,  _chromaticAberration);
        _materialInstance.SetFloat(DistortionStrengthId,   _distortionStrength);
        _materialInstance.SetFloat(DistortionSpeedId,      _distortionSpeed);
        _materialInstance.SetFloat(LensStrengthId,         _lensStrength);
        _materialInstance.SetFloat(LensPowerId,            _lensPower);
        _materialInstance.SetFloat(DepthDarkenId,          _depthDarken);
        _materialInstance.SetFloat(MinLightFloorId,        _minLightFloor);
        _materialInstance.SetFloat(BlurStrengthId,         _blurStrength);
        _materialInstance.SetFloat(CapOpacityId,           _capOpacity);
        _materialInstance.SetFloat(CapDistortionId,        _capDistortion);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Smoothly animates the runtime fill to <paramref name="target"/> over <paramref name="duration"/> seconds.</summary>
    public void AnimateFillTo(float target, float duration)
    {
        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(FillRoutine(target, duration));
    }

    private IEnumerator FillRoutine(float target, float duration)
    {
        float start   = _runtimeFill >= 0f ? _runtimeFill : fillFraction;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed      += Time.deltaTime;
            _runtimeFill  = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        _runtimeFill  = target;
        _fillCoroutine = null;
    }
}
