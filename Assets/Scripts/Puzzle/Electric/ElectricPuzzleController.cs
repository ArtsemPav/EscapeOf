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
///   • <see cref="_lampRenderer"/> on the lamp mesh (pSphere25) is tinted via material properties.
///   • <see cref="_fuseMesh"/> is the in-scene SafeGuard mesh at the anchor — shown as ghost during drag, animated on insertion.
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
    [Tooltip("Renderer on the lamp mesh (pSphere25) whose material is tinted red→green based on puzzle state.")]
    [SerializeField] private Renderer _lampRenderer;

    [Tooltip("Base color of the lamp material in the unsolved state.")]
    [SerializeField] private Color _lampRedColor = new Color(0.52f, 0f, 0.086f, 0.675f);

    [Tooltip("Emission color of the lamp material in the unsolved state.")]
    [SerializeField] private Color _lampRedEmission = new Color(3.59f, 0f, 0.17f, 1f);

    [Tooltip("Base color of the lamp material when the puzzle is solved (lever pulled with correct wires).")]
    [SerializeField] private Color _lampGreenColor = new Color(0f, 0.52f, 0.086f, 0.675f);

    [Tooltip("Emission color of the lamp material when the puzzle is solved.")]
    [SerializeField] private Color _lampGreenEmission = new Color(0f, 3.59f, 0.17f, 1f);

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

    [Tooltip("Transform where the fuse is placed when inserted. Usually Safeguardanchor.")]
    [SerializeField] private Transform _fuseAnchorTransform;

    [Tooltip("Mesh GameObject placed at the fuse anchor (e.g. SafeGuard (3)). " +
             "Shown as a ghost preview during drag and animated into place on insertion.")]
    [SerializeField] private GameObject _fuseMesh;

    [Tooltip("Local position offset from the anchor where the insertion animation starts.")]
    [SerializeField] private Vector3 _fuseInsertStartOffset = new Vector3(0f, 0.15f, -0.12f);

    [Tooltip("Duration of the fuse insertion animation in seconds.")]
    [SerializeField] private float _fuseInsertDuration = 0.5f;

    [Tooltip("Alpha (0–1) of the fuse mesh when shown as a ghost preview during drag.")]
    [SerializeField, Range(0f, 1f)] private float _fuseGhostAlpha = 0.4f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _isSolved;
    private bool _wiresCorrect;
    private bool _fuseInserted;
    private string _fuseItemId;
    private Material _lampMaterial;
    private Coroutine _fuseInsertRoutine;
    private Renderer[] _fuseRenderers;
    private bool _fuseGhostActive;

    // Cached original material state for ghost/restore
    private struct FuseMaterialState
    {
        public Material Material;
        public float OriginalAlpha;
        public float OriginalSurface;
        public float OriginalBlend;
        public int OriginalSrcBlend;
        public int OriginalDstBlend;
        public int OriginalZWrite;
    }
    private FuseMaterialState[] _fuseMaterialStates;

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

        if (_lever == null)
            _lever = GetComponentInChildren<ElectricLever>(includeInactive: true);

        if (_lampRenderer != null)
        {
            _lampMaterial = _lampRenderer.material; // auto-instantiates a unique clone
            _lampMaterial.EnableKeyword("_EMISSION");
        }

        if (_fuseMesh != null && !_fuseInserted)
        {
            _fuseMesh.SetActive(false);
            _fuseRenderers = _fuseMesh.GetComponentsInChildren<Renderer>(true);
            CacheFuseMaterialStates();
        }

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
            ShowFuseMesh();
            if (_fuseAnchorCollider != null) _fuseAnchorCollider.enabled = false;
        }

        RefreshVisuals();
    }

    private void OnDestroy()
    {
        if (_lever != null)
            _lever.OnPulled -= HandleLeverPulled;

        if (_fuseInsertRoutine != null)
            StopCoroutine(_fuseInsertRoutine);

        if (_lampMaterial != null)
            Destroy(_lampMaterial);

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (!_isOpen) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        // Ghost preview of the fuse at the anchor while dragging from inventory
        UpdateFuseGhost(mouse.position.ReadValue());

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

    /// <summary>
    /// Shows or hides the fuse mesh as a semi-transparent ghost preview at the anchor position
    /// while the player drags an accepted fuse item from the PuzzleInventoryBar over the anchor.
    /// </summary>
    private void UpdateFuseGhost(Vector2 screenPos)
    {
        if (_fuseMesh == null || _fuseInserted || _fuseInsertRoutine != null)
        {
            if (_fuseGhostActive)
                SetFuseGhost(false);
            return;
        }

        bool isDraggingFuse = PuzzleInventoryBar.IsDragging
                              && PuzzleInventoryBar.DraggedItem != null
                              && _acceptedItems != null
                              && Array.IndexOf(_acceptedItems, PuzzleInventoryBar.DraggedItem) >= 0;

        if (!isDraggingFuse)
        {
            if (_fuseGhostActive)
                SetFuseGhost(false);
            return;
        }

        // Raycast to check if cursor is over the anchor collider
        bool overAnchor = false;
        if (Camera.main != null && _fuseAnchorCollider != null)
        {
            var ray = Camera.main.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, _terminalLayer, QueryTriggerInteraction.Collide))
                overAnchor = hit.collider == _fuseAnchorCollider;
        }

        if (overAnchor)
        {
            _fuseMesh.transform.SetPositionAndRotation(
                _fuseAnchorTransform.position, _fuseAnchorTransform.rotation);
            SetFuseGhost(true);
        }
        else
        {
            SetFuseGhost(false);
        }
    }

    /// <summary>
    /// Caches the original material state of all fuse mesh renderers
    /// so it can be perfectly restored after the ghost preview.
    /// </summary>
    private void CacheFuseMaterialStates()
    {
        if (_fuseRenderers == null) return;

        var states = new System.Collections.Generic.List<FuseMaterialState>();

        foreach (var rend in _fuseRenderers)
        {
            foreach (var mat in rend.materials)
            {
                if (mat == null) continue;

                states.Add(new FuseMaterialState
                {
                    Material         = mat,
                    OriginalAlpha    = mat.HasProperty("_BaseColor")     ? mat.GetColor("_BaseColor").a : 1f,
                    OriginalSurface  = mat.HasProperty("_Surface")       ? mat.GetFloat("_Surface")      : 0f,
                    OriginalBlend    = mat.HasProperty("_Blend")         ? mat.GetFloat("_Blend")        : 0f,
                    OriginalSrcBlend = mat.HasProperty("_SrcBlend")      ? mat.GetInt("_SrcBlend")       : (int)UnityEngine.Rendering.BlendMode.One,
                    OriginalDstBlend = mat.HasProperty("_DstBlend")      ? mat.GetInt("_DstBlend")       : (int)UnityEngine.Rendering.BlendMode.Zero,
                    OriginalZWrite   = mat.HasProperty("_ZWrite")        ? mat.GetInt("_ZWrite")         : 1,
                });
            }
        }

        _fuseMaterialStates = states.ToArray();
    }

    /// <summary>Toggles the fuse mesh ghost state — semi-transparent when active, hidden when inactive.</summary>
    private void SetFuseGhost(bool active)
    {
        if (_fuseMesh == null || _fuseGhostActive == active) return;

        _fuseGhostActive = active;
        _fuseMesh.SetActive(active);

        if (_fuseRenderers == null) return;

        if (active)
        {
            foreach (var rend in _fuseRenderers)
                rend.enabled = true;

            if (_fuseMaterialStates != null)
            {
                foreach (var s in _fuseMaterialStates)
                {
                    s.Material.SetFloat("_Surface", 1);
                    s.Material.SetFloat("_Blend", 0);
                    s.Material.SetOverrideTag("RenderType", "Transparent");
                    s.Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    s.Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    s.Material.SetInt("_ZWrite", 0);
                    Color c = s.Material.GetColor("_BaseColor");
                    c.a = Mathf.Min(_fuseGhostAlpha, s.OriginalAlpha);
                    s.Material.SetColor("_BaseColor", c);
                }
            }
        }
    }

    /// <summary>Restores fuse mesh renderers to their original material state after ghost preview.</summary>
    private void RestoreFuseMaterials()
    {
        if (_fuseRenderers == null) return;

        foreach (var rend in _fuseRenderers)
            rend.enabled = true;

        if (_fuseMaterialStates == null) return;

        foreach (var s in _fuseMaterialStates)
        {
            s.Material.SetFloat("_Surface", s.OriginalSurface);
            s.Material.SetFloat("_Blend", s.OriginalBlend);
            s.Material.SetOverrideTag("RenderType", s.OriginalSurface < 0.5f ? "Opaque" : "Transparent");
            s.Material.SetInt("_SrcBlend", s.OriginalSrcBlend);
            s.Material.SetInt("_DstBlend", s.OriginalDstBlend);
            s.Material.SetInt("_ZWrite", s.OriginalZWrite);
            Color c = s.Material.GetColor("_BaseColor");
            c.a = s.OriginalAlpha;
            s.Material.SetColor("_BaseColor", c);
        }
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
            UpdateLamp();
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

    /// <summary>Updates the lamp material color and emission based on the solved state.</summary>
    private void UpdateLamp()
    {
        if (_lampMaterial == null) return;

        bool solved = _isSolved;
        _lampMaterial.SetColor("_BaseColor",     solved ? _lampGreenColor     : _lampRedColor);
        _lampMaterial.SetColor("_EmissionColor", solved ? _lampGreenEmission : _lampRedEmission);
    }

    // ── IPuzzleDropHandler ─────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a fuse dragged from the PuzzleInventoryBar.
    /// Raycasts against the Safeguardanchor collider — drop is valid only when
    /// the cursor lands on the anchor. On success, starts the insertion animation.
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

        // End ghost state — restore full-opacity materials for the animation
        SetFuseGhost(false);
        _fuseGhostActive = false;
        RestoreFuseMaterials();

        _fuseInsertRoutine = StartCoroutine(AnimateFuseInsertion(item));
        return true;
    }

    /// <summary>
    /// Animates the fuse mesh from an offset position to the anchor position,
    /// plays the insertion sound, then finalizes the fuse state.
    /// </summary>
    private IEnumerator AnimateFuseInsertion(ItemData item)
    {
        if (_fuseMesh == null || _fuseAnchorTransform == null)
        {
            FinalizeFuseInsertion(item);
            _fuseInsertRoutine = null;
            yield break;
        }

        Vector3 startPos = _fuseAnchorTransform.TransformPoint(_fuseInsertStartOffset);
        Vector3 endPos   = _fuseAnchorTransform.position;
        Quaternion endRot = _fuseAnchorTransform.rotation;

        _fuseMesh.transform.SetPositionAndRotation(startPos, endRot);
        _fuseMesh.SetActive(true);

        PlaySFX(_fuseInsertClip, _fuseInsertVolume);

        float elapsed = 0f;
        while (elapsed < _fuseInsertDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _fuseInsertDuration;
            // Ease-out for a smooth landing
            t = 1f - (1f - t) * (1f - t);
            _fuseMesh.transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        _fuseMesh.transform.SetPositionAndRotation(endPos, endRot);

        FinalizeFuseInsertion(item);
        _fuseInsertRoutine = null;
    }

    /// <summary>Sets fuse state, disables anchor collider, updates lamp, saves.</summary>
    private void FinalizeFuseInsertion(ItemData item)
    {
        _fuseInserted = true;
        _fuseItemId   = item.ItemId;

        if (_fuseAnchorCollider != null)
            _fuseAnchorCollider.enabled = false;

        UpdateLamp();
        SaveManager.Instance?.Save();
    }

    /// <summary>Shows the fuse mesh at the anchor position without animation (used on save/load).</summary>
    private void ShowFuseMesh()
    {
        if (_fuseMesh == null || _fuseAnchorTransform == null) return;

        _fuseMesh.transform.SetPositionAndRotation(
            _fuseAnchorTransform.position, _fuseAnchorTransform.rotation);
        _fuseGhostActive = false;
        RestoreFuseMaterials();
        _fuseMesh.SetActive(true);
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
