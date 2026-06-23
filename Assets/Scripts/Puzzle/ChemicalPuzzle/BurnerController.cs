using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the burner (gorelka) device.
/// Any item in _droppableItems can be placed on the burner and shows a hover preview.
/// Only items in _successItems produce _successResult; everything else produces _spoiledResult.
/// ProcessLoadedFlask is called immediately after LoadFlask by ChemicalSynthesisController.
/// </summary>
public class BurnerController : ChemicalDeviceBase
{
    [Header("References")]
    [Tooltip("Transform marking where the flask should land on the burner (used as parent for flask visuals).")]
    [SerializeField] private Transform _flaskSpot;

    [Tooltip("Collider of the Spot object — its bounds.center is used as the flask placement position.")]
    [SerializeField] private Collider _spotCollider;

    [Tooltip("Flame VFX GameObject — enabled during heating, disabled when done.")]
    [SerializeField] private GameObject _flameVFX;

    [Header("Settings")]
    [Tooltip("Heating duration in seconds.")]
    [SerializeField] private float _duration = 5f;

    [Header("Flask Placement")]
    [Tooltip("Desired uniform world-space scale for the flask prefab while it sits on the burner.")]
    [SerializeField] [Min(0.001f)] private float _flaskPlacementScale = 0.15f;

    [Tooltip("Height above the spot from which the flask begins its drop animation.")]
    [SerializeField] private float _dropHeight = 0.05f;

    [Tooltip("Duration of the drop animation in seconds.")]
    [SerializeField] private float _dropDuration = 0.4f;

    [Header("Audio")]
    [Tooltip("Played once when the flask lands on the burner spot.")]
    [SerializeField] private AudioClip _flaskDropClip;

    [Tooltip("Looping 3D sound played while the flame is active.")]
    [SerializeField] private AudioClip _burnLoopClip;

    [SerializeField] [Range(0f, 1f)] private float _burnLoopVolume = 0.8f;
    [SerializeField] private float _burnLoopMinDistance = 0.5f;
    [SerializeField] private float _burnLoopMaxDistance = 5f;

    [Tooltip("Played once when heating completes.")]
    [SerializeField] private AudioClip _burnCompleteClip;

