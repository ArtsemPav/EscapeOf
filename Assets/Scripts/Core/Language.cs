using UnityEngine;

/// <summary>
/// Supported UI languages. Add a new value here (and a matching text table asset)
/// to ship another language.
/// </summary>
public enum Language
{
    Ru = 0,
    En = 1,
}

/// <summary>Extensions for the <see cref="Language"/> enum.</summary>
public static class LanguageExtensions
{
    /// <summary>
    /// Returns the display name of the language, shown in the settings language row.
    /// Uses the native self-name ("Русский", "English") localized under
    /// ui.settings.language_name.&lt;enum name&gt;; falls back to the enum name
    /// when the key is missing.
    /// </summary>
    public static string DisplayName(this Language language)
    {
        string key = LocalizationKeys.LanguageNamePrefix + language;
        return Localization.TryGet(key, out string name) ? name : language.ToString();
    }
}
