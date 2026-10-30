using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Ignition zone trigger: when the player enters the collider, activates
/// groups of objects (e.g. torch pairs or fire wall segments) one by one
/// with a delay between groups. Used for staged fire ignition.
/// Implements ISaveable so ignited fire stays lit after loading a save.
/// </summary>
public class FireIgnitionZone : MonoBehaviour, ISaveable
{
    /// <summary>A set of objects activated simultaneously (e.g. a pair of torches).</summary>
    [Serializable]
    public class IgnitionGroup
    {
        public GameObject[] objects;
    }

    [Header("Ignition")]
    [Tooltip("Groups of objects activated one by one when the player enters the zone.")]
    [SerializeField] private IgnitionGroup[] _groups;

    [Tooltip("Pause between groups, in seconds.")]
    [SerializeField] private float _groupInterval = 0.35f;

    [Header("Save Settings")]
    [Tooltip("Unique save identifier. Leave empty to auto-generate a stable ID from the object's scene path.")]
    [SerializeField] private string _saveId;

    private bool _triggered;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Manual SaveId wins; otherwise a stable ID is derived from the object's full
    /// scene path (constant between launches, unique per zone instance).
    /// </summary>
    public string SaveId => !string.IsNullOrEmpty(_saveId) ? _saveId : $"fire_zone{GetHierarchyPath(transform)}";

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = $"{t.name}/{path}";
        }
        return "/" + path;
    }

    public string GetSaveData()
        => JsonUtility.ToJson(new SaveData { isTriggered = _triggered });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _triggered = data.isTriggered;

        if (_triggered)
            IgniteAllImmediately();
    }

    [Serializable]
    private struct SaveData
    {
        public bool isTriggered;
    }

    /// <summary>Activates every group at once — no staged delay on load.</summary>
    private void IgniteAllImmediately()
    {
        StopAllCoroutines();
        if (_groups == null) return;
        foreach (IgnitionGroup group in _groups)
        {
            if (group?.objects == null) continue;
            foreach (GameObject obj in group.objects)
                if (obj != null) obj.SetActive(true);
        }
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Start()
    {
        // При загрузке сохранения LoadSaveData уже зажёг огонь —
        // стартовое "гашение" применять нельзя.
        if (_triggered) return;

        if (_groups == null) return;
        foreach (IgnitionGroup group in _groups)
        {
            if (group?.objects == null) continue;
            foreach (GameObject obj in group.objects)
                if (obj != null) obj.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered || other.GetComponentInParent<FPSController>() == null) return;
        _triggered = true;
        SaveManager.Instance?.Save();
        StartCoroutine(IgniteSequence());
    }

    private IEnumerator IgniteSequence()
    {
        if (_groups == null) yield break;

        foreach (IgnitionGroup group in _groups)
        {
            if (group?.objects == null) continue;

            foreach (GameObject obj in group.objects)
                if (obj != null) obj.SetActive(true);

            if (_groupInterval > 0f)
                yield return new WaitForSeconds(_groupInterval);
        }
    }
}
