using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main-menu settings panel: music volume, SFX volume, resolution, frame rate
/// and a back action. Values apply immediately and persist in PlayerPrefs.
/// The resolution list is display-driven: options are the current display's
/// supported modes at the display's aspect ratio, so fullscreen never stretches.
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider _musicSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Button _resolutionButton;
    [SerializeField] private TMP_Text _resolutionLabel;
    [SerializeField] private Button _frameRateButton;
    [SerializeField] private TMP_Text _frameRateLabel;
    [SerializeField] private Button _windowModeButton;
    [SerializeField] private TMP_Text _windowModeLabel;

    private int _resolutionIndex;
    private bool _initialized;

    private void Awake() => SystemSettings.ApplyAtStartup();

    private void OnEnable()
    {
        if (_musicSlider != null)
        {
            _musicSlider.SetValueWithoutNotify(SystemSettings.MusicVolume);
            _musicSlider.onValueChanged.AddListener(SystemSettings.SetMusicVolume);
        }
        if (_sfxSlider != null)
        {
            _sfxSlider.SetValueWithoutNotify(SystemSettings.SfxVolume);
            _sfxSlider.onValueChanged.AddListener(SystemSettings.SetSfxVolume);
        }
        // Cycle buttons (resolution/fps/window mode) are wired via persistent onClick
        // calls in the scene — do NOT add code listeners too, or every click
        // fires twice and two-step cycles return to the same value.
        RefreshResolutionLabel();
        RefreshFrameRateLabel();
        RefreshWindowModeLabel();
    }

    private void OnDisable()
    {
        if (_musicSlider != null) _musicSlider.onValueChanged.RemoveListener(SystemSettings.SetMusicVolume);
        if (_sfxSlider != null) _sfxSlider.onValueChanged.RemoveListener(SystemSettings.SetSfxVolume);
    }

    /// <summary>Switches to the next selectable resolution and applies it immediately.</summary>
    public void CycleResolution()
    {
        int count = VideoSettingOptions.Resolutions.Count;
        int next = (_resolutionIndex + 1) % count;
        SetResolution(next);
    }

    /// <summary>Applies the resolution at the given index and refreshes the label.</summary>
    public void SetResolution(int index)
    {
        var resolutions = VideoSettingOptions.Resolutions;
        _resolutionIndex = Mathf.Clamp(index, 0, resolutions.Count - 1);
        SystemSettings.SetResolution(resolutions[_resolutionIndex]);
        RefreshResolutionLabel();
    }

    /// <summary>Switches between the selectable frame-rate caps and applies the choice immediately.</summary>
    public void CycleFrameRate()
    {
        int[] rates = new int[VideoSettingOptions.FrameRates.Count];
        for (int i = 0; i < rates.Length; i++) rates[i] = VideoSettingOptions.FrameRates[i];
        int current = Application.targetFrameRate;
        int position = Array.IndexOf(rates, current);
        int next = rates[(position + 1) % rates.Length];
        SystemSettings.SetTargetFrameRate(next);
        RefreshFrameRateLabel();
    }

    /// <summary>
    /// Cycles the window mode (borderless fullscreen / windowed) and applies it immediately.
    /// Exclusive fullscreen is deliberately not offered: the monitor driver stretches
    /// resolutions whose aspect ratio differs from the display's, while borderless
    /// fullscreen letterboxes them with black bars (see FullScreenMode docs).
    /// </summary>
    public void CycleWindowMode()
    {
        UnityEngine.FullScreenMode[] modes = new UnityEngine.FullScreenMode[]
        {
            UnityEngine.FullScreenMode.FullScreenWindow,
            UnityEngine.FullScreenMode.Windowed,
        };
        UnityEngine.FullScreenMode current = SystemSettings.FullScreenMode;
        int position = Array.IndexOf(modes, current);
        UnityEngine.FullScreenMode next = modes[(position + 1) % modes.Length];
        SystemSettings.SetFullScreenMode(next);
        RefreshWindowModeLabel();
    }

    private void RefreshWindowModeLabel()
    {
        if (_windowModeLabel == null) return;

        string label = SystemSettings.FullScreenMode switch
        {
            UnityEngine.FullScreenMode.FullScreenWindow => "ОКНО: БЕЗ РАМКИ (ПОЛНЫЙ ЭКРАН)",
            UnityEngine.FullScreenMode.Windowed => "ОКНО: ОКОННЫЙ РЕЖИМ",
            _ => "ОКНО: " + SystemSettings.FullScreenMode,
        };
        _windowModeLabel.text = label;
    }

    /// <summary>Shows the saved (or default) resolution; initializes on first use.</summary>
    private void RefreshResolutionLabel()
    {
        if (_resolutionLabel == null) return;

        IReadOnlyList<VideoSettingOptions.ResolutionOption> resolutions = VideoSettingOptions.Resolutions;
        if (!_initialized)
        {
            _initialized = true;
            VideoSettingOptions.ResolutionOption saved = SystemSettings.SavedResolution
                ?? VideoSettingOptions.DefaultResolution;
            _resolutionIndex = Mathf.Max(0, VideoSettingOptions.IndexOfResolution(saved));
        }

        if (_resolutionIndex >= resolutions.Count)
            _resolutionIndex = Mathf.Max(0, resolutions.Count - 1);

        _resolutionLabel.text = "РАЗРЕШЕНИЕ: " + resolutions[_resolutionIndex].ToString();
    }

    private void RefreshFrameRateLabel()
    {
        if (_frameRateLabel != null)
            _frameRateLabel.text = "КАДРЫ В СЕКУНДУ: " + Application.targetFrameRate;
    }
}
