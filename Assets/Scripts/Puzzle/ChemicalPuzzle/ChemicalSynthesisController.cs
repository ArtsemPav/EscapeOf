using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────────────────
// Synthesis pipeline types — define the full puzzle solution chain here in
// ChemicalSynthesisController's Inspector. Each SynthesisStep is one processing
// event on one device. Values are injected into device controllers on Awake.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which physical device executes a synthesis step.</summary>
public enum SynthesisDevice { Burner, Centrifuge, Mixer, Analyzer }

/// <summary>
/// One step in the synthesis chain. Choose a device, then fill ONLY the fields
/// that belong to that device — the rest are ignored at runtime.
///
/// One SynthesisStep = one processing unit:
///   Burner    — one set of valid inputs → one output
///   Centrifuge — one input → one output  (add multiple steps for multiple mappings)
///   Mixer     — one recipe: N ingredients → one output
///   Analyzer  — win condition flask id
/// </summary>
[Serializable]
public struct SynthesisStep
{
    [Tooltip("Label shown in the Inspector only — has no effect at runtime.\n" +
             "Use it to describe the role of this step.\n" +
             "Example: \"Heat the substrate\", \"Purify component A\", \"Mix serum\"")]
    public string label;

    [Tooltip("Which device processes this step.\n\n" +
             "Fill ONLY the fields that match the chosen device:\n" +
             "  BURNER     → BurnerInputs, SuccessResult, SpoiledResult\n" +
             "  CENTRIFUGE → CentrifugeInput, SuccessResult, SpoiledResult\n" +
             "  MIXER      → MixerIngredients, MixerResult  (= one recipe per step)\n" +
             "  ANALYZER   → AnalyzerWinItemId\n\n" +
             "HOW TO ADD ANOTHER STEP OF THE SAME DEVICE:\n" +
             "  • CENTRIFUGE: add a new step with a different CentrifugeInput — the device\n" +
             "    will produce different outputs for each configured input.\n" +
             "  • MIXER: add a new step with a different ingredient list — each step is\n" +
             "    one additional recipe. First fully-matched recipe wins.\n" +
             "  • BURNER: add more entries to BurnerInputs in the existing step, or add\n" +
             "    a new step if a different output is needed.")]
    public SynthesisDevice device;

    // ── Burner fields ────────────────────────────────────────────────────────

    [Tooltip("BURNER — Items accepted as 'correct' input.\n" +
             "All these produce SuccessResult when heated; anything else gives SpoiledResult.\n\n" +
             "• Unknown variants are normalised via _equivalenceMap — list identified versions only.\n" +
             "• Every item here MUST also appear in _acceptedItems (Global Item Registry).")]
    public ItemData[] burnerInputs;

    // ── Centrifuge fields ────────────────────────────────────────────────────

    [Tooltip("CENTRIFUGE — The item that produces SuccessResult when centrifuged.\n" +
             "Any other accepted item gives SpoiledResult.\n\n" +
             "• Unknown variants are normalised automatically — list the identified version.\n" +
             "• Must also appear in _acceptedItems.\n\n" +
             "HOW TO ADD ANOTHER CENTRIFUGE MAPPING:\n" +
             "  Add a new step with Device = Centrifuge and a different CentrifugeInput.\n" +
             "  Each step adds one input→output pair; the centrifuge checks all of them.")]
    public ItemData centrifugeInput;

    // ── Burner & Centrifuge shared ───────────────────────────────────────────

    [Tooltip("BURNER / CENTRIFUGE — Flask added to inventory when the correct item is processed.\n\n" +
             "HOW TO CHAIN STEPS:\n" +
             "  Set this as the input of the NEXT step that consumes it.\n" +
             "  Example: Burner.SuccessResult = ColbaA → next Centrifuge.CentrifugeInput = ColbaA.\n" +
             "  Also add it to _acceptedItems if it needs to be dropped into another device.")]
    public ItemData successResult;

    [Tooltip("BURNER / CENTRIFUGE — Flask returned when the wrong item is processed.\n" +
             "Assign any UnknownSpoiledColba — the exact variant shown to the player is\n" +
             "randomised from _unknownSlagVariants at runtime.")]
    public ItemData spoiledResult;

    // ── Mixer fields ─────────────────────────────────────────────────────────

    [Tooltip("MIXER — All items that must be present simultaneously to match this recipe.\n" +
             "Order does NOT matter — the Mixer checks presence only.\n\n" +
             "• Unknown variants are normalised automatically — list identified versions.\n" +
             "• All items must appear in _acceptedItems.\n\n" +
             "HOW TO ADD ANOTHER RECIPE:\n" +
             "  Add a new Mixer step with a different ingredient list.\n\n" +
             "HOW TO CHANGE RECIPE PRIORITY:\n" +
             "  Move Mixer steps up/down in the _synthesisSteps array —\n" +
             "  the first fully-matched recipe wins.")]
    public ItemData[] mixerIngredients;

    [Tooltip("MIXER — Flask produced when all MixerIngredients are present.\n" +
             "Typically the next step's input or the final product.")]
    public ItemData mixerResult;

    // ── Analyzer fields ──────────────────────────────────────────────────────

