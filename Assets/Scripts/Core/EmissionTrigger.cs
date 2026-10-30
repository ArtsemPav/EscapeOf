using System;
using UnityEngine;

/// <summary>
/// Enables emission on a target material when the player enters this trigger zone.
/// Optionally disables it again when the player leaves.
/// Works with URP/Lit: sets _EmissionColor and enables the _EMISSION keyword
/// on a material instance so other objects sharing the material stay unaffected.
/// Implements ISaveable so the emission state persists across game loads
/// (can be disabled per-instance via the Save State toggle).
/// </summary>
public class EmissionTrigger : MonoBehaviour, ISaveable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Trigger")]
    [Tooltip("Tag of the collider that activates the trigger.")]
    [SerializeField] private string _playerTag = "Player";

    [Header("Emission Target")]
    [Tooltip("Renderer whose material gets the emission. If empty, the renderer on this GameObject is used.")]
    [SerializeField] private Renderer _targetRenderer;

    [Tooltip("Emission color to apply. Values above 1 activate Bloom in URP.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _emissionColor = new Color(1f, 0.55f, 0.15f, 1f);

    [Header("Behaviour")]
    [Tooltip("When true, emission turns off again when the player exits the trigger.")]
    [SerializeField] private bool _disableOnExit;

    [Tooltip("When true, the trigger works only once and disables itself after firing.")]
    [SerializeField] private bool _fireOnce = true;

    [Header("Save Settings")]
    [Tooltip("When true, the emission state is saved and restored on game load. When false, this trigger is not persisted.")]
    [SerializeField] private bool _saveState = true;

    [Tooltip("Unique save identifier. Leave empty to auto-generate a stable ID from the object's scene path.")]
    [SerializeField] private string _saveId;

    private Material _material;
    private bool _fired;
    private bool _emissionOn;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Manual SaveId wins; otherwise a stable ID is derived from the object's full
    /// scene path (constant between launches, unique per trigger instance).
    /// </summary>
    public string SaveId => !string.IsNullOrEmpty(_saveId) ? _saveId : $"emission_trigger{GetHierarchyPath(transform)}";

    public string GetSaveData()
        => JsonUtility.ToJson(new SaveData { emissionOn = _emissionOn, fired = _fired });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _fired = data.fired;
        SetEmission(data.emissionOn);

        if (_fired && _fireOnce)
            enabled = false;
    }

    [Serializable]
    private struct SaveData
    {
        public bool emissionOn;
        public bool fired;
    }

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

    /// <summary>Records the emission state and queues a save checkpoint (only when saving is enabled).</summary>
    private void NotifyStateChanged()
    {
        if (!_saveState) return;
        SaveManager.Instance?.Save();
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();

        if (_targetRenderer != null)
            _material = _targetRenderer.material; // instance, so shared material is untouched
        else
            Debug.LogWarning($"[EmissionTrigger '{name}'] No renderer assigned or found on this GameObject.", this);

        // Регистрируемся только когда сохранение состояния включено.
        if (_saveState)
            SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (_saveState)
            SaveManager.Instance?.Unregister(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired && _fireOnce) return;
        if (!other.CompareTag(_playerTag)) return;

        SetEmission(true);
        _fired = true;
        NotifyStateChanged();

        if (_fireOnce)
            enabled = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (_fired && _fireOnce) return;
        if (!other.CompareTag(_playerTag)) return;

        if (_disableOnExit)
        {
            SetEmission(false);
            NotifyStateChanged();
        }
    }

    // ── Visual ─────────────────────────────────────────────────────────────────

    /// <summary>Enables or disables emission on the target material instance.</summary>
    private void SetEmission(bool active)
    {
        _emissionOn = active;
        if (_material == null) return;

        _material.SetColor(EmissionColorId, active ? _emissionColor : Color.black);
        _material.EnableKeyword("_EMISSION");
    }
}
