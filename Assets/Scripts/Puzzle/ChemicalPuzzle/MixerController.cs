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

    [Header("Accepted Items")]
    [Tooltip("Whitelist of ItemData assets the mixer accepts. Leave empty to accept everything.")]
    [SerializeField] private ItemData[] _acceptedItems;

    [Header("Slag Items")]
    [Tooltip("Items that contaminate the entire mix. If any loaded item is in this list the result is always _spoiledResult, regardless of other ingredients.")]
    [SerializeField] private ItemData[] _slagItems;

    [Header("Recipes")]
    [Tooltip("Ordered list of valid mixing recipes. First match wins.")]
    [SerializeField] private MixingRecipe[] _recipes;

    [Header("Results")]
    [Tooltip("Returned when a mix is contaminated by a slag item or matches no recipe.")]
    [SerializeField] private ItemData _spoiledResult;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private readonly List<ItemData> _addedItems = new List<ItemData>();
    private bool _isLocked;
    private bool _containsSlag;

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
    }

    /// <summary>
    /// Returns true when <paramref name="item"/> is in the accepted-items whitelist.
    /// An empty whitelist rejects everything — fill it in the Inspector.
    /// </summary>
    public bool Accepts(ItemData item)
    {
        if (item == null || _acceptedItems == null || _acceptedItems.Length == 0) return false;
        return Array.IndexOf(_acceptedItems, item) >= 0;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Adds a flask to the accumulator. Triggers export when the portion count is reached.</summary>
    public override void LoadFlask(ItemData input)
    {
        if (_isLocked) return;
        if (!Accepts(input)) return;

        _addedItems.Add(input);

        // Track slag contamination.
        if (!_containsSlag && IsSlag(input))
            _containsSlag = true;

        // Animate fill and tint.
        float fillTarget = Mathf.Clamp01(_addedItems.Count * _fillPerPortion);
        if (_liquidWobble != null)
        {
            _liquidWobble.AnimateFillTo(fillTarget, _fillAnimDuration);

            // Slag tints the liquid a sickly colour; otherwise use the item's own colour.
            _liquidWobble.SetLiquidColor(_containsSlag
                ? _spoiledResult != null ? _spoiledResult.GetLiquidColor() : Color.black
                : input.GetLiquidColor());
        }

        if (_addedItems.Count >= _portionsToExport)
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

        // Tint liquid to the result colour so the player sees what's being produced.
        if (_liquidWobble != null && result != null)
            _liquidWobble.SetLiquidColor(result.GetLiquidColor());

        yield return new WaitForSeconds(_exportDelay);

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
    /// </summary>
    private bool RecipeMatches(MixingRecipe recipe)
    {
        if (recipe.ingredients == null || recipe.ingredients.Length == 0) return false;
        if (recipe.ingredients.Length != _addedItems.Count) return false;

        foreach (var required in recipe.ingredients)
        {
            if (!_addedItems.Contains(required)) return false;
        }

        return true;
    }

    private bool IsSlag(ItemData item)
    {
        if (_slagItems == null || _slagItems.Length == 0) return false;
        return Array.IndexOf(_slagItems, item) >= 0;
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
        _glowRenderer.enabled = on;
    }
}