    [Tooltip("ANALYZER — ItemId of the flask that wins the puzzle when analyzed.\n\n" +
             "HOW TO SET:\n" +
             "  1. Open the target ItemData asset in the Inspector.\n" +
             "  2. Find 'Item Id' under the Save header.\n" +
             "     If empty, Unity uses the asset FILE NAME as the id.\n" +
             "  3. Paste that exact string here.\n\n" +
             "EXAMPLE: asset named 'SerumColba.asset' → enter: SerumColba\n\n" +
             "Unknown variants are resolved via _equivalenceMap before the id check —\n" +
             "so UnknownSerumColba wins if it maps to SerumColba.")]
    public string analyzerWinItemId;
}

/// <summary>
/// Main orchestrator for the Chemical Synthesis puzzle.
/// Implements IPuzzleDropHandler (routes item drops to the correct device via Raycast)
/// and ISaveable (persists the solved state and loaded flask IDs).
/// Attach to the root ChemicalPuzzle GameObject.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ChemicalSynthesisController : MonoBehaviour, IPuzzleDropHandler, ISaveable, IChemicalPuzzleContext
{
    [Header("Puzzle")]
    [SerializeField] private PuzzleModeController _puzzleMode;

    [Header("Devices")]
    [SerializeField] private CentrifugeController _centrifuge;
    [SerializeField] private BurnerController     _burner;
    [SerializeField] private MixerController      _mixer;
    [SerializeField] private AnalyzerController   _analyzer;
    [SerializeField] private TrashController      _trash;

    [Header("Centrifuge")]
    [Tooltip("Rotation speed of the centrifuge wheel in degrees per second.")]
    [SerializeField] private float _centrifugeWheelRotationSpeed = 360f;

    [Header("Global Item Registry")]
    [Tooltip("All ItemData assets accepted by any device in the puzzle. " +
             "Unknown variants are normalised automatically — only list the identified versions here.")]
    [SerializeField] private ItemData[] _acceptedItems;

    [Tooltip("Maps every unknown flask variant to its identified counterpart. " +
             "Shared across all devices — configure once here. " +
             "Both the whitelist check and the analyzer identification use this table.")]
    [SerializeField] private IdentificationEntry[] _equivalenceMap;

    // ─── Synthesis Pipeline ───────────────────────────────────────────────────
    // Define the full puzzle solution path here as an ordered list of steps.
    // Each step = one processing event on one device.
    // Values are injected into device controllers on Awake.
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Synthesis Steps")]
    [Tooltip("Ordered list of all processing steps in the puzzle chain.\n\n" +
             "HOW TO READ THIS LIST:\n" +
             "  Each entry is one step: a device + its recipe for that step.\n" +
             "  The order in this list is for YOUR reference — it does NOT enforce\n" +
             "  a play order; all devices are always accessible to the player.\n" +
             "  The logical order is defined by which items feed into which devices.\n\n" +
             "HOW TO ADD A NEW STEP:\n" +
             "  1. Click '+' to add an entry.\n" +
             "  2. Set Label (your notes) and Device.\n" +
             "  3. Fill ONLY the fields that match the chosen device\n" +
             "     (each field's tooltip shows which device it belongs to).\n\n" +
             "HOW TO ADD ANOTHER CENTRIFUGE STEP (e.g. to purify a second component):\n" +
             "  Add a new step, set Device = Centrifuge.\n" +
             "  Set CentrifugeInput = the new item to purify.\n" +
             "  Set SuccessResult = the purified output.\n" +
             "  The centrifuge will then recognise both inputs and produce the correct output.\n\n" +
             "HOW TO CHANGE THE SOLUTION ORDER:\n" +
             "  Change SuccessResult of step N to match the input of step N+1.\n" +
             "  Example: Burner.SuccessResult = ColbaA → Centrifuge.CentrifugeInput = ColbaA.\n\n" +
             "IMPORTANT: All ItemData assets used as inputs/outputs MUST also appear in\n" +
             "_acceptedItems (Global Item Registry) so they can be dropped into devices.")]
    [SerializeField] private SynthesisStep[] _synthesisSteps;

    [Header("Mixer — Contamination (global, applies to all Mixer steps)")]
    [Tooltip("Items that contaminate the entire Mixer batch regardless of recipe.\n" +
             "If ANY loaded flask is in this list, the result is always MixerSpoiledResult.\n\n" +
             "HOW TO ADD A NEW SLAG COLOUR:\n" +
             "  1. Create the ItemData asset (identified version).\n" +
             "  2. Add it here.\n" +
             "  3. Add it to _acceptedItems.\n" +
             "  4. Add its unknown form to _equivalenceMap.\n" +
             "  5. Add the unknown form to _unknownSlagVariants (Slag Variants section).")]
    [SerializeField] private ItemData[] _mixerSlagItems;

    [Tooltip("Flask returned when the Mixer mix is contaminated or no recipe step matched.\n" +
             "Assign any UnknownSpoiledColba — the actual variant shown to the player is\n" +
             "randomised from _unknownSlagVariants at runtime.")]
    [SerializeField] private ItemData _mixerSpoiledResult;

    [Header("Items")]
    [Tooltip("Empty flask returned to inventory when a filled flask is loaded into a device.")]
    [SerializeField] private ItemData _amptyColba;

    [Tooltip("All intermediate and ingredient flasks that must be removed from inventory when the puzzle is solved. " +
             "Do NOT include the winning flask — it stays in inventory as the reward.")]
    [SerializeField] private ItemData[] _puzzleItems;

    [Header("Slag Variants")]
    [Tooltip("Pool of all unknown-slag ItemData assets (UnknownSpoiledColba*). " +
             "When any device returns a failure result that belongs to this pool, " +
             "the result is re-rolled to a random entry — the player receives a different slag each time.")]
    [SerializeField] private ItemData[] _unknownSlagVariants;

    [Header("Drop Slots (Colliders)")]
    [Tooltip("centrifugaWheel Transform — hover detection is limited to this object and its children.")]
    [SerializeField] private Transform _centrifugeWheel;

    [Tooltip("Colba_Analize — drop zone for the analyzer.")]
    [SerializeField] private Collider _analyzerSlot;

    [Tooltip("Drop zone collider for the burner (assign after mesh is added).")]
    [SerializeField] private Collider _burnerSlot;

    [Tooltip("Drop zone collider for the mixer (assign after mesh is added).")]
    [SerializeField] private Collider _mixerSlot;

    [Header("Raycast")]
    [Tooltip("Layer mask for device colliders.")]
    [SerializeField] private LayerMask _deviceLayerMask;

    [Header("Audio")]
    [SerializeField] private AudioClip _successClip;
    [SerializeField] private AudioClip _failClip;

    [Header("Save")]
    [SerializeField] private string _saveId = "chemical_synthesis";

    // ── IChemicalPuzzleContext ────────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="item"/> (or its normalised counterpart)
    /// is listed in the global <see cref="_acceptedItems"/> whitelist.
    /// </summary>
    public bool IsAccepted(ItemData item)
    {
        if (item == null || _acceptedItems == null || _acceptedItems.Length == 0) return false;
        return System.Array.IndexOf(_acceptedItems, item) >= 0 ||
               System.Array.IndexOf(_acceptedItems, Normalize(item)) >= 0;
    }

    /// <summary>
    /// Returns the identified counterpart for <paramref name="item"/> from the shared
    /// equivalence map, or <paramref name="item"/> itself when no mapping exists.
    /// </summary>
    public ItemData Normalize(ItemData item)
    {
        if (item == null || _equivalenceMap == null) return item;
        foreach (var entry in _equivalenceMap)
            if (entry.unknown == item && entry.identified != null)
                return entry.identified;
        return item;
    }

    /// <summary>
    /// Returns true when <paramref name="item"/> belongs to the chemical puzzle
    /// (input, output, slag variant, or equivalence-map entry).
    /// Unlike <see cref="IsAccepted"/>, this includes ALL puzzle-known items,
    /// not just the device-input whitelist.
    /// </summary>
    public bool IsPuzzleItem(ItemData item)
    {
        if (item == null) return false;
        return _allPuzzleItems.Contains(item);
    }

    /// <summary>
    /// Populates <see cref="_allPuzzleItems"/> with every item the puzzle references:
    /// accepted inputs, synthesis results, slag variants, equivalence map entries,
    /// puzzle-cleanup items, and the empty flask.
    /// </summary>
    private void BuildAllPuzzleItemsSet()
    {
        void Add(ItemData item) { if (item != null) _allPuzzleItems.Add(item); }
        void AddRange(ItemData[] items) { if (items != null) foreach (var i in items) Add(i); }

        AddRange(_acceptedItems);
        AddRange(_unknownSlagVariants);
        AddRange(_mixerSlagItems);
        Add(_amptyColba);
        Add(_mixerSpoiledResult);

        if (_equivalenceMap != null)
            foreach (var entry in _equivalenceMap)
            {
                Add(entry.unknown);
                Add(entry.identified);
            }

        if (_synthesisSteps != null)
            foreach (var step in _synthesisSteps)
            {
                AddRange(step.burnerInputs);
                Add(step.centrifugeInput);
                Add(step.successResult);
                Add(step.spoiledResult);
                AddRange(step.mixerIngredients);
                Add(step.mixerResult);
            }
    }

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isSolved = _isSolved });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data.isSolved)
        {
            _isSolved = true;
            _puzzleMode?.SetSolved();
        }
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _isSolved;

    // Pairs a result item with the inventory slot index it should return to.
    // -1 means "unknown origin" — falls back to AddItem (first empty slot).
    private struct PendingResult
    {
        public ItemData item;
        public int      originSlot;
    }

    // Queue for device results that need sequential inspection panels.
    // Multiple results (e.g. 3 centrifuge flasks) are shown one at a time so
    // BeginInspection is never called while another inspection is already open.
    private readonly Queue<PendingResult> _pendingResults = new Queue<PendingResult>();

    // Set to true in OnAnalyzerSuccess so the next OnAnalyzerFlaskReturned
    // call (which delivers the winning flask) triggers the inventory cleanup.
    private bool _pendingInventoryCleanup;

    // Built from _unknownSlagVariants in Awake — O(1) membership test in RandomizeSlag.
    private readonly HashSet<ItemData> _unknownSlagSet = new HashSet<ItemData>();

    // Complete set of all items the puzzle knows about (inputs, outputs, slag, equivalence map).
    // Used by the trash to reject non-puzzle items (e.g. jars) while accepting any puzzle result.
    private readonly HashSet<ItemData> _allPuzzleItems = new HashSet<ItemData>();

    // Pre-allocated buffer for zero-GC RaycastNonAlloc calls in Update.
    private readonly RaycastHit[] _hoverHitBuffer = new RaycastHit[16];

    // ── Origin-slot tracking ──────────────────────────────────────────────────
    // Each field remembers which inventory slot index the dragged item came from.
    // Captured inside HandleDrop at drop time; used when the device returns a result.

    private int _burnerOriginSlot    = -1;
    private int _centrifugeOriginSlot = -1;
    private int _analyzerOriginSlot   = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // ── Auto-resolve components within the prefab (Inspector refs override) ─────
        if (_puzzleMode == null)
            _puzzleMode = GetComponentInParent<PuzzleModeController>(true)
                       ?? GetComponentInChildren<PuzzleModeController>(true);

        if (_centrifuge == null)
            _centrifuge = GetComponentInChildren<CentrifugeController>(true);

        if (_burner == null)
            _burner = GetComponentInChildren<BurnerController>(true);

        if (_mixer == null)
            _mixer = GetComponentInChildren<MixerController>(true);

        if (_analyzer == null)
            _analyzer = GetComponentInChildren<AnalyzerController>(true);

        // ── Auto-resolve drop-zone refs from device controllers ───────────────────
        if (_centrifugeWheel == null && _centrifuge != null)
            _centrifugeWheel = _centrifuge.WheelTransform;

        if (_analyzerSlot == null && _analyzer != null)
            _analyzerSlot = _analyzer.DropZoneCollider;

        // Burner and Mixer use the first trigger Collider on their own GameObjects
        // (the broad BoxCollider trigger that covers the device surface).
        if (_burnerSlot == null && _burner != null)
            _burnerSlot = _burner.GetComponent<Collider>();

        if (_mixerSlot == null && _mixer != null)
            _mixerSlot = _mixer.DropZoneCollider ?? _mixer.GetComponent<Collider>();

        if (_trash == null)
            _trash = GetComponentInChildren<TrashController>(true);

        // ── Inject shared context into all device controllers ─────────────────────
        _centrifuge?.Initialize(this);
        _burner?.Initialize(this);
        _mixer?.Initialize(this);
        _analyzer?.Initialize(this);

        // ── Push the central Synthesis Recipe into each device ────────────────────
        // This is where the puzzle solution path (configured above) is applied.
        // Device component fields serve as fallbacks when a recipe slot is left empty.
        ApplySynthesisRecipe();

        // ── Register events ───────────────────────────────────────────────────────
        SaveManager.Instance?.Register(this);

        if (_centrifuge != null)
        {
            _centrifuge.WheelRotationSpeed = _centrifugeWheelRotationSpeed;
            _centrifuge.OnProcessComplete += OnCentrifugeComplete;
        }

        if (_burner != null)
            _burner.OnProcessComplete += OnBurnerComplete;

        if (_mixer != null)
            _mixer.OnProcessComplete += OnMixerComplete;

        if (_analyzer != null)
        {
            _analyzer.OnSuccess += OnAnalyzerSuccess;
            _analyzer.OnFail    += OnAnalyzerFail;
            _analyzer.OnFlaskReturned += OnAnalyzerFlaskReturned;
        }

        // ── Build unknown-slag lookup ──────────────────────────────────────────
        if (_unknownSlagVariants != null)
            foreach (ItemData slag in _unknownSlagVariants)
                if (slag != null) _unknownSlagSet.Add(slag);

        // ── Build complete puzzle-item set for trash validation ───────────────
        BuildAllPuzzleItemsSet();
    }

    // ── Synthesis Pipeline Injection ──────────────────────────────────────────

    /// <summary>
    /// Aggregates all <see cref="_synthesisSteps"/> by device type and pushes the
    /// collected configs into each device controller, making this the single source
    /// of truth for the puzzle solution path.
    ///
    /// Multiple steps for the same device are merged:
    ///   Burner     — all BurnerInputs arrays are concatenated into one whitelist.
    ///   Centrifuge — each step adds one CentrifugeMapping (input → result).
    ///   Mixer      — each step adds one MixingRecipe to the recipe list.
    ///   Analyzer   — last step with a non-empty AnalyzerWinItemId wins.
    ///
    /// A device whose steps are all empty/null falls back to values already
    /// serialised on the device component itself.
    /// </summary>
    private void ApplySynthesisRecipe()
    {
        if (_synthesisSteps == null || _synthesisSteps.Length == 0) return;

        // ── Accumulate per-device data ────────────────────────────────────────
        var burnerInputs    = new List<ItemData>();
        ItemData burnerSuccess  = null;
        ItemData burnerSpoiled  = null;

        var centrifugeMappings  = new List<CentrifugeMapping>();
        ItemData centrifugeSpoiled = null;

        var mixerRecipes        = new List<MixingRecipe>();

        string analyzerWinId    = "";

        foreach (SynthesisStep step in _synthesisSteps)
        {
            switch (step.device)
            {
                case SynthesisDevice.Burner:
                    if (step.burnerInputs != null)
                        burnerInputs.AddRange(step.burnerInputs);
                    if (step.successResult != null) burnerSuccess  = step.successResult;
                    if (step.spoiledResult != null) burnerSpoiled  = step.spoiledResult;
                    break;

                case SynthesisDevice.Centrifuge:
                    if (step.centrifugeInput != null)
                        centrifugeMappings.Add(new CentrifugeMapping
                        {
                            input  = step.centrifugeInput,
                            result = step.successResult
                        });
                    if (step.spoiledResult != null && centrifugeSpoiled == null)
                        centrifugeSpoiled = step.spoiledResult;
                    break;

                case SynthesisDevice.Mixer:
                    if (step.mixerIngredients != null && step.mixerIngredients.Length > 0)
                        mixerRecipes.Add(new MixingRecipe
                        {
                            ingredients = step.mixerIngredients,
                            result      = step.mixerResult
                        });
                    break;

                case SynthesisDevice.Analyzer:
                    if (!string.IsNullOrEmpty(step.analyzerWinItemId))
                        analyzerWinId = step.analyzerWinItemId;
                    break;
            }
        }

        // ── Push into device controllers ──────────────────────────────────────
        _burner?.ApplyRecipe(
            burnerInputs.Count > 0 ? burnerInputs.ToArray() : null,
            burnerSuccess,
            burnerSpoiled);

        _centrifuge?.ApplyRecipe(
            centrifugeMappings.Count > 0 ? centrifugeMappings.ToArray() : null,
            centrifugeSpoiled);

        _mixer?.ApplyRecipe(
            mixerRecipes.Count > 0 ? mixerRecipes.ToArray() : null,
            _mixerSlagItems,
            _mixerSpoiledResult);

        _analyzer?.SetWinItemId(analyzerWinId);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);

        if (_centrifuge != null)
            _centrifuge.OnProcessComplete -= OnCentrifugeComplete;

        if (_burner != null)
            _burner.OnProcessComplete -= OnBurnerComplete;

        if (_mixer != null)
            _mixer.OnProcessComplete -= OnMixerComplete;

        if (_analyzer != null)
        {
            _analyzer.OnSuccess -= OnAnalyzerSuccess;
            _analyzer.OnFail    -= OnAnalyzerFail;
            _analyzer.OnFlaskReturned -= OnAnalyzerFlaskReturned;
        }
    }

    private void Update()
    {
        UpdateCentrifugeHover();
        UpdateBurnerHover();
        UpdateAnalyzerHover();
        UpdateMixerHover();
        UpdateTrashHover();
        UpdateClickRetrieve();
    }

    /// <summary>
    /// Each frame during a drag, raycasts into the scene and asks the centrifuge to
    /// show or hide its hover ghost depending on whether the cursor is over it.
    /// Only responds to centrifugaWheel and its children (Colba_Centrifuga*) —
    /// NOT the large cenrtpokras trigger which also parents analyzer components.
    /// </summary>
    private void UpdateCentrifugeHover()
    {
        if (_centrifuge == null) return;

        if (!PuzzleInventoryBar.IsDragging || PuzzleInventoryBar.DraggedItem == null ||
            !_centrifuge.Accepts(PuzzleInventoryBar.DraggedItem) || _centrifuge.IsFull || _centrifuge.IsBusy)
        {
            _centrifuge.HideHoverPreview();
            _centrifuge.HideHighlight();
            return;
        }

        if (Mouse.current == null || Camera.main == null)
        {
            _centrifuge.HideHoverPreview();
            _centrifuge.HideHighlight();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        int hitCount = Physics.RaycastNonAlloc(ray, _hoverHitBuffer, Mathf.Infinity,
                                               _deviceLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = _hoverHitBuffer[i].collider.transform;

            if (_centrifugeWheel != null &&
                (hitTransform == _centrifugeWheel || hitTransform.IsChildOf(_centrifugeWheel)))
            {
                int slotIdx = _centrifuge.GetNearestEmptySlotIndex(mousePos);
                _centrifuge.ShowHoverPreview(slotIdx, PuzzleInventoryBar.DraggedItem);
                _centrifuge.ShowHighlight();
                return;
            }
        }

        _centrifuge.HideHoverPreview();
        _centrifuge.HideHighlight();
    }

    /// <summary>
    /// Each frame during a drag, shows a ghost flask over the analyzer when the cursor is above it.
    /// </summary>
    private void UpdateAnalyzerHover()
    {
        if (_analyzer == null) return;

        if (!PuzzleInventoryBar.IsDragging || PuzzleInventoryBar.DraggedItem == null ||
            !_analyzer.CanDrop(PuzzleInventoryBar.DraggedItem) || _isSolved)
        {
            _analyzer.HideHoverPreview();
            _analyzer.HideHighlight();
            return;
        }

        if (Mouse.current == null || Camera.main == null)
        {
            _analyzer.HideHoverPreview();
            _analyzer.HideHighlight();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        int hitCount = Physics.RaycastNonAlloc(ray, _hoverHitBuffer, Mathf.Infinity,
                                               _deviceLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            if (_analyzerSlot != null && _hoverHitBuffer[i].collider == _analyzerSlot)
            {
                _analyzer.ShowHoverPreview(PuzzleInventoryBar.DraggedItem);
                _analyzer.ShowHighlight();
                return;
            }
        }

        _analyzer.HideHoverPreview();
        _analyzer.HideHighlight();
    }

    /// <summary>
    /// Each frame during a drag, pulses the mixer flask highlight when the cursor is above it
    /// and the dragged item is accepted by the mixer.
    /// </summary>
    private void UpdateMixerHover()
    {
        if (_mixer == null) return;

        if (!PuzzleInventoryBar.IsDragging || PuzzleInventoryBar.DraggedItem == null ||
            !_mixer.Accepts(PuzzleInventoryBar.DraggedItem) || _mixer.IsFull || _mixer.IsBusy)
        {
            _mixer.HideHighlight();
            return;
        }

        if (Mouse.current == null || Camera.main == null)
        {
            _mixer.HideHighlight();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        int hitCount = Physics.RaycastNonAlloc(ray, _hoverHitBuffer, Mathf.Infinity,
                                               _deviceLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            if (_mixerSlot != null && _hoverHitBuffer[i].collider == _mixerSlot)
            {
                _mixer.ShowHighlight();
                return;
            }
        }

        _mixer.HideHighlight();
    }

    /// <summary>
    /// Each frame during a drag, highlights the trash bin when the cursor is above it
    /// and the player is dragging any item.
    /// </summary>
    private void UpdateTrashHover()
    {
        if (_trash == null) return;

        if (!PuzzleInventoryBar.IsDragging || PuzzleInventoryBar.DraggedItem == null ||
            PuzzleInventoryBar.DraggedItem == _amptyColba ||
            !IsPuzzleItem(PuzzleInventoryBar.DraggedItem))
        {
            _trash.HideHighlight();
            return;
        }

        if (Mouse.current == null || Camera.main == null)
        {
            _trash.HideHighlight();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        int hitCount = Physics.RaycastNonAlloc(ray, _hoverHitBuffer, Mathf.Infinity,
                                               _deviceLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hoverHitBuffer[i].collider;
            if (_trash.DropZoneCollider != null && col == _trash.DropZoneCollider)
            {
                _trash.ShowHighlight();
                return;
            }

            if (_trash.DropZoneCollider == null &&
                col.GetComponentInParent<TrashController>() == _trash)
            {
                _trash.ShowHighlight();
                return;
            }
        }

        _trash.HideHighlight();
    }

    /// <summary>
    /// Each frame during a drag, shows a ghost flask over the burner when the cursor is above it.
    /// </summary>
    private void UpdateBurnerHover()
    {
        if (_burner == null) return;

        if (!PuzzleInventoryBar.IsDragging || PuzzleInventoryBar.DraggedItem == null ||
            !_burner.CanDrop(PuzzleInventoryBar.DraggedItem) || _burner.IsBusy)
        {
            _burner.HideHoverPreview();
            _burner.HideHighlight();
            return;
        }

        if (Mouse.current == null || Camera.main == null)
        {
            _burner.HideHoverPreview();
            _burner.HideHighlight();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        int hitCount = Physics.RaycastNonAlloc(ray, _hoverHitBuffer, Mathf.Infinity,
                                               _deviceLayerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            if (_hoverHitBuffer[i].collider.GetComponentInParent<BurnerController>() == _burner)
            {
                _burner.ShowHoverPreview(PuzzleInventoryBar.DraggedItem);
                _burner.ShowHighlight();
                return;
            }
        }

        _burner.HideHoverPreview();
        _burner.HideHighlight();
    }

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// On left-click (when not dragging), checks if the player clicked on a placed flask
    /// in the centrifuge or analyzer and returns it to inventory.
    /// </summary>
    private void UpdateClickRetrieve()
    {
        if (PuzzleInventoryBar.IsDragging) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (Camera.main == null) return;

        Vector2      mousePos = Mouse.current.position.ReadValue();
        Ray          ray      = Camera.main.ScreenPointToRay(mousePos);
        RaycastHit[] hits     = Physics.RaycastAll(ray, Mathf.Infinity, _deviceLayerMask,
                                                   QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            Transform hitT = hit.collider.transform;

            // ── Centrifuge: retrieve flask from the nearest occupied slot ──────
            if (_centrifuge != null && _centrifugeWheel != null && !_centrifuge.IsBusy &&
                (hitT == _centrifugeWheel || hitT.IsChildOf(_centrifugeWheel)))
            {
                ItemData flask = _centrifuge.TryRetrieveFlask(mousePos);
                if (flask != null) { InventorySystem.Instance?.AddItem(flask); return; }
            }

            // ── Analyzer: retrieve flask before the cycle starts ───────────────
            if (_analyzerSlot != null && hit.collider == _analyzerSlot &&
                _analyzer != null && !_analyzer.IsBusy)
            {
                ItemData flask = _analyzer.TryRetrieveFlask();
                if (flask != null) { InventorySystem.Instance?.AddItem(flask); return; }
            }

            // ── Mixer: retrieve the single poured flask (only while not locked) ──
            if (_mixerSlot != null && hit.collider == _mixerSlot &&
                _mixer != null && !_mixer.IsFull && !_mixer.IsBusy)
            {
                ItemData flask = _mixer.TryRetrieveFlask();
                if (flask != null)
                {
                    // The empty-colba replacement was placed in inventory when the flask
                    // was dropped; swap it back for the original flask.
                    if (_amptyColba != null && InventorySystem.Instance != null &&
                        !InventorySystem.Instance.ReplaceItem(_amptyColba, flask))
                        InventorySystem.Instance.AddItem(flask);
                    else if (_amptyColba == null)
                        InventorySystem.Instance?.AddItem(flask);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Determines which device the player dropped an item on via screen-space raycast.
    /// Hits are sorted by distance so the closest collider wins.
    /// Centrifuge detection is scoped to centrifugaWheel and its children only —
    /// avoids false positives from cenrtpokras which also parents the analyzer.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        if (item == null || Camera.main == null) return false;

        // Capture the inventory slot the dragged item came from — used to return
        // the result back to the same position after device processing completes.
        int originSlot = PuzzleInventoryBar.DragSourceSlotIndex;

        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, _deviceLayerMask,
                                               QueryTriggerInteraction.Collide);

        // Sort by distance — closest collider wins.
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            Collider   col  = hit.collider;
            Transform  hitT = col.transform;

            // ── Centrifuge: only centrifugaWheel and its children (Colba_Centrifuga*) ──
            if (_centrifuge != null && _centrifugeWheel != null &&
                (hitT == _centrifugeWheel || hitT.IsChildOf(_centrifugeWheel)))
            {
                if (!_centrifuge.Accepts(item) || _centrifuge.IsFull || _centrifuge.IsBusy) return false;

                _centrifugeOriginSlot = originSlot;
                int slotIdx = _centrifuge.GetNearestEmptySlotIndex(screenPosition);
                _centrifuge.PrepareSlot(slotIdx);
                _centrifuge.LoadFlask(item);
                return true;
            }

            // ── Analyzer ──────────────────────────────────────────────────────
            if (_analyzerSlot != null && col == _analyzerSlot)
            {
                if (_analyzer == null || !_analyzer.Accepts(item)) return false;

                _analyzerOriginSlot = originSlot;
                _analyzer.LoadFlask(item);
                return true;
            }

            // ── Burner ────────────────────────────────────────────────────────
            if (_burnerSlot != null && col == _burnerSlot)
            {
                if (_burner == null || _burner.IsBusy || !_burner.CanDrop(item)) return false;

                _burnerOriginSlot = originSlot;
                _burner.LoadFlask(item);
                _burner.ProcessLoadedFlask();
                return true;
            }

            // ── Mixer ─────────────────────────────────────────────────────────
            if (_mixerSlot != null && col == _mixerSlot)
            {
                if (_mixer == null || _mixer.IsFull || !_mixer.Accepts(item)) return false;

                _mixer.LoadFlask(item);
                _mixer.ProcessLoadedFlask();
                replacement = _amptyColba;
                return true;
            }

            // ── Trash: empties the flask, shows inspection panel for the empty one ──
            if (_trash != null)
            {
                bool hitTrash = (_trash.DropZoneCollider != null && col == _trash.DropZoneCollider)
                             || (_trash.DropZoneCollider == null &&
                                 col.GetComponentInParent<TrashController>() == _trash);

                if (hitTrash)
                {
                    // Empty flask cannot be discarded.
                    if (item == _amptyColba) return false;

                    // Only puzzle-relevant items can be trashed (reject jars and other non-puzzle items).
                    if (!IsPuzzleItem(item)) return false;

                    _trash.PlayDropSound();

                    // Show the empty flask via the inspection panel (same flow as devices).
                    // Skip preview if the item is already empty — just discard it silently.
                    if (item != _amptyColba && _amptyColba != null)
                    {
                        // Trash result (empty flask) has no origin slot — goes to first free.
                        _pendingResults.Enqueue(new PendingResult { item = _amptyColba, originSlot = -1 });
                        TryShowNextResult();
                    }
                    // Consume the dragged item — no replacement returned.
                    return true;
                }
            }
        }

        return false;
    }

    // ── Device Callbacks ──────────────────────────────────────────────────────

    /// <summary>
    /// If <paramref name="result"/> is a registered unknown-slag variant, picks a random one
    /// from <see cref="_unknownSlagVariants"/>. This ensures the player receives a different-coloured
    /// slag on each failure, adding to the puzzle's misdirection.
    /// </summary>
    private ItemData RandomizeSlag(ItemData result)
    {
        if (result == null || _unknownSlagVariants == null || _unknownSlagVariants.Length == 0)
            return result;
        if (!_unknownSlagSet.Contains(result))
            return result;

        return _unknownSlagVariants[UnityEngine.Random.Range(0, _unknownSlagVariants.Length)];
    }

    private void OnBurnerComplete(ItemData result)
    {
        if (result == null) return;
        _pendingResults.Enqueue(new PendingResult { item = RandomizeSlag(result), originSlot = _burnerOriginSlot });
        _burnerOriginSlot = -1;
        TryShowNextResult();
    }

    private void OnCentrifugeComplete(ItemData result)
    {
        if (result == null) return;
        // _centrifugeOriginSlot captures the last drag's source slot.
        // For the typical single-flask case this is exact. For multi-flask batches,
        // only the first result lands in the right slot; subsequent results fall
        // back to first-available via PlaceItemAt's built-in fallback.
        _pendingResults.Enqueue(new PendingResult { item = RandomizeSlag(result), originSlot = _centrifugeOriginSlot });
        TryShowNextResult();
    }

    /// <summary>
    /// Shows the next queued device result in the inspection panel.
    /// Called after each pickup callback so results are presented one at a time.
    /// </summary>
    private void TryShowNextResult()
    {
        if (_pendingResults.Count == 0) return;

        // If an inspection is already open, do nothing — the pickup callback will
        // call TryShowNextResult again when the current inspection closes.
        if (ItemInspector.Instance != null && ItemInspector.Instance.IsInspecting) return;

        PendingResult pending = _pendingResults.Dequeue();

        if (ItemInspector.Instance != null)
        {
            ItemInspector.Instance.BeginInspection(pending.item, null, (item) =>
            {
                PlaceOrAddItem(item, pending.originSlot);
                TryShowNextResult();
            });
        }
        else
        {
            PlaceOrAddItem(pending.item, pending.originSlot);
            TryShowNextResult();
        }
    }

    /// <summary>
    /// Places <paramref name="item"/> at <paramref name="originSlot"/> when that slot is still
    /// empty, otherwise falls back to <see cref="InventorySystem.AddItem"/> (first free slot).
    /// </summary>
    private void PlaceOrAddItem(ItemData item, int originSlot)
    {
        if (item == null) return;
        if (originSlot >= 0 && InventorySystem.Instance != null)
            InventorySystem.Instance.PlaceItemAt(originSlot, item);
        else
            InventorySystem.Instance?.AddItem(item);
    }

    private void OnMixerComplete(ItemData result)
    {
        if (result == null) return;
        ItemData finalResult = RandomizeSlag(result);

        // After the player picks up the mixed result, drain the liquid back to zero.
        if (ItemInspector.Instance != null)
        {
            ItemInspector.Instance.BeginInspection(finalResult, null, (item) =>
            {
                if (_amptyColba == null || InventorySystem.Instance == null ||
                    !InventorySystem.Instance.ReplaceItem(_amptyColba, item))
                    InventorySystem.Instance?.AddItem(item);
                _mixer?.ResetLiquid();
            });
        }
        else
        {
            InventorySystem.Instance?.AddItem(finalResult);
            _mixer?.ResetLiquid();
        }
    }

    private void OnAnalyzerSuccess()
    {
        _isSolved = true;
        _pendingInventoryCleanup = true;
        AudioManager.Instance?.PlaySFX(_successClip);

        // SetSolved and Save are deferred to OnAnalyzerFlaskReturned so they fire
        // only after the winning flask is in inventory and all puzzle items are purged.
    }

    private void OnAnalyzerFail()
    {
        AudioManager.Instance?.PlaySFX(_failClip);
    }

    private void OnAnalyzerFlaskReturned(ItemData flask)
    {
        if (flask == null) return;

        // Capture and clear the flag immediately so the lambda can't fire cleanup twice.
        bool doCleanup = _pendingInventoryCleanup;
        _pendingInventoryCleanup = false;

        int originSlot = _analyzerOriginSlot;
        _analyzerOriginSlot = -1;

        if (ItemInspector.Instance != null)
        {
            ItemInspector.Instance.BeginInspection(flask, null, (item) =>
            {
                PlaceOrAddItem(item, originSlot);

                if (doCleanup)
                {
                    // 1. Purge all intermediate puzzle items — winning flask is now in inventory.
                    ClearPuzzleItemsFromInventory();

                    // 2. Mark the puzzle solved and persist only after the inventory is clean.
                    _puzzleMode?.SetSolved();
                    SaveManager.Instance?.Save();
                }
            });
        }
        else
        {
            PlaceOrAddItem(flask, originSlot);

            if (doCleanup)
            {
                ClearPuzzleItemsFromInventory();
                _puzzleMode?.SetSolved();
                SaveManager.Instance?.Save();
            }
        }
    }

    /// <summary>
    /// Removes every entry in <see cref="_puzzleItems"/> from the player's inventory.
    /// Called once after the winning flask is delivered, so the player keeps only the
    /// reward flask and loses all intermediate ingredients, by-products, and empties.
    /// </summary>
    private void ClearPuzzleItemsFromInventory()
    {
        if (_puzzleItems == null || InventorySystem.Instance == null) return;

        foreach (ItemData item in _puzzleItems)
        {
            if (item == null) continue;

            // Remove all copies of this item (player may have picked up duplicates).
            while (InventorySystem.Instance.RemoveItem(item)) { }
        }
    }
}
