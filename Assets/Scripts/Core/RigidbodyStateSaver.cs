using System;
using UnityEngine;

/// <summary>
/// Persists the world position and rotation of this object's Rigidbody across saves,
/// so physically moved/dropped props (e.g. a clock that fell after a wall vanished)
/// restore to their final pose on load instead of replaying the fall — and impact
/// sounds — every time. Save() should be called by the gameplay flow when the prop
/// has come to rest (e.g. from the puzzle's solved sequence).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RigidbodyStateSaver : MonoBehaviour, ISaveable
{
    [Header("Save Settings")]
    [SerializeField] private string _saveId = "rigidbody_state_unique_id";

    [Tooltip("If true, the velocity/angular velocity is persisted too. Only needed for props " +
             "that must resume mid-motion; leave off for props that settle after falling.")]
    [SerializeField] private bool _saveVelocities = false;

    private Rigidbody _rigidbody;

    // ── ISaveable ───────────────────────────────────────────────────────

    /// <summary>Unique, never-changing identifier used by the save system.</summary>
    public string SaveId => _saveId;

    /// <summary>Returns a JSON snapshot of the rigidbody's current world pose.</summary>
    public string GetSaveData()
    {
        var data = new SaveData
        {
            hasState = true,
            positionX = transform.position.x,
            positionY = transform.position.y,
            positionZ = transform.position.z,
            rotationX = transform.rotation.x,
            rotationY = transform.rotation.y,
            rotationZ = transform.rotation.z,
            rotationW = transform.rotation.w,
            velocityX = _saveVelocities ? _rigidbody.linearVelocity.x : 0f,
            velocityY = _saveVelocities ? _rigidbody.linearVelocity.y : 0f,
            velocityZ = _saveVelocities ? _rigidbody.linearVelocity.z : 0f,
            angularVelocityX = _saveVelocities ? _rigidbody.angularVelocity.x : 0f,
            angularVelocityY = _saveVelocities ? _rigidbody.angularVelocity.y : 0f,
            angularVelocityZ = _saveVelocities ? _rigidbody.angularVelocity.z : 0f
        };
        return JsonUtility.ToJson(data);
    }

    /// <summary>Teleports the rigidbody to the saved pose (and velocities, if enabled).</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (!data.hasState)
        {
            return;
        }

        // Teleport via the physics API so interpolation and collision state stay consistent.
        _rigidbody.position = new Vector3(data.positionX, data.positionY, data.positionZ);
        _rigidbody.rotation = new Quaternion(data.rotationX, data.rotationY, data.rotationZ, data.rotationW);

        if (_saveVelocities)
        {
            _rigidbody.linearVelocity = new Vector3(data.velocityX, data.velocityY, data.velocityZ);
            _rigidbody.angularVelocity = new Vector3(data.angularVelocityX, data.angularVelocityY, data.angularVelocityZ);
        }
        else
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }
    }

    [Serializable]
    private struct SaveData
    {
        public bool hasState;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW;
        public float velocityX;
        public float velocityY;
        public float velocityZ;
        public float angularVelocityX;
        public float angularVelocityY;
        public float angularVelocityZ;
    }

    // ── Registration ────────────────────────────────────────────────────

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }
}
