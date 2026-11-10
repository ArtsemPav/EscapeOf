using System;
using UnityEngine;

/// <summary>
/// Player-facing language setting stored in PlayerPrefs, following the
/// <see cref="SystemSettings"/> pattern. Raising <see cref="LanguageChanged"/>
/// lets localized UI (LocalizedTMP, LocalizedImage, ...) refresh itself live.
/// </summary>
public static class LocalizationSettings
{
    public const string LanguagePref = "Settings_Language";

    /// <summary>Language assumed for players who have not chosen one yet.</summary>
    public const Language DefaultLanguage = Language.Ru;

    /// <summary>Raised after the language changes so localized UI can refresh.</summary>
    public static event Action LanguageChanged;

    /// <summary>The saved language, or <see cref="DefaultLanguage"/> when unset.</summary>
    public static Language Current =>
        (Language)PlayerPrefs.GetInt(LanguagePref, (int)DefaultLanguage);

    /// <summary>Saves the language and notifies all localized UI.</summary>
    public static void SetLanguage(Language value)
    {
        PlayerPrefs.SetInt(LanguagePref, (int)value);
        LanguageChanged?.Invoke();
    }
}
