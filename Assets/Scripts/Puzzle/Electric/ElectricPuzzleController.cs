using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates the electric panel wire-connection puzzle.
/// Follows the same Open/Close/Save pattern as MedallionBoxInteraction.
///
/// Flow:
///   1. Player clicks the panel → Cinemachine camera blends in, UI panel opens, cursor is freed.
///   2. Mouse raycasts against terminal colliders using Camera.main.ScreenPointToRay.
///   3. LMB on a colored terminal → start wire drag; wire end follows the cursor in 3D.
///   4. LMB release on a neutral terminal → connect wire, evaluate solution.
///   5. LMB on an occupied terminal → lift the wire for redirection.
///   6. LMB on the lever → immediately resets all wires if wrong, or completes the puzzle if correct.
///   7. RMB → cancel active drag without closing. ESC / WASD → close panel.
///
/// Setup:
///   • Attach to the root "electric" GameObject (Interactable Layer + BoxCollider).
///   • <see cref="_panelCamera"/> and <see cref="_lampLight"/> are found automatically in children
///     if not assigned in the Inspector.
///   • Assign <see cref="_coloredTerminals"/> and <see cref="_neutralTerminals"/> in order 0..5.
///   • Assign <see cref="_puzzleData"/> (ElectricPuzzleData ScriptableObject).
/// </summary>
[RequireComponent(typeof(Collider))]
public class ElectricPuzzleController : MonoBehaviour, IInteractable, ISaveable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int TerminalCount    = 6;
    private const int DisconnectedValue = -1;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("CinemachineCamera that frames the panel. Leave empty to skip camera switch.")]
    [SerializeField] private CinemachineCamera _panelCamera;

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
    [Tooltip("Hint text shown when the player looks at the panel before entering puzzle mode.")]
    [SerializeField] private string _interactText = "Осмотреть щиток";

    [Tooltip("Duration of Cinemachine blend in/out in seconds.")]
    [SerializeField] private float _blendDuration = 0.75f;

    [Tooltip("Layer mask of terminal colliders used for mouse raycasting while the panel is open.")]
    [SerializeField] private LayerMask _terminalLayer;

    [Tooltip("Prefab used as visual cap at both wire ends (assign pCylinder21 prefab here).")]
    [SerializeField] private GameObject _wireCapPrefab;

    [Tooltip("Material applied to wire LineRenderers. Leave empty for a default unlit material.")]
    [SerializeField] private Material _wireMaterial;

    [Header("Wire Settings")]
    [Tooltip("Simulation and rendering settings shared by all wires in this puzzle.")]
    [SerializeField] private ElectricWireSettings _wireSettings = new ElectricWireSettings();

    [Header("Events")]
    [SerializeField] private UnityEvent _onPuzzleSolved;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _isSolved;
    private bool _wiresCorrect;

    private ElectricWire     _activeWire;
    private ElectricTerminal _activeColoredTerminal;

    private readonly int[] _connections = new int[TerminalCount];
    private readonly ElectricWire[] _wires = new ElectricWire[TerminalCount];

    private CinemachineBrain _brain;
    private float            _originalBlendTime;
    private Collider         _ownCollider;

    // Pending save state applied in Start()
    private bool _pendingLoad;
    private int[] _pendingConnections;
    private bool  _pendingSolved;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "electric_puzzle";

    [Serializable]
    private struct SaveData { public bool isSolved; public bool wiresCorrect; public int[] connections; }

    /// <summary>Serialises current connections and solved flags.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new SaveData
    {
        isSolved     = _isSolved,
        wiresCorrect = _wiresCorrect,
        connections  = (int[])_connections.Clone(),
    });

    /// <summary>Stores loaded data — applied in Start() after all terminals are ready.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _pendingSolved      = data.isSolved;
        _pendingConnections = data.connections ?? new int[TerminalCount];
        Array.Resize(ref _pendingConnections, TerminalCount);
        _pendingLoad = true;
    }

    // ── IInteractable — outer panel collider ──────────────────────────────────

    public bool IsPickable()         => false;
    public bool UseLMBClick          => true;
    public string GetInteractText()  => _interactText;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
    public bool CanInteract()        => !_isOpen && !_isSolved;
    public void Interact()           => Open();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _brain = Camera.main?.GetComponent<CinemachineBrain>();
        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;

        if (_panelCamera != null)
            _panelCamera.gameObject.SetActive(false);

        _ownCollider = GetComponent<Collider>();

        // Auto-find references if not assigned in the inspector.
        if (_lever == null)
            _lever = GetComponentInChildren<ElectricLever>(includeInactive: true);
        if (_lampLight == null)
            _lampLight = GetComponentInChildren<Light>(includeInactive: true);

        // Ensure wrong-pull particles are hidden at startup regardless of prefab state.
        if (_wrongPullParticles != null)
            _wrongPullParticles.gameObject.SetActive(false);

        if (_lever != null)
            _lever.OnPulled += HandleLeverPulled;

        InitConnections();
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_pendingLoad)
        {
            _pendingLoad = false;
            ApplyPendingLoad();
            // Joint settling after all wires are loaded — resolves any remaining inter-wire overlaps
            ElectricWire.JointPresettle();
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

        var kb = Keyboard.current;
        if (kb != null && (kb.escapeKey.wasPressedThisFrame
            || kb.wKey.isPressed || kb.sKey.isPressed
            || kb.aKey.isPressed || kb.dKey.isPressed))
        {
            CancelActiveDrag();
            Close();
            return;
        }

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

    // ── Mouse interaction ─────────────────────────────────────────────────────

    /// <summary>
    /// Called on LMB press. Starts a new wire drag, picks up an existing wire,
    /// or interacts with the lever when wires are correctly connected.
    /// </summary>
    private void HandleMousePress(Vector2 screenPos)
    {
        if (_activeWire != null) return; // already dragging

        // Check for lever interaction (lever is always interactable regardless of wire state).
        if (!_isSolved && TryInteractLever(screenPos))
            return;

        var terminal = RaycastTerminal(screenPos);
        if (terminal == null) return;

        if (terminal.Type == ElectricTerminal.TerminalType.Colored)
        {
            if (terminal.IsFree)
                StartDrag(terminal);
            else
                PickUpWire(terminal); // lift existing wire from colored terminal
        }
        else // Neutral
        {
            if (!terminal.IsFree)
                PickUpWireFromNeutral(terminal); // lift existing wire from neutral terminal
        }
    }

    /// <summary>
    /// Raycasts for the lever on the same layer mask as terminals and calls Interact if hit.
    /// If wires are incorrect, resets all connections immediately before the animation starts.
    /// Returns true if the lever was successfully clicked.
    /// </summary>
    private bool TryInteractLever(Vector2 screenPos)
    {
        if (_lever == null || !_lever.CanInteract()) return false;
        if (Camera.main == null) return false;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 50f, _terminalLayer, QueryTriggerInteraction.Collide))
            return false;

        var lever = hit.collider.GetComponent<ElectricLever>()
                 ?? hit.collider.GetComponentInParent<ElectricLever>();
        if (lever == null) return false;

        // Wrong pull: scatter particles and wipe wires instantly on press,
        // without waiting for the lever animation to finish.
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
            // Free up the neutral slot if another wire already occupies it
            if (!terminal.IsFree)
                DisconnectWireAtNeutral(terminal);

            ConnectActiveWire(terminal);
        }
        else
        {
            // Released outside any neutral terminal → discard wire
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

        // Project onto an infinite plane at the terminal's depth, facing the camera.
        // This is more reliable than a physics raycast — the wire end always sticks to the cursor.
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

        _wires[colored.Index]   = wire;
        _activeWire             = wire;
        _activeColoredTerminal  = colored;
        colored.AttachWire(wire);
    }

    private void ConnectActiveWire(ElectricTerminal neutral)
    {
        _activeWire.ConnectEnd(neutral.transform);
        neutral.AttachWire(_activeWire);
        _connections[_activeColoredTerminal.Index] = neutral.Index;

        _activeWire            = null;
        _activeColoredTerminal = null;

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

        EvaluateWires();
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
            break;
        }

        EvaluateWires();
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
        if (correct == _wiresCorrect) return; // no state change

        _wiresCorrect = correct;
        SetLampColor(correct ? _lampSolvedColor : _lampDefaultColor);

        if (correct)
            SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Called by <see cref="ElectricLever.OnPulled"/> when the lever animation completes.
    /// Correct wires → puzzle fully solved and panel closes.
    /// Wrong wires → wires were already cleared on press; only return the lever here.
    /// </summary>
    private void HandleLeverPulled()
    {
        if (_wiresCorrect)
        {
            _isSolved = true;
            if (_solvedObject != null) _solvedObject.SetActive(true);
            _onPuzzleSolved?.Invoke();
            SaveManager.Instance?.Save();
            Close();
        }
        else
        {
            // Wires were already cleared in TryInteractLever — just return the lever.
            _lever?.Reset();
        }
    }

    /// <summary>
    /// Activates the wrong-pull particle system, plays it once, then deactivates it
    /// automatically when playback finishes so it stays hidden the rest of the time.
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

        foreach (var terminal in _coloredTerminals)
            terminal?.DetachWire();
        foreach (var terminal in _neutralTerminals)
            terminal?.DetachWire();

        _wiresCorrect = false;
        SetLampColor(_lampDefaultColor);
    }

    /// <summary>
    /// Restores lamp color and lever state from saved data without triggering side-effects.
    /// Called in Start() after save data has been applied.
    /// </summary>
    private void RefreshVisuals()
    {
        if (_isSolved)
        {
            SetLampColor(_lampSolvedColor);
            if (_solvedObject != null) _solvedObject.SetActive(true);
            _lever?.SetPulledQuiet();
            return;
        }

        _wiresCorrect = CheckSolution();
        SetLampColor(_wiresCorrect ? _lampSolvedColor : _lampDefaultColor);
    }

    /// <summary>Sets the indicator lamp color directly on the Light component.</summary>
    private void SetLampColor(Color color)
    {
        if (_lampLight == null) return;
        _lampLight.color = color;
    }

    // ── Open / Close (same pattern as MedallionBoxInteraction) ────────────────

    private void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        if (_ownCollider != null) _ownCollider.enabled = false;

        SetBlendDuration(_blendDuration);
        if (_panelCamera != null) _panelCamera.gameObject.SetActive(true);

        // If a Canvas panel is assigned, use the standard OpenPanel flow.
        // Otherwise, push a modal state manually so cursor is freed and player input is blocked.
        if (_panel != null)
        {
            UIManager.Instance?.OpenPanel(_panel);
        }
        else
        {
            UIManager.Instance?.PushModalState();
            GameManager.Instance?.UpdateCursorState();
        }
    }

    private void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_panel != null) _panel.SetActive(false);

        SetBlendDuration(_blendDuration);
        if (_panelCamera != null) _panelCamera.gameObject.SetActive(false);

        StartCoroutine(RestoreAfterBlend());
    }

    private IEnumerator RestoreAfterBlend()
    {
        yield return null;
        while (_brain != null && _brain.IsBlending) yield return null;

        SetBlendDuration(_originalBlendTime);

        if (_panel != null)
            UIManager.Instance?.ClosePanel(_panel);
        else
            UIManager.Instance?.PopModalState();

        if (_ownCollider != null) _ownCollider.enabled = true;
    }

    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend = _brain.DefaultBlend;
        blend.Time = duration;
        _brain.DefaultBlend = blend;
    }

    // ── Save restore ──────────────────────────────────────────────────────────

    private void InitConnections()
    {
        for (int i = 0; i < TerminalCount; i++) _connections[i] = DisconnectedValue;
    }

    private void ApplyPendingLoad()
    {
        _isSolved = _pendingSolved;

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
}
