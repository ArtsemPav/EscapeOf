using UnityEngine;

/// <summary>
/// Player-facing system settings stored in PlayerPrefs and applied at startup.
/// SFX volume maps to the "SFXVolume" parameter of the SFX AudioMixer;
/// music volume is a 0–1 multiplier consumed by AudioManager and MusicDirector.
/// </summary>
public static class SystemSettings
{
    public const string MusicVolumePref = "Settings_MusicVolume";
    public const string SfxVolumePref = "Settings_SfxVolume";
    public const string QualityPref = "Settings_QualityLevel";

    /// <summary>Music loudness multiplier (0–1).</summary>
    public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumePref, 1f);

    /// <summary>Saved sound-effects loudness (0–1 normalized).</summary>
    public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumePref, 1f);

    /// <summary>Converts a normalized loudness (0–1) to AudioMixer decibels.</summary>
    public static float ToDecibels(float normalized)
        => normalized <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp(normalized, 0.0001f, 1f)) * 20f;

    /// <summary>Sets the music volume multiplier and persists it.</summary>
    public static void SetMusicVolume(float value) => PlayerPrefs.SetFloat(MusicVolumePref, Mathf.Clamp01(value));

    /// <summary>Sets the sound-effects volume, persists it and applies it to the SFX mixer immediately.</summary>
    public static void SetSfxVolume(float value)
    {
        PlayerPrefs.SetFloat(SfxVolumePref, Mathf.Clamp01(value));
        AudioManager.Instance?.ApplySavedSfxVolume();
    }

    /// <summary>Sets the graphics quality level and persists it.</summary>
    public static void SetQualityLevel(int level)
    {
        PlayerPrefs.SetInt(QualityPref, level);
        QualitySettings.SetQualityLevel(level, true);
    }

    /// <summary>Restores the saved quality level. Call once at startup.</summary>
    public static void ApplyAtStartup()
    {
        if (PlayerPrefs.HasKey(QualityPref))
            QualitySettings.SetQualityLevel(PlayerPrefs.GetInt(QualityPref), true);
    }
}
