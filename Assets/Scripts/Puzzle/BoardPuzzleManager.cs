using UnityEngine;
using UnityEngine.Events;
using System;
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

    // All BoardPuzzleTrackConnectors found in the scene, cached on Start.
    private List<BoardPuzzleTrackConnector> _allTrackConnectors;

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void Start() {
        _allTrackConnectors = new List<BoardPuzzleTrackConnector>(
            FindObjectsByType<BoardPuzzleTrackConnector>(FindObjectsSortMode.None));

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
            // Re-fire the event so listeners (doors, lights, etc.) can restore their state.
            OnPuzzleSolved.Invoke();
            return;
        }

        foreach (var pipe in _cylinders) {
            if (pipe != null)
                pipe.OnRotated += CheckSolution;
        }
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

        Debug.Log("<color=cyan>[Puzzle] --- Starting Path Validation ---</color>");

        GameObject startTerminal = _targetSequence[0];
        List<GameObject> startConnectors = FindLogicalConnectors(startTerminal);

        if (startConnectors.Count == 0) {
            Debug.Log($"<color=red>[Puzzle] No logical connectors for start terminal '{startTerminal.name}'. " +
                      $"Check BoardPuzzleTrackConnector setup.</color>");
            return;
        }

        foreach (GameObject startConnector in startConnectors) {
            Debug.Log($"[Puzzle] Trying start direction: {startConnector.name}");
            if (TraceSequence(startConnector, fromTerminal: startTerminal, targetIndex: 1)) {
                Debug.Log("<color=cyan>[Puzzle] !!! PUZZLE SOLVED !!!</color>");
                _isSolved = true;
                LockAllCylinders();
                SaveManager.Instance?.Save();
                OnPuzzleSolved.Invoke();
                return;
            }
        }

        Debug.Log("<color=red>[Puzzle] Path broken! No valid path found.</color>");
    }

    // -------------------------------------------------------------------------
    // Sequence tracer (terminal level)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Recursively traces from exitConnector trying to reach _targetSequence[targetIndex],
    /// then continues to the next terminal via the opposite connector.
    /// </summary>
    private bool TraceSequence(GameObject exitConnector, GameObject fromTerminal, int targetIndex) {
        if (targetIndex >= _targetSequence.Count) return true;

        GameObject targetTerminal = _targetSequence[targetIndex];

        // Pass the target terminal so TraceToTerminal skips wrong terminals
        // instead of returning the first one it encounters.
        if (!TraceToTerminal(exitConnector, fromTerminal, targetTerminal, out GameObject arrivalConnector)) {
            Debug.Log($"[Puzzle] Dead end from {exitConnector.name}");
            return false;
        }

        Debug.Log($"<color=green>[Puzzle] Step {targetIndex}: reached {targetTerminal.name} " +
                  $"via {arrivalConnector.name}</color>");

        // Last terminal — done.
        if (targetIndex == _targetSequence.Count - 1) return true;

        // Find the exit connector: the other logical connector of this terminal.
        List<GameObject> terminalConnectors = FindLogicalConnectors(targetTerminal);
        GameObject nextExit = terminalConnectors.FirstOrDefault(c => c != arrivalConnector);

        if (nextExit == null) {
            Debug.Log($"<color=red>[Puzzle] No exit connector at {targetTerminal.name} " +
                      $"(entered via {arrivalConnector.name})</color>");
            return false;
        }

        Debug.Log($"[Puzzle] Exiting {targetTerminal.name} via {nextExit.name}");
        return TraceSequence(nextExit, fromTerminal: targetTerminal, targetIndex: targetIndex + 1);
    }

    // -------------------------------------------------------------------------
    // Cylinder-network tracer (connector level)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Follows PHYSICAL -> INTERNAL -> PHYSICAL -> ... from exitConnector
    /// until the targetTerminal is reached. Wrong terminals are treated as dead ends.
    /// </summary>
    private bool TraceToTerminal(GameObject exitConnector, GameObject fromTerminal,
                                  GameObject targetTerminal,
                                  out GameObject arrivalConnector) {
        arrivalConnector = null;

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
                Debug.Log($"[Path] PHYSICAL: {current.name} -> {neighbor.name}");

                // Check logical arrival at any terminal.
                GameObject terminal = FindLogicalTerminal(neighbor);
                if (terminal != null) {
                    Debug.Log($"[Path] LOGICAL TERMINAL: {neighbor.name} -> {terminal.name}");

                    if (terminal == targetTerminal) {
                        arrivalConnector = neighbor;
                        return true;
                    }

                    // Wrong terminal — treat as dead end, do not pass through it.
                    visited.Add(neighbor);
                    continue;
                }

                // INTERNAL step: follow connections within neighbor's cylinder.
                BoardPuzzleTrackConnector connector =
                    neighbor.GetComponentInParent<BoardPuzzleTrackConnector>();

                if (connector != null) {
                    foreach (GameObject linked in connector.GetConnectedPoints(neighbor)) {
                        if (_targetSequence.Contains(linked)) continue;
                        if (!visited.Contains(linked)) {
                            Debug.Log($"[Path] INTERNAL: {neighbor.name} -> {linked.name}");
                            stack.Push((linked, neighbor));
                        }
                    }
                }
            }
        }

        return false;
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
                if (!_targetSequence.Contains(connected) && !results.Contains(connected))
                    results.Add(connected);
            }
        }
        return results;
    }

    /// <summary>
    /// Checks whether any BoardPuzzleTrackConnector defines a logical connection
    /// FROM connector TO a terminal. Returns the terminal if found, null otherwise.
    /// </summary>
    private GameObject FindLogicalTerminal(GameObject connector) {
        foreach (BoardPuzzleTrackConnector tc in _allTrackConnectors) {
            foreach (GameObject linked in tc.GetConnectedPoints(connector)) {
                if (_targetSequence.Contains(linked))
                    return linked;
            }
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

        Collider[] overlaps = Physics.OverlapSphere(col.bounds.center, _connectionDetectionRadius);
        foreach (Collider other in overlaps) {
            GameObject go = other.gameObject;
            if (go == point) continue;
            if (go == exclude) continue;
            if (_targetSequence.Contains(go)) continue;

            // Accept only connector-point children, not the cylinder GameObject itself.
            BoardPuzzleTrackConnector parentConnector = go.GetComponentInParent<BoardPuzzleTrackConnector>();
            if (parentConnector != null && parentConnector.gameObject != go)
                results.Add(go);
        }
        return results;
    }
}
