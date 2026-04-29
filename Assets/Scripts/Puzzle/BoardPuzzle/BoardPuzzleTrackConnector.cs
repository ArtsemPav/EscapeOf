using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Defines internal logical connections between specific points (GameObjects with colliders) on a cylinder.
/// Allows BoardPuzzleManager to trace paths through the cylinders based on linked points.
/// </summary>
public class BoardPuzzleTrackConnector : MonoBehaviour
{
    [System.Serializable]
    public struct PointConnection
    {
        public string Name;
        [Tooltip("The starting point GameObject (e.g., Col (1))")]
        public GameObject PointA;
        [Tooltip("The destination point GameObjects linked to PointA")]
        public List<GameObject> ConnectedPoints;
    }

    [Header("Point-to-Point Connections")]
    [Tooltip("Define which physical points are connected inside this cylinder. Connections are bidirectional by default.")]
    [SerializeField] private List<PointConnection> _connections = new List<PointConnection>();

    private Dictionary<GameObject, HashSet<GameObject>> _connectionMap;

    private void Awake()
    {
        InitializeConnectionMap();
    }

    /// <summary>
    /// Builds a fast lookup map for connections.
    /// Connections are now unidirectional to allow for specific routing logic.
    /// For bidirectional connections, define them explicitly in both directions.
    /// </summary>
    private void InitializeConnectionMap()
    {
        _connectionMap = new Dictionary<GameObject, HashSet<GameObject>>();

        foreach (var conn in _connections)
        {
            if (conn.PointA == null) continue;

            if (!_connectionMap.ContainsKey(conn.PointA))
                _connectionMap[conn.PointA] = new HashSet<GameObject>();

            foreach (var linked in conn.ConnectedPoints)
            {
                if (linked == null) continue;

                // Add A -> B only
                _connectionMap[conn.PointA].Add(linked);
            }
        }
    }

    /// <summary>
    /// Returns all GameObjects connected to the given point inside this cylinder.
    /// </summary>
    public IEnumerable<GameObject> GetConnectedPoints(GameObject fromPoint)
    {
        if (_connectionMap == null) InitializeConnectionMap();
        
        if (_connectionMap.TryGetValue(fromPoint, out var connected))
        {
            return connected;
        }
        return new List<GameObject>();
    }

    /// <summary>
    /// Checks if there is a direct logical connection between two specific points.
    /// </summary>
    public bool ArePointsConnected(GameObject pointA, GameObject pointB)
    {
        if (_connectionMap == null) InitializeConnectionMap();

        if (_connectionMap.TryGetValue(pointA, out var connected))
        {
            return connected.Contains(pointB);
        }
        return false;
    }
}
