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
    public const string ResolutionIndexPref = "Settings_ResolutionIndex";
    public const string TargetFrameRatePref = "Settings_TargetFrameRate";

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

    /// <summary>Index of the saved resolution in <see cref="VideoSettingOptions.Resolutions"/>, or -1 when the player has not chosen one yet.</summary>
    public static int ResolutionIndex => PlayerPrefs.GetInt(ResolutionIndexPref, -1);

    /// <summary>The saved target frame rate, or <see cref="VideoSettingOptions.DefaultFrameRate"/> when unset.</summary>
    public static int TargetFrameRate => PlayerPrefs.GetInt(TargetFrameRatePref, VideoSettingOptions.DefaultFrameRate);

    /// <summary>Applies and persists the resolution at the given curated-list index. Does nothing on an invalid index.</summary>
    public static void SetResolutionIndex(int index)
    {
        if (index < 0 || index >= VideoSettingOptions.Resolutions.Count) return;

        VideoSettingOptions.ResolutionOption option = VideoSettingOptions.Resolutions[index];
        PlayerPrefs.SetInt(ResolutionIndexPref, index);
        ApplyResolution(option);
    }

    /// <summary>Applies and persists the frame-rate cap.</summary>
    public static void SetTargetFrameRate(int frameRate)
    {
        PlayerPrefs.SetInt(TargetFrameRatePref, frameRate);
        ApplyTargetFrameRate(frameRate);
    }

    /// <summary>Pushes the resolution to the screen, keeping the current fullscreen mode.</summary>
    private static void ApplyResolution(VideoSettingOptions.ResolutionOption option)
        => Screen.SetResolution(option.Width, option.Height, Screen.fullScreenMode);

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

        int resolutionIndex = ResolutionIndex;
        if (resolutionIndex >= 0 && resolutionIndex < VideoSettingOptions.Resolutions.Count)
            ApplyResolution(VideoSettingOptions.Resolutions[resolutionIndex]);

        ApplyTargetFrameRate(TargetFrameRate);
    }
}
