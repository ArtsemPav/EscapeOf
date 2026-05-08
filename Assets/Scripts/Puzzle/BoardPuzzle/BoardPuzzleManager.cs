using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages path validation for the board puzzle.
///
/// Connection model:
///   LOGICAL  — terminal <-> connector (A -> A1, A -> A2, B1 -> B).
///              Defined in BoardPuzzleTrackConnector in the inspector.
///   PHYSICAL — connector <-> connector across different cylinders.
///              Detected at runtime via Physics.OverlapSphere.
///   INTERNAL — connector <-> connector within the same cylinder.
///              Also defined in BoardPuzzleTrackConnector.
///
/// Algorithm:
///   1. From the first terminal, collect all logically connected exit connectors (A1, A2).
///   2. Try each. Trace: PHYSICAL -> INTERNAL -> PHYSICAL -> ...
///      After each PHYSICAL step, check whether the landed connector logically reaches a terminal.
///   3. If the correct next terminal is reached, exit from the OTHER connector of that terminal
///      (the one not used for entry).
///   4. Repeat until the full sequence is satisfied.
/// </summary>
public class BoardPuzzleManager : MonoBehaviour, ISaveable {

    [Header("Events")]
    [Tooltip("Fired once when the full terminal sequence is successfully traced.")]
    public UnityEvent OnPuzzleSolved;

    [Header("Puzzle Grid")]
    [Tooltip("All cylinders in the puzzle.")]
    [SerializeField] private BoardPuzzlePipe[] _cylinders;

    [Header("Path Sequence")]
    [Tooltip("The sequence of terminal GameObjects that must be visited in order.")]
    [SerializeField] private List<GameObject> _targetSequence;

    [Header("Physics Detection")]
    [Tooltip("Radius used by OverlapSphere to find physically adjacent connector points.")]
    [SerializeField] private float _connectionDetectionRadius = 0.15f;

    [Header("Save")]
    [Tooltip("Unique identifier for the save system. Must be unique across the entire game.")]
    [SerializeField] private string _saveId = "board_puzzle";

    [Header("Debug")]
    [Tooltip("If enabled, path tracing details will be printed to the console.")]
    [SerializeField] private bool _showDebugLogs = true;

    [Header("Solved Highlight")]
    [Tooltip("How many seconds the highlight stays visible after the puzzle is solved. Set to 0 to keep it on indefinitely.")]
    [SerializeField, Min(0f)] private float _highlightOffDelay = 5f;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        var rotations = new float[_cylinders.Length * 4];
        for (int i = 0; i < _cylinders.Length; i++)
        {
            if (_cylinders[i] == null) continue;
            Quaternion q = _cylinders[i].transform.localRotation;
            rotations[i * 4 + 0] = q.x;
            rotations[i * 4 + 1] = q.y;
            rotations[i * 4 + 2] = q.z;
            rotations[i * 4 + 3] = q.w;
        }

