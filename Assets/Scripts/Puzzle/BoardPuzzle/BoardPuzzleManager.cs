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

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void Start() {
        _allTrackConnectors = new List<BoardPuzzleTrackConnector>(
            FindObjectsByType<BoardPuzzleTrackConnector>(FindObjectsSortMode.None));

        InitVisuals();

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

        UpdateVisualPath();

        if (_showDebugLogs) Debug.Log("<color=cyan>[Puzzle] --- Starting Path Validation ---</color>");

        GameObject startTerminal = _targetSequence[0];
        List<GameObject> startConnectors = FindLogicalConnectors(startTerminal);

        if (startConnectors.Count == 0) {
            if (_showDebugLogs) Debug.Log($"<color=red>[Puzzle] No logical connectors for start terminal '{startTerminal.name}'. " +
                      $"Check BoardPuzzleTrackConnector setup.</color>");
            return;
        }

        foreach (GameObject startConnector in startConnectors) {
            if (_showDebugLogs) Debug.Log($"[Puzzle] Trying start direction: {startConnector.name}");
            if (TraceSequence(startConnector, fromTerminal: startTerminal, targetIndex: 1)) {
                if (_showDebugLogs) Debug.Log("<color=cyan>[Puzzle] !!! PUZZLE SOLVED !!!</color>");
                _isSolved = true;
                UpdateVisualPath();
                LockAllCylinders();
                SaveManager.Instance?.Save();
                OnPuzzleSolved.Invoke();
                return;
            }
        }

        if (_showDebugLogs) Debug.Log("<color=red>[Puzzle] Path broken! No valid path found.</color>");
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
            if (_showDebugLogs) Debug.Log($"<color=green>[Puzzle] Step {targetIndex}: reached {targetTerminal.name} via {arrivalConnector.name}</color>");

            // Last terminal — done.
            if (targetIndex == _targetSequence.Count - 1) return true;

            // Find the next target in the sequence. 
            // If the next target is the SAME as the current one (duplicate in sequence),
            // we skip the tracing step and treat the current arrival as the entry for the next step.
            int nextTargetIndex = targetIndex + 1;
            while (nextTargetIndex < _targetSequence.Count && _targetSequence[nextTargetIndex] == targetTerminal)
            {
                if (_showDebugLogs) Debug.Log($"[Puzzle] Duplicate terminal '{targetTerminal.name}' detected at index {nextTargetIndex}, skipping trace.");
                if (nextTargetIndex == _targetSequence.Count - 1) return true;
                nextTargetIndex++;
            }

            // Find the exit connector: the other logical connector of this terminal.
            List<GameObject> terminalConnectors = FindLogicalConnectors(targetTerminal);
            GameObject nextExit = terminalConnectors.FirstOrDefault(c => c != arrivalConnector);

            if (nextExit == null) {
                if (_showDebugLogs) Debug.Log($"<color=red>[Puzzle] No exit connector at {targetTerminal.name} (entered via {arrivalConnector.name})</color>");
                continue; 
            }

            if (_showDebugLogs) Debug.Log($"[Puzzle] Exiting {targetTerminal.name} via {nextExit.name}");
            if (TraceSequence(nextExit, fromTerminal: targetTerminal, targetIndex: nextTargetIndex)) {
                return true;
            }

            if (_showDebugLogs) Debug.Log($"[Puzzle] Path through {arrivalConnector.name} failed further down the line. Trying other arrival points...");
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

        if (fromTerminal != null) visited.Add(fromTerminal);

        stack.Push((exitConnector, fromTerminal));

        while (stack.Count > 0) {
            var (current, prev) = stack.Pop();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);

            foreach (GameObject neighbor in physicalNeighbors) {
                if (visited.Contains(neighbor)) continue;

                // Check logical arrival at any terminal.
                GameObject terminal = FindLogicalTerminal(neighbor);
                if (terminal != null) {
                    if (terminal == targetTerminal) {
                        if (!arrivalConnectors.Contains(neighbor))
                            arrivalConnectors.Add(neighbor);
                    }

                    // Treat terminals as blocking for further pathing, even if it's the target.
                    visited.Add(neighbor);
                    continue;
                }

                // INTERNAL step: follow connections within neighbor's cylinder.
                BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();

                if (connector != null) {
                    foreach (GameObject linked in connector.GetConnectedPoints(neighbor)) {
                        if (IsTerminal(linked)) continue;
                        if (!visited.Contains(linked)) {
                            stack.Push((linked, neighbor));
                        }
                    }
                }
            }
        }

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
    /// Returns all connectors logically connected FROM the given point
    /// across all BoardPuzzleTrackConnectors in the scene.
    /// Excludes terminals from the result.
    /// </summary>
    private List<GameObject> FindLogicalConnectors(GameObject fromPoint) {
        List<GameObject> results = new List<GameObject>();
        foreach (BoardPuzzleTrackConnector tc in _allTrackConnectors) {
            foreach (GameObject connected in tc.GetConnectedPoints(fromPoint)) {
                if (!IsTerminal(connected) && !results.Contains(connected))
                    results.Add(connected);
            }
        }
        return results;
    }

    /// <summary>
    /// Checks whether the SPECIFIC BoardPuzzleTrackConnector that owns this connector
    /// defines a logical connection TO a terminal.
    /// </summary>
    private GameObject FindLogicalTerminal(GameObject connector) {
        BoardPuzzleTrackConnector tc = connector.GetComponentInParent<BoardPuzzleTrackConnector>();
        if (tc == null) return null;

        foreach (GameObject linked in tc.GetConnectedPoints(connector)) {
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
    /// </summary>
    private List<GameObject> FindPhysicalNeighbors(GameObject point, GameObject exclude = null) {
        List<GameObject> results = new List<GameObject>();
        Collider col = point.GetComponent<Collider>();
        if (col == null) return results;

        // Get the cylinder (parent with BoardPuzzleTrackConnector) of the current point
        BoardPuzzleTrackConnector currentCylinder = point.GetComponentInParent<BoardPuzzleTrackConnector>();

        Collider[] overlaps = Physics.OverlapSphere(col.bounds.center, _connectionDetectionRadius);
        foreach (Collider other in overlaps) {
            GameObject go = other.gameObject;
            if (go == point) continue;
            if (go == exclude) continue;
            if (IsTerminal(go)) continue;

            // Accept only connector-point children, not the cylinder GameObject itself.
            BoardPuzzleTrackConnector otherCylinder = go.GetComponentInParent<BoardPuzzleTrackConnector>();
            
            // CRITICAL FIX: Only allow connection if the other point belongs to a DIFFERENT cylinder.
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

        {
            GameObject startTerminal = _targetSequence[0];
            
            // Start a flood fill from the first terminal to find ALL reachable points
            Stack<(GameObject current, GameObject prev)> stack = new Stack<(GameObject, GameObject)>();
            stack.Push((startTerminal, null));
            poweredPoints.Add(startTerminal);

            while (stack.Count > 0)
            {
                var (current, prev) = stack.Pop();

                // 1. Get physical neighbors (crossing between cylinders)
                List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);
                foreach (GameObject neighbor in physicalNeighbors)
                {
                    if (poweredPoints.Contains(neighbor)) continue;
                    poweredPoints.Add(neighbor);

                    // If we hit a terminal via physical connection, power it.
                    // Only continue through it if it belongs to the target sequence.
                    // Non-sequence terminals block the beam (show as red, do not propagate further).
                    GameObject terminal = FindLogicalTerminal(neighbor);
                    if (terminal != null)
                    {
                        if (!poweredPoints.Contains(terminal))
                        {
                            poweredPoints.Add(terminal);
                            if (_targetSequence.Contains(terminal))
                                stack.Push((terminal, neighbor));
                        }
                        continue;
                    }

                    // 2. Get internal logical connections (inside the cylinder)
                    BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();
                    if (connector != null)
                    {
                        foreach (GameObject linked in connector.GetConnectedPoints(neighbor))
                        {
                            if (!poweredPoints.Contains(linked))
                            {
                                poweredPoints.Add(linked);
                                stack.Push((linked, neighbor));
                            }
                        }
                    }
                }

                // 3. Special case for the start terminal or intermediate sequence terminals:
                // they have logical exits defined in BoardPuzzleTrackConnector
                List<GameObject> logicalExits = FindLogicalConnectors(current);
                foreach (GameObject exit in logicalExits)
                {
                    if (!poweredPoints.Contains(exit))
                    {
                        poweredPoints.Add(exit);
                        stack.Push((exit, current));
                    }
                }
            }
        }

        // Find frontier: how many consecutive terminals from start are powered in order.
        // _targetSequence[0] is the start terminal and is always in poweredPoints,
        // so nextExpectedIndex is always >= 1.
        int nextExpectedIndex = 0;
        while (nextExpectedIndex < _targetSequence.Count && poweredPoints.Contains(_targetSequence[nextExpectedIndex]))
            nextExpectedIndex++;

        // Apply visual state
        bool anyVisualPowered = false;
        foreach (var kvp in _visualsCache)
        {
            if (kvp.Value == null) continue;

            GameObject obj = kvp.Key;
            bool hasPower = poweredPoints.Contains(obj);
            if (hasPower) anyVisualPowered = true;

            if (kvp.Value.IsTerminal)
            {
                // Correct only if this terminal is the frontier —
                // the last terminal reached correctly in the sequence chain.
                // Everything else (non-sequence, out-of-order, already passed) → incorrect.
                bool isCorrect = nextExpectedIndex > 0 && obj == _targetSequence[nextExpectedIndex - 1];
                kvp.Value.SetPower(hasPower, hasPower, isCorrect);
            }
            else
            {
                // Regular pipe connector.
                kvp.Value.SetPower(hasPower, hasPower);
            }
        }

        if (anyVisualPowered)
            StartHighlightOffTimer();
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
    /// Checks if any terminal is reachable from the current powered path, 
    /// even if it's not the correct one in sequence, to mark it as "incorrect" (red).
    /// </summary>
    private void CheckForIncorrectTerminalVisual(GameObject currentStart, HashSet<GameObject> poweredPoints)
    {
        List<GameObject> exits = FindLogicalConnectors(currentStart);
        foreach (var exit in exits)
        {
            Stack<(GameObject current, GameObject prev)> stack = new Stack<(GameObject, GameObject)>();
            HashSet<GameObject> localVisited = new HashSet<GameObject>();
            stack.Push((exit, currentStart));

            while (stack.Count > 0)
            {
                var (current, prev) = stack.Pop();
                if (localVisited.Contains(current)) continue;
                localVisited.Add(current);

                List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);
                foreach (GameObject neighbor in physicalNeighbors)
                {
                    if (localVisited.Contains(neighbor)) continue;

                    GameObject terminal = FindLogicalTerminal(neighbor);
                    if (terminal != null)
                    {
                        // Found a terminal that isn't the correct one in sequence
                        if (_visualsCache.TryGetValue(terminal, out var visual))
                        {
                            visual.SetPower(true, true, isCorrect: false);
                        }
                        return;
                    }

                    BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();
                    if (connector != null)
                    {
                        foreach (GameObject linked in connector.GetConnectedPoints(neighbor))
                        {
                            stack.Push((linked, neighbor));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A version of TraceToTerminal that powers everything along the way.
    /// Returns true only if targetTerminal is reached.
    /// </summary>
    private bool TraceToTerminalForVisual(GameObject exitConnector, GameObject fromTerminal, 
                                          GameObject targetTerminal, HashSet<GameObject> poweredPoints,
                                          out GameObject arrivalConnector)
    {
        arrivalConnector = null;
        Stack<(GameObject current, GameObject prev)> stack = new Stack<(GameObject, GameObject)>();
        HashSet<GameObject> localVisited = new HashSet<GameObject>();
        
        stack.Push((exitConnector, fromTerminal));

        while (stack.Count > 0)
        {
            var (current, prev) = stack.Pop();
            if (localVisited.Contains(current)) continue;
            localVisited.Add(current);

            // Power everything that is NOT a terminal, OR is one of the allowed terminals in the current segment
            bool isTerminal = IsTerminal(current);
            if (!isTerminal || current == targetTerminal || current == fromTerminal)
            {
                poweredPoints.Add(current);
            }

            List<GameObject> physicalNeighbors = FindPhysicalNeighbors(current, exclude: prev);
            foreach (GameObject neighbor in physicalNeighbors)
            {
                if (localVisited.Contains(neighbor)) continue;

                GameObject terminal = FindLogicalTerminal(neighbor);
                if (terminal != null)
                {
                    // If we reached the target, power it and stop this segment
                    if (terminal == targetTerminal)
                    {
                        poweredPoints.Add(neighbor);
                        poweredPoints.Add(terminal);
                        arrivalConnector = neighbor;
                        return true;
                    }
                    // If it's some other terminal, we don't power it and don't go through it
                    continue;
                }

                // Internal step: Follow internal cylinder connections
                BoardPuzzleTrackConnector connector = neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();
                if (connector != null)
                {
                    // Check if this path leads anywhere valid first
                    bool leadsSomewhere = false;
                    foreach (GameObject linked in connector.GetConnectedPoints(neighbor))
                    {
                        if (!localVisited.Contains(linked))
                        {
                            stack.Push((linked, neighbor));
                            leadsSomewhere = true;
                        }
                    }

                    // Only power the entry point if it's a valid path that doesn't just hit a wrong terminal
                    if (leadsSomewhere)
                    {
                        poweredPoints.Add(neighbor);
                    }
                }
            }
        }

        return false;
    }

    private bool IsTerminal(GameObject go) {
        if (_targetSequence.Contains(go)) return true;
        if (_visualsCache.TryGetValue(go, out var visual)) return visual.IsTerminal;
        return false;
    }
}
