using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages path validation for the board puzzle.
/// Traces a logical 'beam' through cylinders and checks if terminals are visited in the correct sequence.
/// </summary>
public class BoardPuzzleManager : MonoBehaviour {
    [Header("Puzzle Grid")]
    [Tooltip("All cylinders in the puzzle.")]
    [SerializeField] private BoardPuzzlePipe[] _cylinders;

    [Header("Path Sequence")]
    [Tooltip("The sequence of terminal GameObjects that must be visited in order (e.g., A -> E -> A -> B -> C).")]
    [SerializeField] private List<GameObject> _targetSequence;

    private Dictionary<GameObject, BoardPuzzlePipe> _pointToCylinderMap;

    private void Start() {
        InitializeMap();
        foreach (var pipe in _cylinders) {
            if (pipe != null)
                pipe.OnRotated += CheckSolution;
        }
    }

    private void OnDestroy() {
        foreach (var pipe in _cylinders) {
            if (pipe != null)
                pipe.OnRotated -= CheckSolution;
        }
    }

    private void InitializeMap() {
        _pointToCylinderMap = new Dictionary<GameObject, BoardPuzzlePipe>();
        if (_cylinders != null) {
            foreach (var pipe in _cylinders) {
                if (pipe == null) continue;
                AddPointsToMap(pipe.transform, pipe);
            }
        }
        
        // Ensure all target terminals are also in the map with a null pipe reference
        // This allows internal connections to trace to terminals even if they aren't children of a pipe
        foreach (var terminal in _targetSequence) {
            if (terminal != null && !_pointToCylinderMap.ContainsKey(terminal)) {
                _pointToCylinderMap[terminal] = null;
            }
        }
    }

    private void AddPointsToMap(Transform parent, BoardPuzzlePipe pipe) {
        foreach (Transform child in parent) {
            if (child.GetComponent<Collider>() != null) {
                _pointToCylinderMap[child.gameObject] = pipe;
            }
            AddPointsToMap(child, pipe);
        }
    }

    public void CheckSolution() {
        if (_targetSequence == null || _targetSequence.Count < 2) return;

        Debug.Log("<color=cyan>[Puzzle] --- Starting Path Validation ---</color>");

        int sequenceIndex = 0;
        GameObject currentPoint = _targetSequence[0];

        while (sequenceIndex < _targetSequence.Count - 1) {
            GameObject nextTerminal = _targetSequence[sequenceIndex + 1];
            Debug.Log($"<color=yellow>[Puzzle] Attempting to reach: {nextTerminal.name} from {currentPoint.name}</color>");

            if (CanReachTarget(currentPoint, nextTerminal, out GameObject reachedPoint)) {
                sequenceIndex++;
                currentPoint = reachedPoint;
                Debug.Log($"<color=green>[Puzzle] Step {sequenceIndex} reached: {nextTerminal.name}</color>");
            } else {
                Debug.Log($"<color=red>[Puzzle] Path broken! Could not reach {nextTerminal.name}</color>");
                return;
            }
        }

        if (sequenceIndex == _targetSequence.Count - 1) {
            Debug.Log("<color=cyan>[Puzzle] !!! PUZZLE SOLVED !!! Sequence complete.</color>");
        }
    }

    private bool CanReachTarget(GameObject startPoint, GameObject targetTerminal, out GameObject finalPoint) {
        finalPoint = null;
        Queue<GameObject> toVisit = new Queue<GameObject>();
        HashSet<GameObject> visited = new HashSet<GameObject>();

        toVisit.Enqueue(startPoint);

        while (toVisit.Count > 0) {
            GameObject current = toVisit.Dequeue();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            if (current == targetTerminal) {
                finalPoint = current;
                return true;
            }

            // 1. Check external connections (to neighboring cylinders/terminals)
            GameObject neighborPoint = FindNeighborConnection(current);
            if (neighborPoint != null && !visited.Contains(neighborPoint)) {
                Debug.Log($"[Path Trace] PHYSICAL: {current.name} -> {neighborPoint.name}");
                toVisit.Enqueue(neighborPoint);
            }

            // 2. Check internal connections in the current cylinder or terminal
            BoardPuzzleTrackConnector connector = current.GetComponentInParent<BoardPuzzleTrackConnector>();
            if (connector != null) {
                foreach (var linked in connector.GetConnectedPoints(current)) {
                    if (!visited.Contains(linked)) {
                        Debug.Log($"[Path Trace] INTERNAL ({current.name}): {current.name} -> {linked.name}");
                        toVisit.Enqueue(linked);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a point on an adjacent cylinder that matches the position/logic of the current point.
    /// </summary>
    private GameObject FindNeighborConnection(GameObject point) {
        Collider col = point.GetComponent<Collider>();
        if (col == null) return null;

        // Increased radius to 0.1f to handle potential small gaps between connectors
        Collider[] overlaps = Physics.OverlapSphere(col.bounds.center, 0.1f);
        foreach (var other in overlaps) {
            if (other.gameObject == point) continue;

            // Check if it's a connection point on another cylinder or a terminal
            if (_pointToCylinderMap.ContainsKey(other.gameObject)) {
                return other.gameObject;
            }

            // Check if it's one of the target terminals
            if (_targetSequence.Contains(other.gameObject)) {
                return other.gameObject;
            }
        }

        return null;
    }
}