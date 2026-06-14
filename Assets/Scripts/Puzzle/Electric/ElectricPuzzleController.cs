using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates the electric panel wire-connection puzzle.
/// Delegates camera, input blocking, ESC handling, and cursor management to
/// <see cref="PuzzleModeController"/> — the same component used by all other puzzles.
///
/// Flow:
///   1. Player clicks the panel → PuzzleInteractable on the same GameObject calls
///      PuzzleModeController.EnterPuzzleMode(), which blends the camera in,
///      frees the cursor, and blocks FPS input.
///   2. Mouse raycasts against terminal colliders using Camera.main.ScreenPointToRay.
///   3. LMB on a colored terminal → start wire drag; wire end follows the cursor in 3D.
///   4. LMB release on a neutral terminal → connect wire, evaluate solution.
///   5. LMB on an occupied terminal → lift the wire for redirection.
///   6. LMB on the lever → immediately resets all wires if wrong, or completes the puzzle if correct.
///   7. RMB → cancel active drag without closing.
///   8. ESC → handled automatically by PuzzleModeController via InputManager.
///
/// Setup:
///   • Attach to the root "electric" GameObject (Interactable Layer + BoxCollider).
///   • Add <see cref="PuzzleModeController"/> and <see cref="PuzzleInteractable"/> to the same GameObject.
///   • Set PuzzleModeController._showInventoryBar = true to show the fuse inventory bar.
///   • <see cref="_lampLight"/> is found automatically in children if not assigned in the Inspector.
///   • Assign <see cref="_coloredTerminals"/> and <see cref="_neutralTerminals"/> in order 0..5.
///   • Assign <see cref="_puzzleData"/> (ElectricPuzzleData ScriptableObject).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(PuzzleModeController))]
public class ElectricPuzzleController : MonoBehaviour, ISaveable, IPuzzleDropHandler
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int TerminalCount     = 6;
    private const int DisconnectedValue = -1;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Root panel GameObject in Canvas — opened/closed via UIManager.")]
    [SerializeField] private GameObject _panel;

    [Tooltip("Colored terminals in order 0..5 (Terminal_Colored_0..5).")]
    [SerializeField] private ElectricTerminal[] _coloredTerminals = new ElectricTerminal[TerminalCount];

    [Tooltip("Neutral terminals in order 0..5 (Terminal_Neutral_0..5).")]
    [SerializeField] private ElectricTerminal[] _neutralTerminals = new ElectricTerminal[TerminalCount];

    [Tooltip("Puzzle configuration: solution mapping and wire colors.")]
    [SerializeField] private ElectricPuzzleData _puzzleData;

    [Tooltip("GameObject to activate when the puzzle is fully solved (lever pulled).")]
    [SerializeField] private GameObject _solvedObject;

    [Header("Lamp")]
    [Tooltip("Point Light inside the electric prefab used as the indicator lamp.")]
    [SerializeField] private Light _lampLight;

    [Tooltip("Lamp color in the default (unsolved) state.")]
    [SerializeField] private Color _lampDefaultColor = Color.red;

    [Tooltip("Lamp color when wires are correctly connected (wires solved, lever not yet pulled).")]
    [SerializeField] private Color _lampSolvedColor = Color.green;

    [Header("Lever")]
    [Tooltip("ElectricLever component on pCube17. Enabled after wires are correctly connected.")]
    [SerializeField] private ElectricLever _lever;

    [Tooltip("Particle system played when the lever is pulled with incorrect wire connections.")]
    [SerializeField] private ParticleSystem _wrongPullParticles;

    [Header("Settings")]
    [Tooltip("Layer mask of terminal colliders used for mouse raycasting while the panel is open.")]
    [SerializeField] private LayerMask _terminalLayer;

    [Tooltip("Prefab used as visual cap at both wire ends (assign pCylinder21 prefab here).")]
    [SerializeField] private GameObject _wireCapPrefab;

    [Tooltip("Material applied to wire LineRenderers. Leave empty for a default unlit material.")]
    [SerializeField] private Material _wireMaterial;

    [Tooltip("Optional: Renderer to copy Light Layers from. If empty, the script will find one in children.")]
    [SerializeField] private Renderer _referenceRenderer;

    [Header("Wire Settings")]
    [Tooltip("Simulation and rendering settings shared by all wires in this puzzle.")]
    [SerializeField] private ElectricWireSettings _wireSettings = new ElectricWireSettings();

    [Header("Sounds")]
    [Tooltip("Played when the fuse is inserted into the anchor.")]
    [SerializeField] private AudioClip _fuseInsertClip;

    [Tooltip("Played when a wire is connected to a neutral terminal.")]
    [SerializeField] private AudioClip _wireConnectClip;

    [Tooltip("Played when a wire is disconnected (picked up from a terminal).")]
    [SerializeField] private AudioClip _wireDisconnectClip;

    [Tooltip("Played when the puzzle is fully solved (lever pulled with correct wires).")]
    [SerializeField] private AudioClip _solvedClip;

    [Tooltip("Played when the lever snaps back after a wrong combination.")]
    [SerializeField] private AudioClip _wrongPullClip;

    [Header("Sound Volumes")]
    [SerializeField, Range(0f, 1f)] private float _fuseInsertVolume    = 1f;
    [SerializeField, Range(0f, 1f)] private float _wireConnectVolume   = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _wireDisconnectVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _solvedVolume        = 1f;
    [SerializeField, Range(0f, 1f)] private float _wrongPullVolume     = 0.6f;

    [Header("Events")]
    [Tooltip("Items that can be applied to this puzzle (fuse). " +
             "Leave empty to disable the inventory bar for this puzzle.")]
    [SerializeField] private ItemData[] _acceptedItems;

    [Tooltip("Sphere collider on the Safeguardanchor GameObject — the drop zone for the fuse.")]
    [SerializeField] private Collider _fuseAnchorCollider;

    [Tooltip("Transform where the fuse prefab is spawned when inserted. Usually Safeguardanchor.")]
    [SerializeField] private Transform _fuseAnchorTransform;

    [Tooltip("Prefab instantiated at the anchor when the fuse is inserted (e.g. SafeGuard.prefab). " +
             "Falls back to item.inspectionPrefab if left empty.")]
    [SerializeField] private GameObject _fusePrefab;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _isSolved;
    private bool _wiresCorrect;
    private bool _fuseInserted;
    private string _fuseItemId;
    private GameObject _fuseInstance;

    private ElectricWire     _activeWire;
    private ElectricTerminal _activeColoredTerminal;

    private readonly int[]          _connections = new int[TerminalCount];
    private readonly ElectricWire[] _wires       = new ElectricWire[TerminalCount];

    private PuzzleModeController _controller;
    private uint                 _cachedRenderingLayerMask;

    /// <summary>
    /// Returns the rendering layer mask (Light Layers) that should be applied to dynamic wires.
    /// This is either copied from the assigned Reference Renderer or found in children.
    /// </summary>
    public uint RenderingLayerMask => _cachedRenderingLayerMask;

    // Pending save state applied in Start()
    private bool   _pendingLoad;
    private int[]  _pendingConnections;
    private bool   _pendingSolved;
    private bool   _pendingFuseInserted;
    private string _pendingFuseItemId;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "electric_puzzle";

    [Serializable]
    private struct SaveData
    {
        public bool   isSolved;
        public bool   wiresCorrect;
        public int[]  connections;
        public bool   fuseInserted;
        public string fuseItemId;
    }

    /// <summary>Serialises current connections, solved flag, and fuse state.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new SaveData
    {
        isSolved     = _isSolved,
        wiresCorrect = _wiresCorrect,
        connections  = (int[])_connections.Clone(),
        fuseInserted = _fuseInserted,
        fuseItemId   = _fuseItemId,
    });

    /// <summary>Stores loaded data — applied in Start() after all terminals are ready.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _pendingSolved       = data.isSolved;
        _pendingConnections  = data.connections ?? new int[TerminalCount];
        _pendingFuseInserted = data.fuseInserted;
        _pendingFuseItemId   = data.fuseItemId;
        Array.Resize(ref _pendingConnections, TerminalCount);
        _pendingLoad = true;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<PuzzleModeController>();

        // Cache the rendering layer mask for wires.
        if (_referenceRenderer == null)
            _referenceRenderer = GetComponentInChildren<Renderer>();
        
        if (_referenceRenderer != null)
            _cachedRenderingLayerMask = _referenceRenderer.renderingLayerMask;
        else
            _cachedRenderingLayerMask = 1; // Default layer mask if nothing found.

        if (_lampLight == null)
            _lampLight = GetComponentInChildren<Light>(includeInactive: true);
        if (_lever == null)
            _lever = GetComponentInChildren<ElectricLever>(includeInactive: true);

        if (_wrongPullParticles != null)
            _wrongPullParticles.gameObject.SetActive(false);

        if (_lever != null)
            _lever.OnPulled += HandleLeverPulled;

        InitConnections();
        SaveManager.Instance?.Register(this);
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnEntered += HandleEntered;
            _controller.OnExited  += HandleExited;
            _controller.OnSolved  += HandleSolved;
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited  -= HandleExited;
            _controller.OnSolved  -= HandleSolved;
        }
    }

    private void Start()
    {
        if (_pendingLoad)
        {
            _pendingLoad = false;
            ApplyPendingLoad();
            ElectricWire.JointPresettle();
        }

        if (_fuseInserted)
        {
            var fuseItem = FindAcceptedItemById(_fuseItemId);
            SpawnFuseVisual(fuseItem);
            if (_fuseAnchorCollider != null) _fuseAnchorCollider.enabled = false;
        }

        RefreshVisuals();
    }

    private void OnDestroy()
    {
        if (_lever != null)
            _lever.OnPulled -= HandleLeverPulled;

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (!_isOpen) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        // RMB — cancel current drag without closing
        if (mouse.rightButton.wasPressedThisFrame)
        {
            CancelActiveDrag();
            return;
        }

        // Update dragged wire end to follow cursor on the panel surface
        if (_activeWire != null)
            UpdateDragPoint(mouse.position.ReadValue());

        // LMB pressed → start drag from a terminal
        if (mouse.leftButton.wasPressedThisFrame)
            HandleMousePress(mouse.position.ReadValue());

        // LMB released while dragging → connect or destroy
        if (mouse.leftButton.wasReleasedThisFrame && _activeWire != null)
            HandleMouseRelease(mouse.position.ReadValue());
    }

    // ── PuzzleModeController event handlers ───────────────────────────────────

    private void HandleEntered()
    {
        _isOpen = true;

        if (_panel != null)
            UIManager.Instance?.OpenPanel(_panel);

        _lever?.SetInteractionEnabled(true);
    }

    private void HandleExited()
    {
        _isOpen = false;

        CancelActiveDrag();
        _lever?.SetInteractionEnabled(false);

        if (_panel != null)
            UIManager.Instance?.ClosePanel(_panel);
    }

    private void HandleSolved()
    {
        // Fired by PuzzleModeController.SetSolved() — no additional action needed here.
    }

    // ── Mouse interaction ─────────────────────────────────────────────────────

    /// <summary>
    /// Called on LMB press. Starts a new wire drag, picks up an existing wire,
    /// or interacts with the lever.
    /// </summary>
    private void HandleMousePress(Vector2 screenPos)
    {
        if (_activeWire != null) return;

        if (!_isSolved && TryInteractLever(screenPos))
            return;

        var terminal = RaycastTerminal(screenPos);
        if (terminal == null) return;

        if (terminal.Type == ElectricTerminal.TerminalType.Colored)
        {
            if (terminal.IsFree)
                StartDrag(terminal);
            else
                PickUpWire(terminal);
        }
        else
        {
            if (!terminal.IsFree)
                PickUpWireFromNeutral(terminal);
        }
    }

    /// <summary>
    /// Raycasts for the lever and calls Interact if hit.
    /// If wires are incorrect, resets all connections immediately before the animation starts.
    /// Returns true if the lever was successfully clicked.
    /// </summary>
    private bool TryInteractLever(Vector2 screenPos)
    {
        if (!_fuseInserted) return false;
        if (_lever == null || !_lever.CanInteract()) return false;
        if (Camera.main == null) return false;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 50f, _terminalLayer, QueryTriggerInteraction.Collide))
            return false;

        var lever = hit.collider.GetComponent<ElectricLever>()
                 ?? hit.collider.GetComponentInParent<ElectricLever>();
        if (lever == null) return false;

        if (!_wiresCorrect)
        {
            PlayWrongPullParticles();
            ResetAllWires();
        }

        lever.Interact();
        return true;
    }

    /// <summary>
    /// Called on LMB release while a wire is being dragged.
    /// Connects to a neutral terminal if cursor is over one; otherwise destroys the wire.
    /// </summary>
    private void HandleMouseRelease(Vector2 screenPos)
    {
        var terminal = RaycastTerminal(screenPos);

        if (terminal != null && terminal.Type == ElectricTerminal.TerminalType.Neutral)
        {
            if (!terminal.IsFree)
                DisconnectWireAtNeutral(terminal);

            ConnectActiveWire(terminal);
        }
        else
        {
            CancelActiveDrag();
        }
    }

    /// <summary>Raycasts against terminal colliders and returns the hit ElectricTerminal, or null.</summary>
    private ElectricTerminal RaycastTerminal(Vector2 screenPos)
    {
        if (Camera.main == null) return null;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 50f, _terminalLayer, QueryTriggerInteraction.Collide))
            return null;

        return hit.collider.GetComponent<ElectricTerminal>()
            ?? hit.collider.GetComponentInParent<ElectricTerminal>();
    }

    private void UpdateDragPoint(Vector2 screenPos)
    {
        if (Camera.main == null) return;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        var plane = new Plane(-Camera.main.transform.forward,
                              _activeColoredTerminal.transform.position);
        if (plane.Raycast(ray, out float enter))
            _activeWire.SetDragPoint(ray.GetPoint(enter));
    }

    // ── Wire lifecycle ────────────────────────────────────────────────────────

    private void StartDrag(ElectricTerminal colored)
    {
        var wire = CreateWireObject(colored.Index);
        Color color = _puzzleData != null ? _puzzleData.WireColors[colored.Index] : Color.white;
        wire.Init(colored.transform, colored.Index, color, _wireMaterial, _wireCapPrefab, _wireSettings);

        _wires[colored.Index]  = wire;
        _activeWire            = wire;
        _activeColoredTerminal = colored;
        colored.AttachWire(wire);
    }

    private void ConnectActiveWire(ElectricTerminal neutral)
    {
        _activeWire.ConnectEnd(neutral.transform);
        neutral.AttachWire(_activeWire);
        _connections[_activeColoredTerminal.Index] = neutral.Index;

        _activeWire            = null;
        _activeColoredTerminal = null;

        PlaySFX(_wireConnectClip, _wireConnectVolume);
        EvaluateWires();
        SaveManager.Instance?.Save();
    }

    private void CancelActiveDrag()
    {
        if (_activeWire == null) return;

        Destroy(_activeWire.gameObject);
        _wires[_activeWire.ColoredIndex] = null;
        _connections[_activeWire.ColoredIndex] = DisconnectedValue;
        _activeColoredTerminal?.DetachWire();

        _activeWire            = null;
        _activeColoredTerminal = null;

        EvaluateWires();
        SaveManager.Instance?.Save();
    }

    private void PickUpWire(ElectricTerminal colored)
    {
        var wire = _wires[colored.Index];
        if (wire == null) return;

        int nIdx = _connections[colored.Index];
        if (nIdx != DisconnectedValue)
        {
            _neutralTerminals[nIdx].DetachWire();
            _connections[colored.Index] = DisconnectedValue;
        }

        wire.DisconnectEnd();
        _activeWire            = wire;
        _activeColoredTerminal = colored;

        PlaySFX(_wireDisconnectClip, _wireDisconnectVolume);
        EvaluateWires();
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Lifts the wire connected to <paramref name="neutral"/> so the user can redirect it.
    /// The wire's colored origin stays attached; only the neutral end is freed.
    /// </summary>
    private void PickUpWireFromNeutral(ElectricTerminal neutral)
    {
        for (int i = 0; i < TerminalCount; i++)
        {
            if (_connections[i] != neutral.Index) continue;

            var wire = _wires[i];
            if (wire == null) break;

            neutral.DetachWire();
            _connections[i] = DisconnectedValue;
            wire.DisconnectEnd();

            _activeWire            = wire;
            _activeColoredTerminal = _coloredTerminals[i];

            PlaySFX(_wireDisconnectClip, _wireDisconnectVolume);
            break;
        }

        EvaluateWires();
        SaveManager.Instance?.Save();
    }

    private void DisconnectWireAtNeutral(ElectricTerminal neutral)
    {
        for (int i = 0; i < TerminalCount; i++)
        {
            if (_connections[i] != neutral.Index) continue;

            if (_wires[i] != null) { Destroy(_wires[i].gameObject); _wires[i] = null; }
            _coloredTerminals[i].DetachWire();
            _connections[i] = DisconnectedValue;
            neutral.DetachWire();
            break;
        }

        EvaluateWires();
        SaveManager.Instance?.Save();
    }

    // ── Solution check ────────────────────────────────────────────────────────

    /// <summary>Returns true when every connection matches the puzzle solution.</summary>
    private bool CheckSolution()
    {
        if (_puzzleData == null) return false;
        var solution = _puzzleData.Solution;
        for (int i = 0; i < TerminalCount; i++)
            if (_connections[i] != solution[i]) return false;
        return true;
    }

    /// <summary>
    /// Re-evaluates wire connections and updates lamp color.
    /// Saves when wires transition to the correct state for the first time.
    /// </summary>
    private void EvaluateWires()
    {
        if (_isSolved) return;

        bool correct = CheckSolution();
        if (correct == _wiresCorrect) return;

        _wiresCorrect = correct;
        UpdateLamp();

        if (correct)
            SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Called by <see cref="ElectricLever.OnPulled"/> when the lever animation completes.
    /// Correct wires → puzzle fully solved via PuzzleModeController.SetSolved().
    /// Wrong wires → wires were already cleared on press; only return the lever here.
    /// </summary>
    private void HandleLeverPulled()
    {
        if (_wiresCorrect)
        {
            _isSolved = true;
            if (_solvedObject != null) _solvedObject.SetActive(true);
            PlaySFX(_solvedClip, _solvedVolume);
            SaveManager.Instance?.Save();

            // Delegate exit to PuzzleModeController — consistent with all other puzzles.
            _controller?.SetSolved();
        }
        else
        {
            PlaySFX(_wrongPullClip, _wrongPullVolume);
            _lever?.Reset();
        }
    }

    /// <summary>
    /// Activates the wrong-pull particle system, plays it once, then deactivates it.
    /// </summary>
    private void PlayWrongPullParticles()
    {
        if (_wrongPullParticles == null) return;
        _wrongPullParticles.gameObject.SetActive(true);
        _wrongPullParticles.Play();
        StartCoroutine(DeactivateParticlesWhenDone());
    }

    private IEnumerator DeactivateParticlesWhenDone()
    {
        yield return new WaitWhile(() => _wrongPullParticles != null && _wrongPullParticles.isPlaying);
        if (_wrongPullParticles != null)
            _wrongPullParticles.gameObject.SetActive(false);
    }

    /// <summary>
    /// Destroys every placed wire and resets all terminal and connection state.
    /// Called immediately when the lever is pulled with incorrect connections.
    /// </summary>
    private void ResetAllWires()
    {
        if (_activeWire != null)
        {
            Destroy(_activeWire.gameObject);
            _activeWire            = null;
            _activeColoredTerminal = null;
        }

        for (int i = 0; i < TerminalCount; i++)
        {
            if (_wires[i] != null)
            {
                Destroy(_wires[i].gameObject);
                _wires[i] = null;
            }
            _connections[i] = DisconnectedValue;
        }

        foreach (var terminal in _coloredTerminals) terminal?.DetachWire();
        foreach (var terminal in _neutralTerminals)  terminal?.DetachWire();

        _wiresCorrect = false;
        UpdateLamp();
    }

    /// <summary>
    /// Restores lamp color and lever state from saved data without triggering side-effects.
    /// Called in Start() after save data has been applied.
    /// </summary>
    private void RefreshVisuals()
    {
        if (_isSolved)
        {
            _wiresCorrect = true;
            if (_solvedObject != null) _solvedObject.SetActive(true);
            _lever?.SetPulledQuiet();
        }
        else
        {
            _wiresCorrect = CheckSolution();
        }

        UpdateLamp();
    }

    /// <summary>Updates the indicator lamp based on the complete puzzle state.</summary>
    private void UpdateLamp()
    {
        if (_lampLight == null) return;

        if (!_fuseInserted)
        {
            _lampLight.enabled = false;
            return;
        }

        _lampLight.enabled = true;
        _lampLight.color = (_isSolved || _wiresCorrect) ? _lampSolvedColor : _lampDefaultColor;
    }

    // ── IPuzzleDropHandler ─────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a fuse dragged from the PuzzleInventoryBar.
    /// Raycasts against the Safeguardanchor collider — drop is valid only when
    /// the cursor lands on the anchor. The bar removes the item from inventory on true.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        if (item == null) return false;
        if (_acceptedItems == null || Array.IndexOf(_acceptedItems, item) < 0) return false;
        if (_fuseInserted) return false;
        if (_fuseAnchorCollider == null || Camera.main == null) return false;

        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, 50f, _terminalLayer, QueryTriggerInteraction.Collide))
            return false;

        if (hit.collider != _fuseAnchorCollider) return false;

        InsertFuse(item);
        return true;
    }

    /// <summary>Applies fuse insertion: sets state, spawns visual, disables anchor, updates lamp.</summary>
    private void InsertFuse(ItemData item)
    {
        _fuseInserted = true;
        _fuseItemId   = item.ItemId;

        if (_fuseAnchorCollider != null)
            _fuseAnchorCollider.enabled = false;

        SpawnFuseVisual(item);
        PlaySFX(_fuseInsertClip, _fuseInsertVolume);
        UpdateLamp();
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Instantiates the fuse prefab at the anchor transform.
    /// Uses <see cref="_fusePrefab"/> if assigned, otherwise falls back to item.inspectionPrefab.
    /// </summary>
    private void SpawnFuseVisual(ItemData item)
    {
        var prefab = _fusePrefab != null ? _fusePrefab : item?.inspectionPrefab;
        if (prefab == null || _fuseAnchorTransform == null) return;

        if (_fuseInstance != null)
            Destroy(_fuseInstance);

        _fuseInstance = Instantiate(
            prefab,
            _fuseAnchorTransform.position,
            _fuseAnchorTransform.rotation,
            _fuseAnchorTransform
        );
    }

    /// <summary>Finds an ItemData in _acceptedItems by its stable ItemId (used after save/load).</summary>
    private ItemData FindAcceptedItemById(string id)
    {
        if (string.IsNullOrEmpty(id) || _acceptedItems == null) return null;
        foreach (var it in _acceptedItems)
            if (it != null && it.ItemId == id) return it;
        return null;
    }

    // ── Save restore ──────────────────────────────────────────────────────────

    private void InitConnections()
    {
        for (int i = 0; i < TerminalCount; i++) _connections[i] = DisconnectedValue;
    }

    private void ApplyPendingLoad()
    {
        _isSolved     = _pendingSolved;
        _fuseInserted = _pendingFuseInserted;
        _fuseItemId   = _pendingFuseItemId;

        for (int i = 0; i < TerminalCount; i++)
            _connections[i] = _pendingConnections[i];

        for (int i = 0; i < TerminalCount; i++)
        {
            int nIdx = _connections[i];
            if (nIdx == DisconnectedValue) continue;
            if (i >= _coloredTerminals.Length || nIdx >= _neutralTerminals.Length) continue;

            var colored = _coloredTerminals[i];
            var neutral = _neutralTerminals[nIdx];
            if (colored == null || neutral == null) continue;

            var wire = CreateWireObject(i);
            Color color = _puzzleData != null ? _puzzleData.WireColors[i] : Color.white;
            wire.Init(colored.transform, i, color, _wireMaterial, _wireCapPrefab, _wireSettings);
            wire.ConnectEnd(neutral.transform);

            _wires[i] = wire;
            colored.AttachWire(wire);
            neutral.AttachWire(wire);
        }
    }

    private ElectricWire CreateWireObject(int coloredIndex)
    {
        var go = new GameObject($"Wire_{coloredIndex}");
        go.transform.SetParent(transform);
        go.AddComponent<LineRenderer>();
        return go.AddComponent<ElectricWire>();
    }

    /// <summary>Routes SFX through AudioManager singleton — consistent with all other puzzle sounds.</summary>
    private static void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip, volume);
    }
}
