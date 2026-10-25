using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Player-facing system settings stored in PlayerPrefs and applied at startup.
/// Volumes map to the exposed MusicVolume / SFXVolume parameters of the audio
/// mixer, so moving a slider affects currently playing audio instantly.
/// </summary>
public static class SystemSettings
{
    public const string MusicVolumePref = "Settings_MusicVolume";
    public const string SfxVolumePref = "Settings_SfxVolume";
    public const string QualityPref = "Settings_QualityLevel";
    public const string ResolutionWidthPref = "Settings_ResolutionWidth";
    public const string ResolutionHeightPref = "Settings_ResolutionHeight";
    public const string LegacyResolutionIndexPref = "Settings_ResolutionIndex";
    public const string TargetFrameRatePref = "Settings_TargetFrameRate";
    public const string FullScreenModePref = "Settings_FullScreenMode";

    /// <summary>
    /// Borderless fullscreen: letterboxes resolutions whose aspect ratio differs
    /// from the monitor's, so non-native modes show black bars instead of stretching.
    /// </summary>
    public const FullScreenMode DefaultFullScreenMode = FullScreenMode.FullScreenWindow;

    /// <summary>Music loudness (0–1) as written to the mixer's Music bus.</summary>
    public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumePref, 1f);

    /// <summary>Saved sound-effects loudness (0–1 normalized).</summary>
    public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumePref, 1f);

    /// <summary>Converts a normalized loudness (0–1) to AudioMixer decibels.</summary>
    public static float ToDecibels(float normalized)
        => normalized <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp(normalized, 0.0001f, 1f)) * 20f;

    /// <summary>Sets the music volume, persists it and applies it to the mixer immediately.</summary>
    public static void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat(MusicVolumePref, Mathf.Clamp01(value));
        ApplyMusicVolume();
    }

    /// <summary>Sets the sound-effects loudness, persists it and applies it to the mixer immediately.</summary>
    public static void SetSfxVolume(float value)
    {
        PlayerPrefs.SetFloat(SfxVolumePref, Mathf.Clamp01(value));
        ApplySfxVolume();
    }

    /// <summary>Pushes the saved music volume to the mixer's Music bus.</summary>
    public static void ApplyMusicVolume()
    {
        AudioMixer mixer = ResolveMixer();
        if (mixer != null) mixer.SetFloat("MusicVolume", ToDecibels(MusicVolume));
    }

    /// <summary>Pushes the saved SFX volume to the mixer's SFX bus.</summary>
    public static void ApplySfxVolume()
    {
        AudioMixer mixer = ResolveMixer();
        if (mixer != null) mixer.SetFloat("SFXVolume", ToDecibels(SfxVolume));
    }

    /// <summary>The AudioManager's mixer, when the manager exists in this scene.</summary>
    private static AudioMixer ResolveMixer()
    {
        var manager = AudioManager.Instance;
        return manager != null ? manager.ResolveMixerExternal() : null;
    }

    /// <summary>Sets the graphics quality level and persists it.</summary>
    public static void SetQualityLevel(int level)
    {
        PlayerPrefs.SetInt(QualityPref, level);
        QualitySettings.SetQualityLevel(level, true);
    }

    /// <summary>
    /// The saved resolution as width x height, or null when the player has not
    /// chosen one yet. Stored by dimensions (not by list index) so the choice
    /// survives changes to the supported-mode list and monitor swaps.
    /// </summary>
    public static VideoSettingOptions.ResolutionOption? SavedResolution
    {
        get
        {
            if (!PlayerPrefs.HasKey(ResolutionWidthPref) || !PlayerPrefs.HasKey(ResolutionHeightPref))
            {
                // Fall back to the legacy index-based pref written by older builds.
                int legacyIndex = PlayerPrefs.GetInt(LegacyResolutionIndexPref, -1);
                var options = VideoSettingOptions.Resolutions;
                if (legacyIndex >= 0 && legacyIndex < options.Count) return options[legacyIndex];
                return null;
            }

            return new VideoSettingOptions.ResolutionOption(
                PlayerPrefs.GetInt(ResolutionWidthPref),
                PlayerPrefs.GetInt(ResolutionHeightPref));
        }
    }

    /// <summary>The saved target frame rate, or <see cref="VideoSettingOptions.DefaultFrameRate"/> when unset.</summary>
    public static int TargetFrameRate => PlayerPrefs.GetInt(TargetFrameRatePref, VideoSettingOptions.DefaultFrameRate);

    /// <summary>
    /// The saved window mode, or <see cref="DefaultFullScreenMode"/> when unset.
    /// Exclusive fullscreen is never restored: the monitor driver stretches
    /// mismatched aspect ratios in that mode, so it is remapped to borderless.
    /// </summary>
    public static FullScreenMode FullScreenMode
    {
        get
        {
            if (!PlayerPrefs.HasKey(FullScreenModePref)) return DefaultFullScreenMode;
            FullScreenMode saved = (FullScreenMode)PlayerPrefs.GetInt(FullScreenModePref);
            return saved == FullScreenMode.ExclusiveFullScreen ? DefaultFullScreenMode : saved;
        }
    }

    /// <summary>Sets the window mode, persists it and re-applies the current resolution.</summary>
    public static void SetFullScreenMode(FullScreenMode mode)
    {
        PlayerPrefs.SetInt(FullScreenModePref, (int)mode);
        if (SavedResolution is VideoSettingOptions.ResolutionOption saved)
            Screen.SetResolution(saved.Width, saved.Height, mode);
        else
            Screen.fullScreenMode = mode;
    }

    /// <summary>Applies and persists the given resolution.</summary>
    public static void SetResolution(VideoSettingOptions.ResolutionOption option)
    {
        PlayerPrefs.SetInt(ResolutionWidthPref, option.Width);
        PlayerPrefs.SetInt(ResolutionHeightPref, option.Height);
        ApplyResolution(option);
    }

    /// <summary>Applies and persists the frame-rate cap.</summary>
    public static void SetTargetFrameRate(int frameRate)
    {
        PlayerPrefs.SetInt(TargetFrameRatePref, frameRate);
        ApplyTargetFrameRate(frameRate);
    }

    /// <summary>Pushes the resolution to the screen using the saved window mode.</summary>
    private static void ApplyResolution(VideoSettingOptions.ResolutionOption option)
        => Screen.SetResolution(option.Width, option.Height, FullScreenMode);

    /// <summary>Applies the frame-rate cap. VSync stays disabled so the cap is strictly respected.</summary>
    private static void ApplyTargetFrameRate(int frameRate)
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = frameRate;
    }

    /// <summary>Restores the saved video settings (quality, resolution, frame rate). Call once at startup.</summary>
    public static void ApplyAtStartup()
    {
        if (PlayerPrefs.HasKey(QualityPref))
            QualitySettings.SetQualityLevel(PlayerPrefs.GetInt(QualityPref), true);

        // Switch to the saved window mode first so mismatched-aspect resolutions
        // are letterboxed by borderless fullscreen instead of being stretched.
        if (Screen.fullScreenMode != FullScreenMode)
            Screen.fullScreenMode = FullScreenMode;

        if (SavedResolution is VideoSettingOptions.ResolutionOption saved)
            ApplyResolution(saved);

        ApplyTargetFrameRate(TargetFrameRate);
    }
}
