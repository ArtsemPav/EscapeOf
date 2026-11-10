using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject string table for one language: a flat key → value list that
/// serializes cleanly in the Inspector. Create one asset per language via
/// Assets > Create > Escape > Localized Text Table and keep it under a
/// Resources folder so Localization can load it by language name.
/// </summary>
[CreateAssetMenu(fileName = "Ru", menuName = "Escape/Localized Text Table")]
public class LocalizedTextTable : ScriptableObject
{
    [System.Serializable]
    public class TextEntry
    {
        [Tooltip("Stable lookup key, e.g. 'ui.interact.apply'.")]
        public string key;

        [Tooltip("Translated string. Supports TextMeshPro rich-text tags.")]
        public string value;
    }

    [Tooltip("All strings of this language. Keys must be unique — duplicates are resolved by the first entry.")]
    [SerializeField] private List<TextEntry> entries = new List<TextEntry>();

    private Dictionary<string, string> _lookup;

    /// <summary>Builds the lookup dictionary once per play session / asset load.</summary>
    private void OnEnable()
    {
        if (_lookup != null) return;
        _lookup = new Dictionary<string, string>(entries.Count);
        foreach (TextEntry entry in entries)
        {
            if (string.IsNullOrEmpty(entry.key) || _lookup.ContainsKey(entry.key)) continue;
            _lookup.Add(entry.key, entry.value);
        }
    }

    /// <summary>Looks up a string by key; returns false when the key is missing.</summary>
    public bool TryGet(string key, out string value)
    {
        OnEnable();
        return _lookup.TryGetValue(key, out value);
    }
}
