using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Slows the player down when they enter this trigger, shows a spider web
/// overlay on the screen and destroys the web so it can only be passed once.
/// Attach to spider web trigger objects (e.g. PRF_SpiderWeb).
/// Implements ISaveable so a passed web stays destroyed after loading a save.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpiderWebSlowTrigger : MonoBehaviour, ISaveable
{
    [Header("Slow Settings")]
    [Tooltip("Movement multiplier applied while the effect is active (0..1). " +
             "0.5 means the player moves at half speed.")]
    [SerializeField, Range(0.01f, 1f)] private float speedMultiplier = 0.5f;

    [Tooltip("How long (seconds) the slow effect lasts after touching the web.")]
    [SerializeField] private float slowDuration = 3f;

    [Tooltip("Minimal time (seconds) between re-triggers to avoid spam.")]
    [SerializeField] private float reTriggerDelay = 1f;

    [Tooltip("Destroy the web object after the player passes through it.")]
    [SerializeField] private bool destroyOnPass = true;

    [Header("Flashlight Dimming")]
    [Tooltip("Flashlight to dim when touching the web. If not set, uses FlashlightController.Instance.")]
    [SerializeField] private FlashlightController flashlight;

    [Tooltip("Intensity multiplier while the web effect is active (0..1). 0.35 = the light drops to 35% brightness.")]
    [SerializeField, Range(0f, 1f)] private float flashlightDimScale = 0.35f;

    [Tooltip("How long (seconds) the flashlight stays dimmed. 0 = use the slow duration.")]
    [SerializeField] private float flashlightDimDuration = 0f;

    [Header("Save Settings")]
    [Tooltip("Unique save identifier. Leave empty to auto-generate a stable ID from the object's scene path.")]
    [SerializeField] private string _saveId;

    private bool _isPassed;
    private float _lastTriggerTime = -Mathf.Infinity;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Manual SaveId wins; otherwise a stable ID is derived from the object's full
    /// scene path (constant between launches, unique per web instance).
    /// </summary>
    public string SaveId => !string.IsNullOrEmpty(_saveId) ? _saveId : $"spider_web{GetHierarchyPath(transform)}";

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
        => JsonUtility.ToJson(new SaveData { isPassed = _isPassed });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isPassed = data.isPassed;

        if (_isPassed)
            Destroy(gameObject);
    }

    [Serializable]
    private struct SaveData
    {
        public bool isPassed;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        // A passed web stays registered: SaveManager may still call GetSaveData()
        // during the debounce window after this object is destroyed.
        // The next scene load overwrites the registry entry with a fresh instance.
        if (!string.IsNullOrEmpty(_saveId) && !_isPassed)
            SaveManager.Instance?.Unregister(this);
    }

    private void Reset()
    {
        // When the component is added in the editor, make sure the collider is a trigger.
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - _lastTriggerTime < reTriggerDelay) return;

        var player = other.GetComponentInParent<FPSController>();
        if (player == null) return;

        _lastTriggerTime = Time.time;
        player.ApplyWebSlow(speedMultiplier);
        SpiderWebOverlay.Instance?.Show(slowDuration);

        var targetFlashlight = flashlight != null ? flashlight : FlashlightController.Instance;
        targetFlashlight?.DimTemporarily(
            flashlightDimScale,
            flashlightDimDuration > 0f ? flashlightDimDuration : slowDuration);

        // Корутина запускается на игроке: этот объект может быть уничтожен
        // сразу после прохода, и тогда рестор скорости не выполнится.
        player.StartCoroutine(RestoreSpeedAfterDelay(player, slowDuration));

        if (destroyOnPass)
            NotifyPassed();
    }

    private IEnumerator RestoreSpeedAfterDelay(FPSController player, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (player != null) player.RemoveWebSlow();
    }

    /// <summary>
    /// Records that the web has been passed and queues a save checkpoint.
    /// Must be called before Destroy(gameObject) so the snapshot captures the state.
    /// </summary>
    private void NotifyPassed()
    {
        _isPassed = true;
        SaveManager.Instance?.Save();
        Destroy(gameObject);
    }
}