        return JsonUtility.ToJson(new SaveData
        {
            isSolved  = _isSolved,
            rotations = rotations
        });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _loadedIsSolved   = data.isSolved;
        _loadedRotations  = data.rotations;
    }

    [Serializable]
    private struct SaveData
    {
        public bool    isSolved;
        public float[] rotations;
    }

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool    _isSolved;
    private bool    _loadedIsSolved;
    private float[] _loadedRotations;
    private Coroutine _highlightOffCoroutine;

    // All BoardPuzzleTrackConnectors found in the scene, cached on Start.
    private List<BoardPuzzleTrackConnector> _allTrackConnectors;
    // Cache for connector visuals to update emission.
    private Dictionary<GameObject, BoardPuzzleConnectorVisual> _visualsCache = new();
    // Fast O(1) terminal lookup built from _targetSequence.
    private HashSet<GameObject> _terminalSet = new();
    // Global logical connection cache: point -> all points it logically connects to.
    private Dictionary<GameObject, List<GameObject>> _logicalConnectionCache = new();
    // Reusable buffer for Physics.OverlapSphereNonAlloc to avoid per-frame allocations.
    private readonly Collider[] _overlapBuffer = new Collider[32];

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void Start() {
        _allTrackConnectors = new List<BoardPuzzleTrackConnector>(
            FindObjectsByType<BoardPuzzleTrackConnector>(FindObjectsSortMode.None));

        _terminalSet = new HashSet<GameObject>(_targetSequence.Where(t => t != null));

        BuildLogicalConnectionCache();
        InitVisuals();
        ValidateSetup();

        // Restore cylinder rotations from save before subscribing to OnRotated.
        if (_loadedRotations != null && _loadedRotations.Length == _cylinders.Length * 4)
        {
            for (int i = 0; i < _cylinders.Length; i++)
            {
                if (_cylinders[i] == null) continue;
                _cylinders[i].transform.localRotation = new Quaternion(
                    _loadedRotations[i * 4 + 0],
                    _loadedRotations[i * 4 + 1],
                    _loadedRotations[i * 4 + 2],
                    _loadedRotations[i * 4 + 3]);
            }
        }

        if (_loadedIsSolved)
        {
            _isSolved = true;
            LockAllCylinders();
            TurnOffAllVisuals();
            // Re-fire the event so listeners (doors, lights, etc.) can restore their state.
            OnPuzzleSolved.Invoke();
            return;
        }

        foreach (var pipe in _cylinders) {
            if (pipe != null)
                pipe.OnRotated += CheckSolution;
        }

        TurnOffAllVisuals();
    }

    private void OnDestroy() {
        SaveManager.Instance?.Unregister(this);

        foreach (var pipe in _cylinders) {
            if (pipe != null)
                pipe.OnRotated -= CheckSolution;
        }
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public void CheckSolution() {
        if (_targetSequence == null || _targetSequence.Count < 2) return;

        // Ensure collider world positions reflect the latest Transform changes
        // before any OverlapSphere queries. OnRotated fires immediately after
        // setting transform.localRotation in the coroutine, while physics sync
        // happens on the next FixedUpdate — this bridges that gap.
        Physics.SyncTransforms();

        UpdateVisualPath();

        Log("<color=cyan>[Puzzle] --- Starting Path Validation ---</color>");

        GameObject startTerminal = _targetSequence[0];
        List<GameObject> startConnectors = FindLogicalConnectors(startTerminal);

        if (startConnectors.Count == 0) {
            Log($"<color=red>[Puzzle] No logical connectors for start terminal '{startTerminal.name}'. " +
                      $"Check BoardPuzzleTrackConnector setup.</color>");
            return;
        }

        foreach (GameObject startConnector in startConnectors) {
            Log($"[Puzzle] Trying start direction: {startConnector.name}");
            if (TraceSequence(startConnector, fromTerminal: startTerminal, targetIndex: 1)) {
                Log("<color=cyan>[Puzzle] !!! PUZZLE SOLVED !!!</color>");
                _isSolved = true;
                UpdateVisualPath();
                LockAllCylinders();
                SaveManager.Instance?.Save();
                OnPuzzleSolved.Invoke();
                return;
            }
        }

        Log("<color=red>[Puzzle] Path broken! No valid path found.</color>");
    }

    // -------------------------------------------------------------------------
    // Sequence tracer (terminal level)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Recursively traces from exitConnector trying to reach _targetSequence[targetIndex],
    /// then continues to the next terminal via the opposite connector.
    /// Supports multiple paths to the same terminal.
    /// </summary>
    private bool TraceSequence(GameObject exitConnector, GameObject fromTerminal, int targetIndex) {
        if (targetIndex >= _targetSequence.Count) return true;

        GameObject targetTerminal = _targetSequence[targetIndex];

        // Find ALL possible ways to reach this terminal from the current exit.
        List<GameObject> possibleArrivals = FindAllArrivalConnectors(exitConnector, fromTerminal, targetTerminal);

        if (possibleArrivals.Count == 0) {
            if (_showDebugLogs) Debug.Log($"[Puzzle] Dead end from {exitConnector.name} - cannot reach {targetTerminal.name}");
            return false;
        }

        foreach (GameObject arrivalConnector in possibleArrivals) {
            Log($"<color=green>[Puzzle] Step {targetIndex}: reached {targetTerminal.name} via {arrivalConnector.name}</color>");

            // Last terminal — done.
            if (targetIndex == _targetSequence.Count - 1) return true;

            // Find the exit connector: the other logical connector of this terminal.
            List<GameObject> terminalConnectors = FindLogicalConnectors(targetTerminal);
            GameObject nextExit = terminalConnectors.FirstOrDefault(c => c != arrivalConnector);

            if (nextExit == null) {
                Log($"<color=red>[Puzzle] No exit connector at {targetTerminal.name} (entered via {arrivalConnector.name})</color>");
                continue;
            }

            int nextTargetIndex = targetIndex + 1;

            // loopEntry — the connector we entered the terminal through.
            // loopExit  — the connector we exit through (opposite of entry).
            // Each duplicate requires a loop: exit via loopExit, return via loopEntry.
            // Entry/exit stay fixed across multiple consecutive duplicates.
            GameObject loopEntry = arrivalConnector;
            GameObject loopExit  = nextExit;
            bool loopFailed = false;

            while (nextTargetIndex < _targetSequence.Count && _targetSequence[nextTargetIndex] == targetTerminal)
            {
                Log($"[Puzzle] Duplicate '{targetTerminal.name}' at index {nextTargetIndex}: " +
                                              $"checking loop {loopExit.name} → return via {loopEntry.name}");

                List<GameObject> loopArrivals = FindAllArrivalConnectors(loopExit, targetTerminal, targetTerminal);

                if (!loopArrivals.Contains(loopEntry))
                {
                    Log($"<color=red>[Puzzle] Loop failed: cannot return to {targetTerminal.name} via {loopEntry.name}</color>");
                    loopFailed = true;
                    break;
                }

                Log($"<color=green>[Puzzle] Loop OK: {targetTerminal.name} reached again via {loopEntry.name}</color>");

                if (nextTargetIndex == _targetSequence.Count - 1) return true;
                nextTargetIndex++;
            }

            if (loopFailed) {
                Log($"[Puzzle] Path through {arrivalConnector.name} failed on loop. Trying other arrival points...");
                continue;
            }

            Log($"[Puzzle] Exiting {targetTerminal.name} via {loopExit.name}");
            if (TraceSequence(loopExit, fromTerminal: targetTerminal, targetIndex: nextTargetIndex)) {
                return true;
            }

            Log($"[Puzzle] Path through {arrivalConnector.name} failed further down the line. Trying other arrival points...");
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Cylinder-network tracer (connector level)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Follows PHYSICAL -> INTERNAL -> PHYSICAL -> ... from exitConnector
    /// and collects ALL arrival connectors that logically reach targetTerminal.
    /// </summary>
    private List<GameObject> FindAllArrivalConnectors(GameObject exitConnector, GameObject fromTerminal, GameObject targetTerminal) {
        List<GameObject> arrivalConnectors = new List<GameObject>();
        Stack<(GameObject current, GameObject prev)> stack = new Stack<(GameObject, GameObject)>();
        HashSet<GameObject> visited = new HashSet<GameObject>();

        // Block ALL sequence terminals from the beginning.
        // targetTerminal is only reachable via FindLogicalTerminal on its connectors — not directly.
        // fromTerminal is blocked to prevent immediate backtrack.
        // All other sequence terminals are blocked so the path cannot pass through them
        // out-of-order, even if their connector points lack a logical link to the terminal.
        foreach (GameObject t in _targetSequence)
            visited.Add(t);

        // exitConnector itself must NOT be in visited — it is our starting point.
        visited.Remove(exitConnector);

        stack.Push((exitConnector, fromTerminal));

        Log($"  [Trace] FindAllArrivalConnectors: start={exitConnector.name}, from={fromTerminal?.name}, target={targetTerminal.name}");

        while (stack.Count > 0) {
            var (current, prev) = stack.Pop();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);

            Log($"  [Trace]   Processing {current.name} (prev={prev?.name}) → physical neighbors: [{string.Join(", ", physicalNeighbors.Select(n => n.name))}]");

            foreach (GameObject neighbor in physicalNeighbors) {
                if (visited.Contains(neighbor)) {
                    Log($"  [Trace]     {neighbor.name} already visited, skip");
                    continue;
                }

                // Check logical arrival at any terminal.
                GameObject terminal = FindLogicalTerminal(neighbor);
                if (terminal != null) {
                    Log($"  [Trace]     {neighbor.name} → terminal={terminal.name} (target={targetTerminal.name}) {(terminal == targetTerminal ? "✓ ARRIVAL" : "✗ wrong terminal")}");

                    if (terminal == targetTerminal) {
                        if (!arrivalConnectors.Contains(neighbor))
                            arrivalConnectors.Add(neighbor);
                    }

                    // Treat terminals as blocking for further pathing to other cylinders,
                    // but we still allow the INTERNAL step below to complete the cylinder path.
                    visited.Add(neighbor);
                }

                // INTERNAL step: follow connections within neighbor's cylinder.
                BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();

                if (connector != null) {
                    List<GameObject> internalLinks = new List<GameObject>();
                    foreach (GameObject linked in connector.GetConnectedPoints(neighbor)) {
                        if (IsTerminal(linked)) continue;
                        if (!visited.Contains(linked)) {
                            stack.Push((linked, neighbor));
                            internalLinks.Add(linked);
                        }
                    }
                    Log($"  [Trace]     {neighbor.name} → internal links: [{string.Join(", ", internalLinks.Select(l => l.name))}]");
                } else {
                    Log($"  [Trace]     {neighbor.name} → no BoardPuzzleTrackConnector found in parent");
                }
            }
        }

        Log($"  [Trace] FindAllArrivalConnectors result: [{string.Join(", ", arrivalConnectors.Select(c => c.name))}]");
        return arrivalConnectors;
    }

    // -------------------------------------------------------------------------
    // Logical connection helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls Lock() on every cylinder to block further rotation after the puzzle is solved.
    /// </summary>
    private void LockAllCylinders()
    {
        foreach (BoardPuzzlePipe pipe in _cylinders)
        {
            if (pipe != null) pipe.Lock();
        }
    }

    /// <summary>
    /// Builds a global lookup dictionary: point -> all points it logically connects to,
    /// aggregated across all BoardPuzzleTrackConnectors in the scene.
    /// Called once in Start() so that FindLogicalConnectors and FindLogicalTerminal are O(1).
    /// </summary>
    private void BuildLogicalConnectionCache()
    {
        _logicalConnectionCache = new Dictionary<GameObject, List<GameObject>>();

        foreach (BoardPuzzleTrackConnector tc in _allTrackConnectors)
        {
            foreach (GameObject source in tc.GetAllSourcePoints())
            {
                if (!_logicalConnectionCache.TryGetValue(source, out List<GameObject> list))
                {
                    list = new List<GameObject>();
                    _logicalConnectionCache[source] = list;
                }

                foreach (GameObject linked in tc.GetConnectedPoints(source))
                {
                    if (!list.Contains(linked))
                        list.Add(linked);
                }
            }
        }
    }

    /// <summary>
    /// Returns all connectors logically connected FROM the given point.
    /// Excludes terminals from the result.
    /// Uses the pre-built cache for O(1) lookup.
    /// </summary>
    private List<GameObject> FindLogicalConnectors(GameObject fromPoint)
    {
        if (!_logicalConnectionCache.TryGetValue(fromPoint, out List<GameObject> connected))
            return new List<GameObject>();

        List<GameObject> results = new List<GameObject>(connected.Count);
        foreach (GameObject go in connected)
        {
            if (!IsTerminal(go))
                results.Add(go);
        }
        return results;
    }

    /// <summary>
    /// Checks whether the connector logically leads to a terminal,
    /// using the pre-built cache for O(1) lookup.
    /// </summary>
    private GameObject FindLogicalTerminal(GameObject connector)
    {
        if (!_logicalConnectionCache.TryGetValue(connector, out List<GameObject> connected))
            return null;

        foreach (GameObject linked in connected)
        {
            if (IsTerminal(linked))
                return linked;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Physical connection helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all physically adjacent objects that are valid connector points
    /// (have a BoardPuzzleTrackConnector in their hierarchy),
    /// excluding the given object to prevent backtracking.
    /// Uses a reusable NonAlloc buffer to avoid per-call allocations.
    /// </summary>
    private List<GameObject> FindPhysicalNeighbors(GameObject point, GameObject exclude = null) {
        List<GameObject> results = new List<GameObject>();
        Collider col = point.GetComponent<Collider>();
        if (col == null) return results;

        // Get the cylinder (parent with BoardPuzzleTrackConnector) of the current point.
        BoardPuzzleTrackConnector currentCylinder = point.GetComponentInParent<BoardPuzzleTrackConnector>();

        int count = Physics.OverlapSphereNonAlloc(col.bounds.center, _connectionDetectionRadius, _overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            GameObject go = _overlapBuffer[i].gameObject;
            if (go == point) continue;
            if (go == exclude) continue;
            if (IsTerminal(go)) continue;

            // Accept only connector-point children, not the cylinder GameObject itself.
            BoardPuzzleTrackConnector otherCylinder = go.GetComponentInParent<BoardPuzzleTrackConnector>();

            // CRITICAL: Only allow connection if the other point belongs to a DIFFERENT cylinder.
            // This prevents points on the same cylinder from "short-circuiting" physically.
            if (otherCylinder != null && otherCylinder.gameObject != go && otherCylinder != currentCylinder)
                results.Add(go);
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Path Visualization Helpers
    // -------------------------------------------------------------------------

    private void InitVisuals()
    {
        var allVisuals = FindObjectsByType<BoardPuzzleConnectorVisual>(FindObjectsSortMode.None);
        foreach (var v in allVisuals)
        {
            _visualsCache[v.gameObject] = v;
        }
    }

    private void UpdateVisualPath()
    {
        if (_targetSequence == null || _targetSequence.Count == 0) return;

        HashSet<GameObject> poweredPoints = new HashSet<GameObject>();
        HashSet<GameObject> incorrectTerminals = new HashSet<GameObject>();

        // The first terminal is always the origin.
        int lastCorrectIndex = 0;
        GameObject startTerminal = _targetSequence[0];
        poweredPoints.Add(startTerminal);

        foreach (GameObject exit in FindLogicalConnectors(startTerminal))
        {
            poweredPoints.Add(exit);
        }

        for (int i = 1; i < _targetSequence.Count; i++)
        {
            GameObject prevTerminal = _targetSequence[i - 1];
            GameObject nextTerminal = _targetSequence[i];

            if (!poweredPoints.Contains(prevTerminal)) continue;

            List<GameObject> exits = FindLogicalConnectors(prevTerminal);
            foreach (GameObject exit in exits)
            {
                HashSet<GameObject> segmentPoints = new HashSet<GameObject>();
                HashSet<GameObject> segmentIncorrect = new HashSet<GameObject>();

                bool reached = FloodFillSegment(exit, prevTerminal, nextTerminal, segmentPoints, segmentIncorrect);

                foreach (GameObject p in segmentPoints) poweredPoints.Add(p);
                foreach (GameObject t in segmentIncorrect) incorrectTerminals.Add(t);

                if (reached && i == lastCorrectIndex + 1)
                    lastCorrectIndex = i;
            }
        }

        foreach (GameObject p in poweredPoints)
            incorrectTerminals.Remove(p);

        if (_showDebugLogs)
        {
            var names = string.Join(", ", poweredPoints.Select(o => o != null ? o.name : "null"));
            var incNames = string.Join(", ", incorrectTerminals.Select(o => o != null ? o.name : "null"));
            Log($"  [Visual] poweredPoints ({poweredPoints.Count}): {names}");
            Log($"  [Visual] incorrectTerminals ({incorrectTerminals.Count}): {incNames}");
            Log($"  [Visual] lastCorrectIndex={lastCorrectIndex}");
        }

        // Apply visual state.
        bool anyVisualPowered = false;

        // Grouping: (Renderer, MaterialIndex) -> (hasPower, isIncorrect, isCorrect)
        var rendererStates = new Dictionary<(Renderer, int), (bool hasPower, bool isIncorrect, bool isCorrect)>();

        // Pass 1: Aggregate states per renderer
        foreach (var kvp in _visualsCache)
        {
            BoardPuzzleConnectorVisual visual = kvp.Value;
            if (visual == null || visual.TargetRenderer == null) continue;

            GameObject obj = kvp.Key;
            bool hasPower = poweredPoints.Contains(obj);
            bool isIncorrect = incorrectTerminals.Contains(obj);
            bool isCorrect = true;

            if (visual.IsTerminal)
            {
                int seqIdx = _targetSequence.IndexOf(obj);
                isCorrect = _isSolved ? _targetSequence.Contains(obj) : (seqIdx >= 0 && seqIdx <= lastCorrectIndex);
            }

            var key = (visual.TargetRenderer, visual.MaterialIndex);
            if (!rendererStates.TryGetValue(key, out var state))
            {
                rendererStates[key] = (hasPower, isIncorrect, isCorrect);
            }
            else
            {
                // Consolidate: if ANY point on this renderer is powered or incorrect, it should be lit.
                // For 'isCorrect', we prefer true if any point is correct.
                rendererStates[key] = (
                    state.hasPower || hasPower,
                    state.isIncorrect || isIncorrect,
                    state.isCorrect || isCorrect
                );
            }
        }

        // Pass 2: Apply aggregated states to all visuals
        foreach (var kvp in _visualsCache)
        {
            BoardPuzzleConnectorVisual visual = kvp.Value;
            if (visual == null || visual.TargetRenderer == null) continue;

            var key = (visual.TargetRenderer, visual.MaterialIndex);
            if (rendererStates.TryGetValue(key, out var state))
            {
                if (state.hasPower || state.isIncorrect) anyVisualPowered = true;

                if (visual.IsTerminal)
                {
                    if (state.hasPower) visual.SetPower(true, true, state.isCorrect);
                    else if (state.isIncorrect) visual.SetPower(true, true, false);
                    else visual.SetPower(false);
                }
                else
                {
                    visual.SetPower(state.hasPower, state.hasPower);
                }
            }
        }

        if (anyVisualPowered)
            StartHighlightOffTimer();
        }

    /// <summary>
    /// Flood-fills from <paramref name="exitConnector"/>, respecting the same PHYSICAL→INTERNAL
    /// alternation as the validator. Powers all traversed nodes into <paramref name="segmentPoints"/>.
    /// Returns true only when <paramref name="targetTerminal"/> is reached.
    /// Collects terminals that were physically reached but are not the target into
    /// <paramref name="incorrectTerminals"/> so they can be highlighted in the wrong color.
    /// </summary>
    private bool FloodFillSegment(
        GameObject exitConnector,
        GameObject fromTerminal,
        GameObject targetTerminal,
        HashSet<GameObject> segmentPoints,
        HashSet<GameObject> incorrectTerminals = null)
    {
        bool reached = false;
        Stack<(GameObject current, GameObject prev)> stack = new Stack<(GameObject, GameObject)>();
        HashSet<GameObject> visited = new HashSet<GameObject>();

        // Block all sequence terminals so the segment cannot pass through any of them.
        foreach (GameObject t in _targetSequence)
            visited.Add(t);

        // Allow power to flow out from the current starting terminal.
        visited.Remove(fromTerminal);

        stack.Push((exitConnector, fromTerminal));

        while (stack.Count > 0)
        {
            var (current, prev) = stack.Pop();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            segmentPoints.Add(current);

            List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);
            foreach (GameObject neighbor in physicalNeighbors)
            {
                if (visited.Contains(neighbor)) continue;

                // Check if this connector logically leads to a terminal.
                GameObject terminal = FindLogicalTerminal(neighbor);
                if (terminal != null)
                {
                    visited.Add(neighbor);
                    // Any connector that leads to a terminal should be powered.
                    segmentPoints.Add(neighbor);

                    if (terminal == targetTerminal)
                    {
                        segmentPoints.Add(terminal);
                        reached = true;
                    }
                    else
                    {
                        // Terminal physically reached but wrong order or not in sequence.
                        incorrectTerminals?.Add(terminal);
                    }
                }

                // INTERNAL step: follow connections within the neighbor's cylinder.
                BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();
                if (connector != null)
                {
                    foreach (GameObject linked in connector.GetConnectedPoints(neighbor))
                    {
                        if (!visited.Contains(linked))
                            stack.Push((linked, neighbor));
                    }
                }
            }
        }

        return reached;
    }

    /// <summary>Starts the coroutine that turns off highlight after <see cref="_highlightOffDelay"/> seconds.</summary>
    private void StartHighlightOffTimer()
    {
        if (_highlightOffDelay <= 0f) return;
        if (_highlightOffCoroutine != null)
            StopCoroutine(_highlightOffCoroutine);
        _highlightOffCoroutine = StartCoroutine(TurnOffHighlightAfterDelay());
    }

    private IEnumerator TurnOffHighlightAfterDelay()
    {
        yield return new WaitForSeconds(_highlightOffDelay);
        TurnOffAllVisuals();
        _highlightOffCoroutine = null;
    }

    /// <summary>Sets power to false on every cached connector visual.</summary>
    private void TurnOffAllVisuals()
    {
        foreach (var kvp in _visualsCache)
        {
            if (kvp.Value != null)
                kvp.Value.SetPower(false);
        }
    }

    /// <summary>
    /// Warns in the console if any sequence terminal has no logical connectors defined,
    /// which would break order-enforcement in the path tracer.
    /// </summary>
    private void ValidateSetup()
    {
        if (_targetSequence == null) return;
        foreach (GameObject terminal in _targetSequence)
        {
            if (terminal == null)
            {
                Debug.LogWarning($"[Puzzle] '{name}': _targetSequence contains a null entry. Remove it.", this);
                continue;
            }

            bool hasConnector = _allTrackConnectors.Any(tc => tc.GetConnectedPoints(terminal).Any());
            if (!hasConnector)
                Debug.LogWarning($"[Puzzle] '{name}': Terminal '{terminal.name}' has no logical connectors " +
                                 $"defined in any BoardPuzzleTrackConnector. Order enforcement will break.", this);
        }
    }

    private bool IsTerminal(GameObject go) {
        return _terminalSet.Contains(go) ||
               (_visualsCache.TryGetValue(go, out var visual) && visual.IsTerminal);
    }

    /// <summary>
    /// Writes a message to the console only when _showDebugLogs is enabled.
    /// Excluded from non-editor builds at compile time via Conditional.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string message)
    {
        if (_showDebugLogs) Debug.Log(message);
    }
}
