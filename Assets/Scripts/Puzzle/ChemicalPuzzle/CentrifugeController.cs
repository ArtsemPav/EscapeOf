using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps one specific input item to its centrifuge output.
/// Add one entry per "clean" item the centrifuge can produce.
/// Configure via ChemicalSynthesisController → Synthesis Steps (Device: Centrifuge).
/// </summary>
[Serializable]
public struct CentrifugeMapping
{
    [Tooltip("The item (identified version) that produces Result when centrifuged.\n" +
             "Unknown variants are normalised automatically via the shared equivalence map.\n" +
             "Must also appear in _acceptedItems on ChemicalSynthesisController.")]
    public ItemData input;

    [Tooltip("Flask returned when this Input item is centrifuged.\n" +
             "Typically the next step's input or a Mixer ingredient.")]
    public ItemData result;
}

/// <summary>
/// Controls the centrifuge device with three independent flask slots.
/// Each slot accepts items from the <see cref="_acceptedItems"/> whitelist.
/// The spin cycle starts when button2 is pressed (at least one slot must be occupied).
/// After spinning, every loaded flask produces a result that fires OnProcessComplete.
/// </summary>
public class CentrifugeController : ChemicalDeviceBase
{
    private const int SlotCount = 3;

    [Header("References")]
    [Tooltip("button2 — starts the centrifuge cycle.")]
    [SerializeField] private ButtonPressAnimation _startButton;

    [Tooltip("centrifugaWheel transform — rotated procedurally during the spin cycle.")]
    [SerializeField] private Transform _wheelTransform;

    [SerializeField] private CentrifugeScreenController _screen;

    [Header("Slots")]
    [Tooltip("Transforms of the three visual slots (Colba_Centrifuga1 / 2 / 3).")]
    [SerializeField] private Transform[] _slotTransforms;

    [Header("Ghost Preview")]
    [Tooltip("Optional material applied to the hover ghost. Leave null to use the item's own material.")]
    [SerializeField] private Material _ghostMaterial;

    [Header("Hover Highlight")]
    [Tooltip("Renderer that lights up when a valid item is dragged over the centrifuge. " +
             "Auto-resolved to the first child MeshRenderer if left empty.")]
    [SerializeField] private Renderer _hoverHighlightRenderer;

    [Tooltip("Emission colour added on top of the material while a valid item hovers. " +
             "Keep values below 1 for a subtle glow.")]
    [SerializeField] private Color _highlightColor = new Color(0f, 0.5f, 0.3f);

    // ── Results — set at runtime by ChemicalSynthesisController.ApplySynthesisRecipe() ──
    // Configure in ChemicalSynthesisController → Synthesis Steps (Device: Centrifuge).

    [Header("Flask Placement")]
    [Tooltip("Uniform scale applied to flask prefabs when placed into centrifuge slots. Tune until flasks match the physical centrifuge size.")]
    [SerializeField] private float _flaskPlacementScale = 1f;

    [Header("Drop Animation")]
    [SerializeField] private float _dropHeight       = 0.05f;
    [SerializeField] private float _dropDuration     = 0.4f;
    [SerializeField] private float _hoverBobAmplitude = 0.015f;
    [SerializeField] private float _hoverBobSpeed    = 2.5f;

    [Header("Settings")]
    [SerializeField] private float _duration = 5f;

    [Header("Audio")]
    [Tooltip("Played once when a flask is dropped into a slot.")]
    [SerializeField] private AudioClip _flaskDropClip;

    [Tooltip("Played once when the start button is pressed.")]
    [SerializeField] private AudioClip _buttonClip;

    [Tooltip("Looping 3D sound played during the spin cycle.")]
    [SerializeField] private AudioClip _spinLoopClip;

    [SerializeField] [Range(0f, 1f)] private float _spinLoopVolume = 0.8f;
    [SerializeField] private float _spinLoopMinDistance = 0.5f;
    [SerializeField] private float _spinLoopMaxDistance = 6f;

    [Tooltip("Played once when the spin cycle completes.")]
    [SerializeField] private AudioClip _spinCompleteClip;

