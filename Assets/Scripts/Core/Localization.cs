using System;
using UnityEngine;

/// <summary>
/// Static localization facade: resolves translated strings and sprites for the
/// current <see cref="Language"/>. Tables are loaded from any Resources folder as
/// "Localization/&lt;language name&gt;" (text) and "Localization/&lt;language name&gt;Sprites"
/// (sprites), e.g. Assets/Data/Localization/Resources/Localization/Ru.asset.
/// Missing keys log a warning and return the key itself — a visible stub instead
/// of a crash. UI components subscribe to LocalizationSettings.LanguageChanged.
/// </summary>
public static class Localization
{
    private const string TextTableResourcePath = "Localization/{0}";
    private const string SpriteTableResourcePath = "Localization/{0}Sprites";

    private static LocalizedTextTable _textTable;
    private static LocalizedSpriteTable _spriteTable;
    private static Language _loadedLanguage = (Language)(-1);
    private static bool _initialized;

    /// <summary>Resolves the tables for the current language once, then tracks language switches.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        LocalizationSettings.LanguageChanged += OnLanguageChanged;
        LoadTables(LocalizationSettings.Current);
    }

    /// <summary>Drops cached tables so the next Get re-loads the new language's tables.</summary>
    private static void OnLanguageChanged()
    {
        _loadedLanguage = (Language)(-1);
        _textTable = null;
        _spriteTable = null;
        LoadTables(LocalizationSettings.Current);
    }

    /// <summary>Loads both tables of the given language from Resources.</summary>
    private static void LoadTables(Language language)
    {
        if (_loadedLanguage == language && _textTable != null) return;
        _loadedLanguage = language;

        string folder = language.ToString();
        _textTable = Resources.Load<LocalizedTextTable>(string.Format(TextTableResourcePath, folder));
        _spriteTable = Resources.Load<LocalizedSpriteTable>(string.Format(SpriteTableResourcePath, folder));

        if (_textTable == null)
            Debug.LogWarning($"[Localization] Text table for '{folder}' not found in any Resources folder. " +
                             "Raw keys will be shown instead of strings.");
    }

    /// <summary>
    /// Resolves the translated string for the key without logging a warning on
    /// missing keys. Returns false and the key itself when the key is missing —
    /// use for probing optional keys; <see cref="Get"/> for mandatory ones.
    /// </summary>
    public static bool TryGet(string key, out string value)
    {
        EnsureInitialized();

        value = key;
        return _textTable != null && _textTable.TryGet(key, out value);
    }

    /// <summary>Returns the translated string for the key, or the key itself when missing.</summary>
    public static string Get(string key)
    {
        if (TryGet(key, out string value)) return value;

        Debug.LogWarning($"[Localization] Missing key '{key}' for language '{_loadedLanguage}'.");
        return key;
    }

    /// <summary>
    /// Returns the translation for the key, or the fallback when the key is missing.
    /// Standard pattern for Inspector-field fallbacks: localized value wins, serialized text stays as backup.
    /// </summary>
    public static string Resolve(string key, string fallback)
        => TryGet(key, out string value) ? value : fallback;

    /// <summary>Returns a formatted string (string.Format over the translated template).</summary>
    public static string GetFormat(string key, params object[] args)
        => string.Format(Get(key), args);

    /// <summary>
    /// Returns the translated sprite for the key, or null when missing —
    /// callers must keep a fallback on the original (untranslated) sprite.
    /// </summary>
    public static Sprite GetSprite(string key)
    {
        EnsureInitialized();

        if (_spriteTable != null && _spriteTable.TryGet(key, out Sprite sprite)) return sprite;
        return null;
    }
}
