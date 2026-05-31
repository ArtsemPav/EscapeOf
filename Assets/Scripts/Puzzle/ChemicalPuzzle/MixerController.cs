using System;
using System.Collections;
using System.Collections.Generic;
using ChemicalPuzzle;
using UnityEngine;

/// <summary>
/// A data-driven mixing recipe: all <see cref="ingredients"/> must be present
/// (order-independent) for <see cref="result"/> to be produced.
/// </summary>
[Serializable]
public struct MixingRecipe
{
    [Tooltip("All items that must be present in the mixer at the same time (order-independent).")]
    public ItemData[] ingredients;

    [Tooltip("Item produced when all ingredients match.")]
    public ItemData result;
}

/// <summary>
/// Controls the reactor-mixer device.
/// Accumulates flasks by ItemData reference until the required portion count is reached,
/// then determines the result using a data-driven recipe table.
///
/// Slag logic: any item listed in _slagItems, when present in the mix, poisons the
/// entire batch — the result is always _spoiledResult regardless of other ingredients.
/// This lets players experiment with wrong combinations and get a punishing output.
/// </summary>
public class MixerController : ChemicalDeviceBase
{
    [Header("Settings")]
    [Tooltip("Number of flask drops required to trigger export.")]
    [SerializeField] private int _portionsToExport = 2;

    [SerializeField] private float _exportDelay = 1.5f;

    [Header("Drop Zone")]
    [Tooltip("Collider on the mixer mesh that receives item drops. Auto-used by ChemicalSynthesisController.")]
    [SerializeField] private Collider _dropZoneCollider;

    [Header("Liquid")]
    [Tooltip("LiquidWobble component on the liquid mesh child.")]
    [SerializeField] private LiquidWobble _liquidWobble;

    [Tooltip("Duration of fill / drain animations in seconds.")]
    [SerializeField] private float _fillAnimDuration = 0.6f;

    [Tooltip("How much liquid (0–1) each poured flask adds. Default 0.24 = 24 % per flask.")]
    [SerializeField] [Range(0f, 1f)] private float _fillPerPortion = 0.24f;

    [Header("Glow")]
    [Tooltip("Renderer that shows the glow effect while the mixer is locked.")]
    [SerializeField] private Renderer _glowRenderer;

    [Header("Audio")]
    [Tooltip("Played once each time a flask is poured into the mixer.")]
    [SerializeField] private AudioClip _pourClip;

    [Tooltip("Looping 3D sound played during the export delay after the last flask.")]
    [SerializeField] private AudioClip _mixLoopClip;

    [SerializeField] [Range(0f, 1f)] private float _mixLoopVolume = 0.7f;
    [SerializeField] private float _mixLoopMinDistance = 0.5f;
    [SerializeField] private float _mixLoopMaxDistance = 5f;

    [Tooltip("Played once when the mix is exported.")]
    [SerializeField] private AudioClip _mixCompleteClip;