    [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

    /// <summary>
    /// Rotation speed of the centrifuge wheel in degrees per second.
    /// Set externally by <see cref="ChemicalSynthesisController"/> so the value
    /// lives in one place on the main orchestrator.
    /// </summary>
    public float WheelRotationSpeed { get; set; } = 360f;

    /// <summary>The centrifugaWheel transform used for drop-zone detection by the orchestrator.</summary>
    public Transform WheelTransform => _wheelTransform;

    [Header("Results")]
    [Tooltip("Input→output pairs for the centrifuge.\n" +
             "Each entry maps one identified item to its clean result.\n" +
             "RECOMMENDED: Configure via ChemicalSynthesisController → Synthesis Steps (Device: Centrifuge).\n" +
             "Values set there override this field at runtime.")]
    [SerializeField] private CentrifugeMapping[] _cleanMappings;
    [Tooltip("Flask returned when a centrifuged item matches no entry in _cleanMappings.\n" +
             "RECOMMENDED: Configure via ChemicalSynthesisController → Synthesis Steps (Device: Centrifuge).")]
    [SerializeField] private ItemData _spoiledResult;

    // ── Shared context (injected by ChemicalSynthesisController) ──────────────

    private IChemicalPuzzleContext _context;

    /// <summary>Injects the shared puzzle context. Called by ChemicalSynthesisController in Awake.</summary>
    public void Initialize(IChemicalPuzzleContext context) => _context = context;

    /// <summary>
    /// Overrides the centrifuge mappings at runtime from the central Synthesis Steps plan.
    /// Called by ChemicalSynthesisController.ApplySynthesisRecipe() in Awake.
    /// Each Centrifuge step in the plan adds one CentrifugeMapping entry.
    /// Non-null / non-empty arguments replace values serialised on this component.
    /// </summary>
    public void ApplyRecipe(CentrifugeMapping[] cleanMappings, ItemData spoiledResult)
    {
        if (cleanMappings != null && cleanMappings.Length > 0) _cleanMappings = cleanMappings;
        if (spoiledResult != null)                             _spoiledResult  = spoiledResult;
    }

    // ── Per-slot state ─────────────────────────────────────────────────────────

    private readonly ItemData[]   _loadedFlasks       = new ItemData[SlotCount];
    private readonly GameObject[] _loadedFlaskObjects  = new GameObject[SlotCount];

    private int _pendingSlotIndex = -1;

    private AudioSource _spinLoopSource;

    // ── Hover preview ──────────────────────────────────────────────────────────

    private GameObject _hoverGhost;
    private int        _hoveredSlotIndex = -1;
    private Coroutine  _bobCoroutine;

    // ── Highlight state ────────────────────────────────────────────────────────

    private bool _isHighlighted;
    private Material _originalSharedMaterial;
    private Material _highlightInstance;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>True when every slot is occupied.</summary>
    public bool IsFull
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (_loadedFlasks[i] == null) return false;
            return true;
        }
    }

    /// <summary>True when at least one slot contains a flask.</summary>
    public bool HasAnyFlask
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (_loadedFlasks[i] != null) return true;
            return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="item"/> is accepted by the puzzle's global whitelist.
    /// Unknown variants are normalised automatically via the shared equivalence map.
    /// </summary>
    public bool Accepts(ItemData item) => _context?.IsAccepted(item) ?? false;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_startButton != null)
            _startButton.OnPressed += OnButtonPressed;
    }

    private void OnDestroy()
    {
        if (_startButton != null)
            _startButton.OnPressed -= OnButtonPressed;

        HideHoverPreview();
        HideHighlight();
        StopSpinLoop();
        if (_highlightInstance != null) Destroy(_highlightInstance);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Reserves the slot index that the next LoadFlask call will use.</summary>
    public void PrepareSlot(int slotIndex) => _pendingSlotIndex = slotIndex;

    /// <summary>
    /// Returns the index of the slot nearest to <paramref name="screenPos"/> that is still empty,
    /// or -1 when all slots are occupied.
    /// </summary>
    public int GetNearestEmptySlotIndex(Vector2 screenPos)
    {
        if (Camera.main == null) return GetFirstEmptySlot();

        int   nearest  = -1;
        float minDist  = float.MaxValue;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_loadedFlasks[i] != null) continue;
            if (_slotTransforms == null || i >= _slotTransforms.Length || _slotTransforms[i] == null) continue;

            Vector3 sp   = Camera.main.WorldToScreenPoint(_slotTransforms[i].position);
            float   dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
            if (dist < minDist) { minDist = dist; nearest = i; }
        }

        return nearest >= 0 ? nearest : GetFirstEmptySlot();
    }

    /// <summary>
    /// Shows a ghost preview of <paramref name="item"/> at the given slot.
    /// Safe to call every frame — rebuilds the ghost only when the slot changes.
    /// </summary>
    public void ShowHoverPreview(int slotIndex, ItemData item)
    {
        if (slotIndex < 0 || item == null ||
            _slotTransforms == null || slotIndex >= _slotTransforms.Length ||
            _slotTransforms[slotIndex] == null)
        {
            HideHoverPreview();
            return;
        }

        // Ghost is already at the correct slot — nothing to rebuild.
        if (_hoveredSlotIndex == slotIndex && _hoverGhost != null) return;

        HideHoverPreview();
        _hoveredSlotIndex = slotIndex;

        if (item.inspectionPrefab == null) return;

        Transform slot = _slotTransforms[slotIndex];
        _hoverGhost = Instantiate(item.inspectionPrefab, slot.position, slot.rotation, slot);
        _hoverGhost.transform.localScale = ComputeLocalScaleForWorldScale(slot, _flaskPlacementScale);

        // Align the mesh bounds center with the slot position.
        Vector3 offset = ComputeBoundsCenter(_hoverGhost) - slot.position;
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

    /// <summary>Destroys the hover ghost and clears the tracked slot.</summary>
    public void HideHoverPreview()
    {
        if (_bobCoroutine != null) { StopCoroutine(_bobCoroutine); _bobCoroutine = null; }
        if (_hoverGhost   != null) { Destroy(_hoverGhost); _hoverGhost = null; }
        _hoveredSlotIndex = -1;
    }

    /// <summary>Enables a constant emission highlight on the centrifuge mesh.</summary>
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
    /// Places the flask into the slot prepared by <see cref="PrepareSlot"/>,
    /// or into the first available empty slot as a fallback.
    /// Animates the flask model dropping into the slot.
    /// </summary>
    public override void LoadFlask(ItemData input)
    {
        if (input == null || IsBusy) return;

        int slotIdx = ResolveTargetSlot();
        if (slotIdx < 0) return; // no empty slot

        _pendingSlotIndex      = -1;
        _loadedFlasks[slotIdx] = input;

        HideHoverPreview();
        PlaySFX(_flaskDropClip);

        if (input.inspectionPrefab != null &&
            _slotTransforms != null && slotIdx < _slotTransforms.Length &&
            _slotTransforms[slotIdx] != null)
        {
            StartCoroutine(DropFlaskRoutine(input.inspectionPrefab, _slotTransforms[slotIdx], slotIdx));
        }
    }

    /// <summary>Not used directly — centrifuge starts via button press.</summary>
    public override void ProcessLoadedFlask() { }

    /// <summary>
    /// Retrieves the flask from the slot nearest to <paramref name="screenPos"/> if the
    /// centrifuge is idle. Returns the flask data (caller adds it to inventory) or null.
    /// </summary>
    public ItemData TryRetrieveFlask(Vector2 screenPos)
    {
        if (IsBusy) return null;

        int slotIdx = GetNearestOccupiedSlotIndex(screenPos);
        if (slotIdx < 0) return null;

        ItemData flask         = _loadedFlasks[slotIdx];
        _loadedFlasks[slotIdx] = null;

        if (_loadedFlaskObjects[slotIdx] != null)
        {
            Destroy(_loadedFlaskObjects[slotIdx]);
            _loadedFlaskObjects[slotIdx] = null;
        }

        return flask;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void OnButtonPressed()
    {
        if (!HasAnyFlask || IsBusy) return;
        PlaySFX(_buttonClip);
        StartSpin();
    }

    private void StartSpin()
    {
        IsBusy = true;
        StartCoroutine(SpinCoroutine());
    }

    private IEnumerator SpinCoroutine()
    {
        float remaining = _duration;

        _spinLoopSource = AudioManager.Instance != null
            ? AudioManager.Instance.Play3DLoop(_spinLoopClip, transform, _spinLoopVolume, _spinLoopMinDistance, _spinLoopMaxDistance)
            : null;

        while (remaining > 0f)
        {
            _screen?.UpdateTimer(remaining);

            if (_wheelTransform != null)
                _wheelTransform.Rotate(Vector3.up, WheelRotationSpeed * Time.deltaTime, Space.Self);

            remaining -= Time.deltaTime;
            yield return null;
        }

        StopSpinLoop();
        PlaySFX(_spinCompleteClip);

        // Collect all results before clearing slot state.
        var results = new List<ItemData>(SlotCount);

        for (int i = 0; i < SlotCount; i++)
        {
            if (_loadedFlasks[i] == null) continue;

            ItemData result = GetResultForFlask(_loadedFlasks[i]);

            results.Add(result);
            _loadedFlasks[i] = null;

            if (_loadedFlaskObjects[i] != null)
            {
                Destroy(_loadedFlaskObjects[i]);
                _loadedFlaskObjects[i] = null;
            }
        }

        _screen?.ShowIdle();

        // Clear busy flag once and fire one event per occupied slot.
        CompleteWithResults(results);
    }

    private IEnumerator DropFlaskRoutine(GameObject prefab, Transform slot, int slotIdx)
    {
        // Parent to the slot so the flask rotates with centrifugaWheel.
        var flaskObj = Instantiate(prefab, slot.position, slot.rotation, slot);
        flaskObj.transform.localScale = ComputeLocalScaleForWorldScale(slot, _flaskPlacementScale);
        _loadedFlaskObjects[slotIdx] = flaskObj;

        foreach (var col in flaskObj.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Align mesh bounds center with slot position in world space, then animate
        // down from above. We work in world space, then convert to local each frame.
        Vector3 centerOffset = ComputeBoundsCenter(flaskObj) - slot.position;
        Vector3 endWorld   = slot.position - centerOffset;
        Vector3 startWorld = endWorld + Vector3.up * _dropHeight;

        flaskObj.transform.position = startWorld;

        float elapsed = 0f;
        while (elapsed < _dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _dropDuration);
            flaskObj.transform.position = Vector3.Lerp(startWorld, endWorld, t * t); // ease-in
            yield return null;
        }

        flaskObj.transform.position = endWorld;
    }

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
    /// Computes the local scale needed to achieve a uniform world scale of
    /// <paramref name="worldScale"/> when the object is parented to <paramref name="parent"/>.
    /// </summary>
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
        var renderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0) return obj.transform.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.center;
    }

    /// <summary>Delegates normalisation to the shared puzzle context.</summary>
    private ItemData Normalize(ItemData item) => _context?.Normalize(item) ?? item;

    /// <summary>
    /// Looks up the output for <paramref name="flask"/> in <see cref="_cleanMappings"/>.
    /// Checks both the original item and its normalised (identified) counterpart.
    /// Returns <see cref="_spoiledResult"/> if no mapping matches.
    /// </summary>
    private ItemData GetResultForFlask(ItemData flask)
    {
        if (flask == null || _cleanMappings == null || _cleanMappings.Length == 0)
            return _spoiledResult;

        ItemData normalized = Normalize(flask);
        foreach (CentrifugeMapping mapping in _cleanMappings)
        {
            if (mapping.input == null) continue;
            if (mapping.input == flask || mapping.input == normalized)
                return mapping.result ?? _spoiledResult;
        }
        return _spoiledResult;
    }

    private void StopSpinLoop()
    {
        if (_spinLoopSource == null) return;
        Destroy(_spinLoopSource.gameObject);
        _spinLoopSource = null;
    }

    /// <summary>Plays a one-shot SFX through AudioManager if a clip is assigned.</summary>
    private void PlaySFX(AudioClip clip) { if (clip != null) AudioManager.Instance?.PlaySFX(clip, _sfxVolume); }

    private int GetFirstEmptySlot()    {
        for (int i = 0; i < SlotCount; i++)
            if (_loadedFlasks[i] == null) return i;
        return -1;
    }

    private int GetNearestOccupiedSlotIndex(Vector2 screenPos)
    {
        if (Camera.main == null)
        {
            for (int i = 0; i < SlotCount; i++)
                if (_loadedFlasks[i] != null) return i;
            return -1;
        }

        int   nearest = -1;
        float minDist = float.MaxValue;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_loadedFlasks[i] == null) continue;
            if (_slotTransforms == null || i >= _slotTransforms.Length || _slotTransforms[i] == null) continue;

            Vector3 sp   = Camera.main.WorldToScreenPoint(_slotTransforms[i].position);
            float   dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
            if (dist < minDist) { minDist = dist; nearest = i; }
        }

        return nearest;
    }

    /// <summary>
    /// Returns the pending slot index if it is valid and empty,
    /// otherwise falls back to the first empty slot.
    /// </summary>
    private int ResolveTargetSlot()
    {
        if (_pendingSlotIndex >= 0 && _pendingSlotIndex < SlotCount &&
            _loadedFlasks[_pendingSlotIndex] == null)
        {
            return _pendingSlotIndex;
        }

        return GetFirstEmptySlot();
    }
}
