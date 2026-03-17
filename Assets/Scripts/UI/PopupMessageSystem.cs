using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton manager for popup messages (hints, events, warnings).
///
/// SETUP:
///   1. Add this component to the Canvas GameObject (or any persistent object).
///   2. Create a PopupMessageEntry prefab and assign it to entryPrefab.
///   3. Create an empty RectTransform inside the Canvas, assign it to container.
///      Recommended: anchor to a corner (e.g., bottom-right) and set layout group.
///
/// USAGE:
///   PopupMessageSystem.Instance.Show("Используйте E для взаимодействия");
///   PopupMessageSystem.Instance.Show(new PopupMessageData("Дверь открыта!", PopupMessageType.Event, 4f));
/// </summary>
public class PopupMessageSystem : MonoBehaviour
{
    public static PopupMessageSystem Instance { get; private set; }

    [Header("References")]
    [Tooltip("Prefab with PopupMessageEntry component.")]
    [SerializeField] private PopupMessageEntry entryPrefab;

    [Tooltip("Parent RectTransform where popup entries are instantiated.")]
    [SerializeField] private RectTransform container;

    [Header("Configuration")]
    [Tooltip("Maximum number of popups visible simultaneously. Oldest is dismissed when exceeded.")]
    [SerializeField] private int maxVisiblePopups = 3;

    private readonly Queue<PopupMessageData> _queue    = new Queue<PopupMessageData>();
    private readonly List<PopupMessageEntry> _active   = new List<PopupMessageEntry>();

    private bool _isProcessingQueue;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this);
    }

    /// <summary>
    /// Shows a simple hint popup with default settings.
    /// </summary>
    /// <param name="text">Message text. Supports TMP rich-text tags.</param>
    /// <param name="type">Visual style and priority category.</param>
    /// <param name="duration">Seconds the popup stays visible before fading out.</param>
    public void Show(string text, PopupMessageType type = PopupMessageType.Hint, float duration = 3f)
    {
        Show(new PopupMessageData(text, type, duration));
    }

    /// <summary>
    /// Shows a fully configured popup. If max visible count is reached, the oldest
    /// active popup is dismissed immediately to make room.
    /// </summary>
    public void Show(PopupMessageData data)
    {
        if (entryPrefab == null || container == null)
        {
            Debug.LogWarning("[PopupMessageSystem] entryPrefab or container is not assigned.", this);
            return;
        }

        if (_active.Count >= maxVisiblePopups && _active.Count > 0)
            DismissOldest();

        SpawnEntry(data);
    }

    /// <summary>Immediately dismisses all currently visible popups.</summary>
    public void DismissAll()
    {
        foreach (PopupMessageEntry entry in _active)
            entry?.Dismiss();

        _active.Clear();
        _queue.Clear();
    }

    private void SpawnEntry(PopupMessageData data)
    {
        PopupMessageEntry entry = Instantiate(entryPrefab, container);
        _active.Add(entry);

        entry.Play(data, () => OnEntryFinished(entry));
    }

    private void OnEntryFinished(PopupMessageEntry entry)
    {
        _active.Remove(entry);
        if (entry != null)
            Destroy(entry.gameObject);
    }

    private void DismissOldest()
    {
        if (_active.Count == 0) return;
        PopupMessageEntry oldest = _active[0];
        _active.RemoveAt(0);
        oldest.Dismiss();
    }
}