    [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

    [Header("Hover Highlight")]
    [Tooltip("Renderer on the flask mesh that lights up when a valid item is dragged over the mixer. " +
             "Auto-resolved to the first child MeshRenderer named 'mixColba' if left empty.")]
    [SerializeField] private Renderer _hoverHighlightRenderer;

    [Tooltip("Emission colour added on top of the material while a valid item hovers. " +
             "Keep values below 1 for a subtle glow; higher values increase bloom intensity.")]
    [SerializeField] private Color _highlightColor = new Color(0f, 0.5f, 0.3f);

    // _acceptedItems and _equivalenceMap removed — managed globally by ChemicalSynthesisController.

    [Header("Slag Items")]
    [Tooltip("Items that contaminate the entire mix. If any loaded item is in this list the result is always _spoiledResult, regardless of other ingredients.")]
    [SerializeField] private ItemData[] _slagItems;

    [Header("Recipes")]
    [Tooltip("Ordered list of valid mixing recipes. First match wins.")]
    [SerializeField] private MixingRecipe[] _recipes;

    [Header("Results")]
    [Tooltip("Returned when a mix is contaminated by a slag item or matches no recipe.")]
    [SerializeField] private ItemData _spoiledResult;

    // ── Shared context (injected by ChemicalSynthesisController) ──────────────

    private IChemicalPuzzleContext _context;

    /// <summary>Injects the shared puzzle context. Called by ChemicalSynthesisController in Awake.</summary>
    public void Initialize(IChemicalPuzzleContext context) => _context = context;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private readonly List<ItemData> _addedItems = new List<ItemData>();
    private bool _isLocked;
    private bool _containsSlag;
    private AudioSource _mixLoopSource;

    // ── Highlight state ────────────────────────────────────────────────────────

    private bool _isHighlighted;
    private Material _originalSharedMaterial;
    private Material _highlightInstance;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>True when the mixer has reached its portion limit and is exporting.</summary>
    public bool IsFull => _isLocked;

    /// <summary>The collider used as the drop-zone by the orchestrator.</summary>
    public Collider DropZoneCollider => _dropZoneCollider;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Auto-resolve the drop-zone collider if not wired in the Inspector.
        if (_dropZoneCollider == null)
            _dropZoneCollider = GetComponent<Collider>();

        // Auto-resolve the hover highlight renderer (inner flask mesh named "mixColba").
        if (_hoverHighlightRenderer == null)
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.gameObject.name == "mixColba")
                {
                    _hoverHighlightRenderer = r;
                    break;
                }
            }
        }
    }

    private void Update()
    {
    }

    private void OnDestroy()
    {
        StopMixLoop();
        if (_highlightInstance != null) Destroy(_highlightInstance);
    }

    /// <summary>
    /// Returns true when <paramref name="item"/> is accepted by the puzzle's global whitelist.
    /// Unknown variants are normalised automatically via the shared equivalence map.
    /// </summary>
    public bool Accepts(ItemData item) => _context?.IsAccepted(item) ?? false;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Enables a constant emission highlight on the flask mesh.</summary>
    public void ShowHighlight()
    {
        if (_hoverHighlightRenderer == null || _isHighlighted) return;
        _isHighlighted = true;

        if (_highlightInstance == null)
        {
            _originalSharedMaterial = _hoverHighlightRenderer.sharedMaterial;
            _highlightInstance = new Material(_originalSharedMaterial);
            _highlightInstance.EnableKeyword("_EMISSION");
        }

        _highlightInstance.SetColor(EmissionColorId, _highlightColor);
        _hoverHighlightRenderer.material = _highlightInstance;
    }

    /// <summary>Removes the emission highlight and restores the original shared material.</summary>
    public void HideHighlight()
    {
        if (!_isHighlighted) return;
        _isHighlighted = false;

        if (_hoverHighlightRenderer != null && _originalSharedMaterial != null)
            _hoverHighlightRenderer.sharedMaterial = _originalSharedMaterial;
    }

    /// <summary>Adds a flask to the accumulator. Triggers export when the portion count is reached.</summary>
    public override void LoadFlask(ItemData input)
    {
        if (_isLocked) return;
        if (!Accepts(input)) return;

        _addedItems.Add(input);

        PlaySFX(_pourClip);

        // Track slag contamination.
        if (!_containsSlag && IsSlag(input))
            _containsSlag = true;

        // Animate fill.
        float fillTarget = Mathf.Clamp01(_addedItems.Count * _fillPerPortion);
        if (_liquidWobble != null)
            _liquidWobble.AnimateFillTo(fillTarget, _fillAnimDuration);

        bool willExport = _addedItems.Count >= _portionsToExport;

        if (_liquidWobble != null)
        {
            if (willExport)
            {
                // Last flask: if result is slag-contaminated, show the poured item's colour —
                // the actual output is randomised by ChemicalSynthesisController, so using
                // _spoiledResult here would show the wrong (often black) colour.
                // For a clean recipe match, show the expected result colour.
                Color previewColor;
                if (_containsSlag)
                    previewColor = input.GetLiquidColor();
                else
                {
                    ItemData previewResult = DetermineResult();
                    previewColor = previewResult != null ? previewResult.GetLiquidColor() : Color.white;
                }
                _liquidWobble.SetLiquidColor(previewColor);
            }
            else
            {
                // Intermediate flask: always use the colour of the item just poured in.
                // No need to special-case slag here — the poured slag is itself the
                // correct visual reference (e.g. red slag → red liquid).
                _liquidWobble.SetLiquidColor(input.GetLiquidColor());
            }
        }

        if (willExport)
        {
            _isLocked = true;
            IsBusy    = true;
            SetGlow(true);
            StartCoroutine(ExportCoroutine());
        }
    }

    /// <summary>Not used — mixer auto-processes when full via LoadFlask.</summary>
    public override void ProcessLoadedFlask() { }

    /// <summary>Animates the liquid fill back to zero. Call after the result item is collected.</summary>
    public void ResetLiquid()
    {
        _liquidWobble?.AnimateFillTo(0f, _fillAnimDuration);
    }

    // ── Private logic ──────────────────────────────────────────────────────────

    private IEnumerator ExportCoroutine()
    {
        ItemData result = DetermineResult();

        // Liquid colour is already set to the result in LoadFlask() when the last
        // flask was poured, so no colour update is needed here.

        _mixLoopSource = AudioManager.Instance != null
            ? AudioManager.Instance.Play3DLoop(_mixLoopClip, transform, _mixLoopVolume, _mixLoopMinDistance, _mixLoopMaxDistance)
            : null;

        yield return new WaitForSeconds(_exportDelay);

        StopMixLoop();
        PlaySFX(_mixCompleteClip);

        ResetMixer();
        CompleteWithResult(result);
    }

    private ItemData DetermineResult()
    {
        // Slag overrides everything — no recipe can save a contaminated batch.
        if (_containsSlag)
            return _spoiledResult;

        // Try each recipe in order; first full match wins.
        if (_recipes != null)
        {
            foreach (var recipe in _recipes)
            {
                if (RecipeMatches(recipe))
                    return recipe.result;
            }
        }

        // No recipe matched → spoiled result.
        return _spoiledResult;
    }

    /// <summary>
    /// A recipe matches when every ingredient in the recipe is present in the current batch
    /// and the batch contains exactly as many items as the recipe requires.
    /// Unknown items are normalised to their identified counterparts before comparison,
    /// so recipes only need to list the identified versions.
    /// </summary>
    private bool RecipeMatches(MixingRecipe recipe)
    {
        if (recipe.ingredients == null || recipe.ingredients.Length == 0) return false;
        if (recipe.ingredients.Length != _addedItems.Count) return false;

        foreach (var required in recipe.ingredients)
        {
            // Check whether any loaded item (possibly an unknown variant) normalises to the required ingredient.
            bool found = false;
            foreach (var loaded in _addedItems)
            {
                if (loaded == required || Normalize(loaded) == required)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }

        return true;
    }

    /// <summary>Delegates normalisation to the shared puzzle context.</summary>
    private ItemData Normalize(ItemData item) => _context?.Normalize(item) ?? item;

    private void StopMixLoop()
    {
        if (_mixLoopSource == null) return;
        Destroy(_mixLoopSource.gameObject);
        _mixLoopSource = null;
    }

    /// <summary>Plays a one-shot SFX through AudioManager if a clip is assigned.</summary>
    private void PlaySFX(AudioClip clip) { if (clip != null) AudioManager.Instance?.PlaySFX(clip, _sfxVolume); }

    private bool IsSlag(ItemData item)
    {
        if (_slagItems == null || _slagItems.Length == 0) return false;
        return Array.IndexOf(_slagItems, item) >= 0 ||
               Array.IndexOf(_slagItems, Normalize(item)) >= 0;
    }

    private void ResetMixer()
    {
        _addedItems.Clear();
        _isLocked    = false;
        _containsSlag = false;
        SetGlow(false);
        // Liquid visual reset is deferred to ResetLiquid(), called after player picks up.
    }

    private void SetGlow(bool on)
    {
        if (_glowRenderer == null) return;

        // Guard: never disable the main mesh renderer.
        // _glowRenderer and _hoverHighlightRenderer point to the same object when no
        // dedicated glow mesh exists — disabling it would make the entire mixer invisible.
        if (!on && _glowRenderer == _hoverHighlightRenderer) return;

        _glowRenderer.enabled = on;
    }
}
