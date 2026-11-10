using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject sprite table for one language: image assets containing baked-in
/// text (document page scans, photos, whiteboards). Create one asset per language
/// via Assets > Create > Escape > Localized Sprite Table. Callers must fall back to
/// the original sprite when a key is missing (see Localization.GetSprite).
/// </summary>
[CreateAssetMenu(fileName = "RuSprites", menuName = "Escape/Localized Sprite Table")]
public class LocalizedSpriteTable : ScriptableObject
{
    [System.Serializable]
    public class SpriteEntry
    {
        [Tooltip("Stable lookup key, e.g. 'laptop.<fileId>.page0'.")]
        public string key;

        [Tooltip("Translated sprite. Keep the original's aspect ratio so layout stays intact.")]
        public Sprite sprite;
    }

    [Tooltip("All translated sprites of this language. Keys must be unique — duplicates are resolved by the first entry.")]
    [SerializeField] private List<SpriteEntry> entries = new List<SpriteEntry>();

    private Dictionary<string, Sprite> _lookup;

    /// <summary>Builds the lookup dictionary once per play session / asset load.</summary>
    private void OnEnable()
    {
        if (_lookup != null) return;
        _lookup = new Dictionary<string, Sprite>(entries.Count);
        foreach (SpriteEntry entry in entries)
        {
            if (string.IsNullOrEmpty(entry.key) || _lookup.ContainsKey(entry.key)) continue;
            _lookup.Add(entry.key, entry.sprite);
        }
    }

    /// <summary>Looks up a sprite by key; returns false when the key is missing.</summary>
    public bool TryGet(string key, out Sprite sprite)
    {
        OnEnable();
        return _lookup.TryGetValue(key, out sprite);
    }
}
