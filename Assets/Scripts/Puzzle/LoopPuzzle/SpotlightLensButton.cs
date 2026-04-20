using System;
using UnityEngine;

/// <summary>
/// Interactable button that cycles the spectral lens on a target PaintingSpotlight.
/// Each press advances to the next color in _lensOptions (Red → Blue → Yellow → Red…).
/// The indicator MeshRenderer's emission color reflects the active lens.
/// Uses MaterialPropertyBlock to avoid per-instance material creation.
/// Saves/loads its current lens index via ISaveable.
/// </summary>
public class SpotlightLensButton : MonoBehaviour, IInteractable, ISaveable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Save")]
    [SerializeField] private string _saveId = "lens_button_unique_id";

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Сменить линзу";

    [Header("Target Spotlight")]
    [Tooltip("The spotlight (L1, L2 or L4) whose lens this button controls.")]
    [SerializeField] private PaintingSpotlight _targetSpotlight;

    [Header("Lens Cycle")]
    [Tooltip("Ordered list of lens colors this button cycles through.")]
    [SerializeField] private LensColor[] _lensOptions = { LensColor.Red, LensColor.Blue, LensColor.Yellow };

    [Header("Indicator")]
    [SerializeField] private Renderer _indicatorRenderer;
    [Tooltip("HDR emission color shown when the current lens is Red. Values > 1 activate Bloom.")]
    [SerializeField] private Color _redEmission    = new Color(3f, 0.3f, 0.2f);
    [Tooltip("HDR emission color for Blue lens.")]
    [SerializeField] private Color _blueEmission   = new Color(0.2f, 0.8f, 4f);
    [Tooltip("HDR emission color for Yellow lens.")]
    [SerializeField] private Color _yellowEmission = new Color(3f, 2.8f, 0.1f);

    private int _currentIndex;
    private MaterialPropertyBlock _propertyBlock;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { lensIndex = _currentIndex });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _currentIndex = Mathf.Clamp(data.lensIndex, 0, Mathf.Max(0, _lensOptions.Length - 1));
        ApplyCurrentLens();
    }

    [Serializable]
    private struct SaveData { public int lensIndex; }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();

        if (_indicatorRenderer != null)
        {
            Material mat = _indicatorRenderer.material;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorId, Color.black);
        }

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Apply initial lens on Start so all spotlights are initialized first.
        ApplyCurrentLens();
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Cycles to the next lens in the configured options.</summary>
    public void Interact()
    {
        if (_lensOptions == null || _lensOptions.Length == 0) return;
        _currentIndex = (_currentIndex + 1) % _lensOptions.Length;
        ApplyCurrentLens();
    }

    public bool CanInteract()       => true;
    public bool IsPickable()        => false;
    public bool UseLMBClick         => true;
    public string GetInteractText() => _interactText;

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ApplyCurrentLens()
    {
        if (_lensOptions == null || _lensOptions.Length == 0) return;

        var lens = _lensOptions[_currentIndex];
        _targetSpotlight?.SetLens(lens);
        UpdateIndicator(lens);
    }

    private void UpdateIndicator(LensColor lens)
    {
        if (_indicatorRenderer == null) return;

        var emissionColor = lens switch
        {
            LensColor.Red    => _redEmission,
            LensColor.Blue   => _blueEmission,
            LensColor.Yellow => _yellowEmission,
            _                => Color.black
        };

        _indicatorRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColorId, emissionColor);
        _indicatorRenderer.SetPropertyBlock(_propertyBlock);
    }
}
