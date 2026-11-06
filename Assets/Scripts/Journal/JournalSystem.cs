using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central journal state: fixed document categories (set in the Inspector) and the set
/// of unlocked (read) documents. Entry registration happens on the first read of a
/// document in the world via <see cref="DocumentInteraction"/> calling <see cref="AddEntry"/>.
/// Persists unlocked entries through <see cref="SaveManager"/> as ISaveable with SaveId "journal".
/// </summary>
public class JournalSystem : MonoBehaviour, ISaveable
{
    public static JournalSystem Instance { get; private set; }

    [Serializable]
    public class JournalCategory
    {
        [Tooltip("Текст на кнопке категории слева.")]
        public string categoryName;

        [Tooltip("Документы категории в фиксированном порядке — определяют порядок страниц в стопке.")]
        public List<DocumentData> documents = new List<DocumentData>();
    }

    [Header("Categories")]
    [Tooltip("Фиксированные категории журнала. Заполняются в инспекторе.")]
    [SerializeField] private List<JournalCategory> _categories = new List<JournalCategory>();

    private const string SaveIdValue = "journal";

    // Unlocked documents: HashSet for fast lookup + parallel list for stable save order.
    private readonly HashSet<DocumentData> _unlocked = new HashSet<DocumentData>();
    private readonly List<DocumentData> _unlockedList = new List<DocumentData>();

    private bool _isOpen;

    /// <summary>True while the journal panel is open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Fixed journal categories (Inspector-configured).</summary>
    public List<JournalCategory> Categories => _categories;

    /// <summary>Raised when a new document is unlocked (first read) — used by the UI to refresh buttons.</summary>
    public event Action<DocumentData> OnEntryAdded;

    /// <summary>Raised after the journal opens.</summary>
    public event Action OnJournalOpened;

    /// <summary>Raised after the journal closes.</summary>
    public event Action OnJournalClosed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    private void Update()
    {
        // J toggles the journal. Polled here because JournalUI lives on the panel
        // (inactive while closed) and the project has no Journal input action yet.
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.jKey.wasPressedThisFrame)
            JournalUI.Instance?.Toggle();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SaveManager.Instance?.Unregister(this);
            Instance = null;
        }
    }

    /// <summary>
    /// Unlocks the document (registers a journal entry). Idempotent — repeated reads are safe.
    /// Called from DocumentInteraction.Interact() on the first read; the world object stays in place.
    /// </summary>
    public void AddEntry(DocumentData data)
    {
        if (data == null || _unlocked.Contains(data)) return;

        _unlocked.Add(data);
        _unlockedList.Add(data);
        OnEntryAdded?.Invoke(data);
        SaveManager.Instance?.Save();
    }

    /// <summary>True if the document has been read (unlocked in the journal).</summary>
    public bool IsUnlocked(DocumentData data) => data != null && _unlocked.Contains(data);

    /// <summary>Opens the journal (blocked while any other panel is open).</summary>
    public void OpenJournal()
    {
        if (_isOpen) return;
        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen) return;

        _isOpen = true;
        OnJournalOpened?.Invoke();
    }

    /// <summary>Closes the journal.</summary>
    public void CloseJournal()
    {
        if (!_isOpen) return;

        _isOpen = false;
        OnJournalClosed?.Invoke();
    }

    // ── ISaveable ────────────────────────────────────────────────────────────

    /// <summary>Stable save identifier.</summary>
    public string SaveId => SaveIdValue;

    /// <summary>Serializes unlocked documents as a list of DocumentData asset names.</summary>
    public string GetSaveData()
    {
        var entryIds = new List<string>(_unlockedList.Count);
        for (int i = 0; i < _unlockedList.Count; i++)
            entryIds.Add(_unlockedList[i].name);

        return JsonUtility.ToJson(new JournalSaveData { entryIds = entryIds });
    }

    /// <summary>Restores unlocked documents from saved asset names. Old saves stay compatible.</summary>
    public void LoadSaveData(string json)
    {
        _unlocked.Clear();
        _unlockedList.Clear();

        if (string.IsNullOrEmpty(json)) return;

        var data = JsonUtility.FromJson<JournalSaveData>(json);
        if (data == null || data.entryIds == null) return;

        var allDocuments = Resources.FindObjectsOfTypeAll<DocumentData>();
        foreach (string id in data.entryIds)
        {
            foreach (var document in allDocuments)
            {
                if (document != null && document.name == id && !_unlocked.Contains(document))
                {
                    _unlocked.Add(document);
                    _unlockedList.Add(document);
                    break;
                }
            }
        }
    }

    [Serializable]
    private class JournalSaveData
    {
        public List<string> entryIds;
    }
}