    [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

    [Header("Ghost Preview")]
    [Tooltip("Optional material applied to the hover ghost. Reuse FlaskGhost.mat from the centrifuge.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Amplitude of the bob animation for the hover ghost (world-space meters).")]
    [SerializeField] private float _hoverBobAmplitude = 0.015f;

    [Tooltip("Speed of the bob animation cycle.")]
    [SerializeField] private float _hoverBobSpeed = 2.5f;

    [Header("Hover Highlight")]
    [Tooltip("Renderer that lights up when a valid item is dragged over the burner. " +
             "Auto-resolved to the first child MeshRenderer if left empty.")]
    [SerializeField] private Renderer _hoverHighlightRenderer;

    [Tooltip("Emission colour added on top of the material while a valid item hovers. " +
             "Keep values below 1 for a subtle glow.")]
    [SerializeField] private Color _highlightColor = new Color(0f, 0.5f, 0.3f);

    // ── Results — set at runtime by ChemicalSynthesisController.ApplySynthesisRecipe() ──
    // Configure in ChemicalSynthesisController → Synthesis Steps (Device: Burner).
    private ItemData[] _successItems;
    private ItemData   _successResult;
    private ItemData   _spoiledResult;

    // ── Shared context (injected by ChemicalSynthesisController) ──────────────

    private IChemicalPuzzleContext _context;

    /// <summary>Injects the shared puzzle context. Called by ChemicalSynthesisController in Awake.</summary>
    public void Initialize(IChemicalPuzzleContext context) => _context = context;

    /// <summary>
    /// Overrides the burner recipe at runtime from the central Synthesis Recipe plan.
    /// Called by ChemicalSynthesisController.ApplySynthesisRecipe() in Awake.
    /// Non-null arguments replace the values serialized directly on this component.
    /// </summary>
    public void ApplyRecipe(ItemData[] successItems, ItemData successResult, ItemData spoiledResult)
    {
        if (successItems  != null && successItems.Length  > 0) _successItems  = successItems;
        if (successResult != null)                             _successResult = successResult;
        if (spoiledResult != null)                             _spoiledResult = spoiledResult;
    }

    // ── Runtime state ──────────────────────────────────────────────────────────

    private ItemData   _loadedFlask;
    private GameObject _flaskObject;
    private GameObject _hoverGhost;
    private Coroutine  _bobCoroutine;
    private AudioSource _burnLoopSource;

    // ── Highlight state ────────────────────────────────────────────────────────

    private bool _isHighlighted;
    private Material _originalSharedMaterial;
    private Material _highlightInstance;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        HideHoverPreview();
        HideHighlight();
        StopBurnLoop();
        if (_highlightInstance != null) Destroy(_highlightInstance);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="item"/> is accepted by the puzzle's global whitelist.
    /// Unknown variants are normalised automatically via the shared equivalence map.
    /// Used by ChemicalSynthesisController for gating drops and the hover preview.
    /// </summary>
    public bool CanDrop(ItemData item) => _context?.IsAccepted(item) ?? false;

    /// <summary>The Spot collider used as the drop-zone by the orchestrator.</summary>
    public Collider DropZoneCollider => _spotCollider;

    /// <summary>Enables a constant emission highlight on the burner mesh.</summary>
    public void ShowHighlight()
    {
        if (_hoverHighlightRenderer == null)
            _hoverHighlightRenderer = GetComponentInChildren<MeshRenderer>();

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

    /// <summary>
    /// Shows a ghost preview of <paramref name="item"/> at the flask spot.
    /// Safe to call every frame — rebuilds only when the item changes.
    /// </summary>
    public void ShowHoverPreview(ItemData item)
    {
        if (item == null || _flaskSpot == null) { HideHoverPreview(); return; }

        // Ghost already shown for this item — nothing to rebuild.
        if (_hoverGhost != null) return;

        if (item.inspectionPrefab == null) return;

        Vector3 targetCenter = PlacementCenter;

        _hoverGhost = Instantiate(item.inspectionPrefab, targetCenter, _flaskSpot.rotation, _flaskSpot);
        _hoverGhost.transform.localScale = ComputeLocalScaleForWorldScale(_flaskSpot, _flaskPlacementScale);

        // Align the mesh bounds center with the Spot collider center.
        Vector3 offset = ComputeBoundsCenter(_hoverGhost) - targetCenter;
        _hoverGhost.transform.position -= offset;

        if (_ghostMaterial != null)
        {
            foreach (var rend in _hoverGhost.GetComponentsInChildren<Renderer>(true))
            {
                // Preserve the liquid renderer's original materials so the flask colour is visible.
                if (rend.gameObject.name == "liquid") continue;

                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMaterial;
                rend.materials = mats;
            }
        }

        foreach (var col in _hoverGhost.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        _bobCoroutine = StartCoroutine(BobGhostRoutine(_hoverGhost.transform.position));
    }

    /// <summary>Destroys the hover ghost and stops the bob animation.</summary>
    public void HideHoverPreview()
    {
        if (_bobCoroutine != null) { StopCoroutine(_bobCoroutine); _bobCoroutine = null; }
        if (_hoverGhost   != null) { Destroy(_hoverGhost); _hoverGhost = null; }
    }

    /// <summary>Stores the flask data and spawns its visual on the burner spot.</summary>
    public override void LoadFlask(ItemData input)
    {
        HideHoverPreview();
        _loadedFlask = input;

        if (input?.inspectionPrefab != null && _flaskSpot != null)
            StartCoroutine(DropFlaskRoutine(input.inspectionPrefab));
    }

    /// <summary>Starts the heating cycle immediately after the flask is placed.</summary>
    public override void ProcessLoadedFlask()
    {
        if (_loadedFlask == null || IsBusy) return;
        StartCoroutine(HeatCoroutine());
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private IEnumerator DropFlaskRoutine(GameObject prefab)
    {
        Vector3 targetCenter = PlacementCenter;

        _flaskObject = Instantiate(prefab, targetCenter, _flaskSpot.rotation, _flaskSpot);
        _flaskObject.transform.localScale = ComputeLocalScaleForWorldScale(_flaskSpot, _flaskPlacementScale);

        foreach (var col in _flaskObject.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Align mesh bounds center with the Spot collider center.
        Vector3 centerOffset = ComputeBoundsCenter(_flaskObject) - targetCenter;
        Vector3 endWorld   = targetCenter - centerOffset;
        Vector3 startWorld = endWorld + Vector3.up * _dropHeight;

        _flaskObject.transform.position = startWorld;

        float elapsed = 0f;
        while (elapsed < _dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _dropDuration);
            _flaskObject.transform.position = Vector3.Lerp(startWorld, endWorld, t * t); // ease-in
            yield return null;
        }

        _flaskObject.transform.position = endWorld;
    }

    private IEnumerator HeatCoroutine()
    {
        IsBusy = true;

        // Wait for drop animation to finish before igniting.
        yield return new WaitForSeconds(_dropDuration);

        PlaySFX(_flaskDropClip);

        if (_flameVFX != null)
            _flameVFX.SetActive(true);

        _burnLoopSource = AudioManager.Instance != null
            ? AudioManager.Instance.Play3DLoop(_burnLoopClip, transform, _burnLoopVolume, _burnLoopMinDistance, _burnLoopMaxDistance)
            : null;

        yield return new WaitForSeconds(_duration);

        StopBurnLoop();

        if (_flameVFX != null)
            _flameVFX.SetActive(false);

        PlaySFX(_burnCompleteClip);

        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }

        ItemData result = IsSuccessItem(_loadedFlask) ? _successResult : _spoiledResult;
        _loadedFlask = null;

        CompleteWithResult(result);
    }

    private bool IsSuccessItem(ItemData item)
    {
        if (_successItems == null || _successItems.Length == 0) return false;
        return Array.IndexOf(_successItems, item) >= 0 ||
               Array.IndexOf(_successItems, Normalize(item)) >= 0;
    }

    /// <summary>Delegates normalisation to the shared puzzle context.</summary>
    private ItemData Normalize(ItemData item) => _context?.Normalize(item) ?? item;

    private void StopBurnLoop()
    {
        if (_burnLoopSource == null) return;
        Destroy(_burnLoopSource.gameObject);
        _burnLoopSource = null;
    }

    /// <summary>Plays a one-shot SFX through AudioManager if a clip is assigned.</summary>
    private void PlaySFX(AudioClip clip) { if (clip != null) AudioManager.Instance?.PlaySFX(clip, _sfxVolume); }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private IEnumerator BobGhostRoutine(Vector3 basePos)
    {
        float t = 0f;
        while (_hoverGhost != null)
        {
            t += Time.deltaTime * _hoverBobSpeed;
            _hoverGhost.transform.position = basePos + Vector3.up * (_hoverBobAmplitude * (1f + Mathf.Sin(t)));
            yield return null;
        }
    }

    /// <summary>
    /// Returns the world-space center where the flask should be placed.
    /// Prefers the Spot collider's bounds center; falls back to _flaskSpot position.
    /// </summary>
    private Vector3 PlacementCenter =>
        _spotCollider != null ? _spotCollider.bounds.center :
        _flaskSpot    != null ? _flaskSpot.position : transform.position;

    private static Vector3 ComputeLocalScaleForWorldScale(Transform parent, float worldScale)
    {
        Vector3 ps = parent.lossyScale;
        return new Vector3(
            Mathf.Approximately(ps.x, 0f) ? worldScale : worldScale / ps.x,
            Mathf.Approximately(ps.y, 0f) ? worldScale : worldScale / ps.y,
            Mathf.Approximately(ps.z, 0f) ? worldScale : worldScale / ps.z);
    }

    private static Vector3 ComputeBoundsCenter(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0) return obj.transform.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.center;
    }
}
