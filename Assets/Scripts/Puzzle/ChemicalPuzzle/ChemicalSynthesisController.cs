using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Main orchestrator for the Chemical Synthesis puzzle.
/// Implements IPuzzleDropHandler (routes item drops to the correct device via Raycast)
/// and ISaveable (persists the solved state and loaded flask IDs).
/// Attach to the root ChemicalPuzzle GameObject.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ChemicalSynthesisController : MonoBehaviour, IPuzzleDropHandler, ISaveable
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

    // Queue for device results that need sequential inspection panels.
    // Multiple results (e.g. 3 centrifuge flasks) are shown one at a time so
    // BeginInspection is never called while another inspection is already open.
    private readonly Queue<ItemData> _pendingResults = new Queue<ItemData>();

    // Set to true in OnAnalyzerSuccess so the next OnAnalyzerFlaskReturned
    // call (which delivers the winning flask) triggers the inventory cleanup.
    private bool _pendingInventoryCleanup;

    // Built from _unknownSlagVariants in Awake — O(1) membership test in RandomizeSlag.
    private readonly HashSet<ItemData> _unknownSlagSet = new HashSet<ItemData>();

    // Pre-allocated buffer for zero-GC RaycastNonAlloc calls in Update.
    private readonly RaycastHit[] _hoverHitBuffer = new RaycastHit[16];

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

        // ── Register events ───────────────────────────────────────────────────────
        SaveManager.Instance?.Register(this);

        if (_centrifuge != null)
        {
            _centrifuge.WheelRotationSpeed = _centrifugeWheelRotationSpeed;
            _centrifuge.OnProcessComplete += OnDeviceComplete;
        }

        if (_burner != null)
            _burner.OnProcessComplete += OnDeviceComplete;

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
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);

        if (_centrifuge != null)
            _centrifuge.OnProcessComplete -= OnDeviceComplete;

        if (_burner != null)
            _burner.OnProcessComplete -= OnDeviceComplete;

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
            PuzzleInventoryBar.DraggedItem == _amptyColba)
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

                int slotIdx = _centrifuge.GetNearestEmptySlotIndex(screenPosition);
                _centrifuge.PrepareSlot(slotIdx);
                _centrifuge.LoadFlask(item);
                return true;
            }

            // ── Analyzer ──────────────────────────────────────────────────────
            if (_analyzerSlot != null && col == _analyzerSlot)
            {
                if (_analyzer == null || !_analyzer.Accepts(item)) return false;

                _analyzer.LoadFlask(item);
                return true;
            }

            // ── Burner ────────────────────────────────────────────────────────
            if (_burnerSlot != null && col == _burnerSlot)
            {
                if (_burner == null || _burner.IsBusy || !_burner.CanDrop(item)) return false;

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

                    // Show the empty flask via the inspection panel (same flow as devices).
                    // Skip preview if the item is already empty — just discard it silently.
                    if (item != _amptyColba && _amptyColba != null)
                    {
                        _pendingResults.Enqueue(_amptyColba);
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

    private void OnDeviceComplete(ItemData result)
    {
        if (result == null) return;
        _pendingResults.Enqueue(RandomizeSlag(result));
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

        ItemData result = _pendingResults.Dequeue();

        if (ItemInspector.Instance != null)
        {
            ItemInspector.Instance.BeginInspection(result, null, (item) =>
            {
                InventorySystem.Instance?.AddItem(item);
                TryShowNextResult();
            });
        }
        else
        {
            InventorySystem.Instance?.AddItem(result);
            TryShowNextResult();
        }
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
        InventorySystem.Instance?.AddItem(flask);

        if (!_pendingInventoryCleanup) return;

        // 1. Purge all intermediate puzzle items — winning flask is already in inventory.
        _pendingInventoryCleanup = false;
        ClearPuzzleItemsFromInventory();

        // 2. Mark the puzzle solved and persist only after the inventory is clean.
        _puzzleMode?.SetSolved();
        SaveManager.Instance?.Save();
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
