using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main-menu settings panel: music volume, SFX volume, mouse sensitivity,
/// resolution, frame rate, window mode and a back action. Values apply
/// immediately and persist in PlayerPrefs.
/// The resolution list is display-driven: options are the current display's
/// supported modes at the display's aspect ratio, so fullscreen never stretches.
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider _musicSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private TMP_Text _musicLabel;
    [SerializeField] private TMP_Text _sfxLabel;
    [Tooltip("Mouse-look sensitivity multiplier slider (0.1–2.0).")]
    [SerializeField] private Slider _sensitivitySlider;
    [SerializeField] private TMP_Text _sensitivityLabel;
    [Tooltip("Optional numeric readout (e.g. \"1.25\") shown next to the sensitivity slider.")]
    [SerializeField] private TMP_Text _sensitivityValueLabel;
    [Tooltip("Optional button that resets all settings (volumes, mouse sensitivity, resolution, frame rate, window mode) to their defaults.")]
    [SerializeField] private Button _resetButton;
    [Tooltip("When false, the reset button clears only audio volumes and mouse sensitivity — for panels without the video block (e.g. the pause menu), so video settings are never changed invisibly.")]
    [SerializeField] private bool _resetIncludesVideoSettings = true;
    [SerializeField] private Button _resolutionButton;
    [SerializeField] private TMP_Text _resolutionLabel;
    [SerializeField] private Button _frameRateButton;
    [SerializeField] private TMP_Text _frameRateLabel;
    [SerializeField] private Button _windowModeButton;
    [SerializeField] private TMP_Text _windowModeLabel;
    [Header("Language")]
    [Tooltip("Optional button that cycles the UI language.")]
    [SerializeField] private Button _languageButton;
    [SerializeField] private TMP_Text _languageLabel;

    private int _resolutionIndex;
    private bool _initialized;

    private void Awake() => SystemSettings.ApplyAtStartup();

    private void OnEnable()
    {
        if (_musicLabel != null && string.IsNullOrEmpty(_musicBaseText)) _musicBaseText = Localization.Get("ui.settings.music_volume");
        if (_sfxLabel != null && string.IsNullOrEmpty(_sfxBaseText)) _sfxBaseText = Localization.Get("ui.settings.sfx_volume");
        if (_musicSlider != null)
        {
            _musicSlider.SetValueWithoutNotify(SystemSettings.MusicVolume);
            _musicSlider.onValueChanged.AddListener(SystemSettings.SetMusicVolume);
            _musicSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
        }
        if (_sfxSlider != null)
        {
            _sfxSlider.SetValueWithoutNotify(SystemSettings.SfxVolume);
            _sfxSlider.onValueChanged.AddListener(SystemSettings.SetSfxVolume);
            _sfxSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
        }
        if (_sensitivityLabel != null && string.IsNullOrEmpty(_sensitivityBaseText))
            _sensitivityBaseText = Localization.Get("ui.settings.sensitivity");
        if (_sensitivitySlider != null)
        {
            _sensitivitySlider.SetValueWithoutNotify(SystemSettings.MouseSensitivity);
            _sensitivitySlider.onValueChanged.AddListener(SystemSettings.SetMouseSensitivity);
            _sensitivitySlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
        }
        if (_resetButton != null)
            _resetButton.onClick.AddListener(ResetToDefaults);
        if (_languageButton != null)
            _languageButton.onClick.AddListener(CycleLanguage);
        LocalizationSettings.LanguageChanged += OnLanguageChanged;
        RefreshVolumeLabels();
        // Cycle buttons (resolution/fps/window mode) are wired via persistent onClick
        // calls in the scene — do NOT add code listeners too, or every click
        // fires twice and two-step cycles return to the same value.
        RefreshResolutionLabel();
        RefreshFrameRateLabel();
        RefreshWindowModeLabel();
        RefreshLanguageLabel();
    }

    private void OnDisable()
    {
        if (_musicSlider != null) _musicSlider.onValueChanged.RemoveListener(SystemSettings.SetMusicVolume);
        if (_sfxSlider != null) _sfxSlider.onValueChanged.RemoveListener(SystemSettings.SetSfxVolume);
        if (_sensitivitySlider != null) _sensitivitySlider.onValueChanged.RemoveListener(SystemSettings.SetMouseSensitivity);
        if (_resetButton != null) _resetButton.onClick.RemoveListener(ResetToDefaults);
        if (_languageButton != null) _languageButton.onClick.RemoveListener(CycleLanguage);
        LocalizationSettings.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>Refreshes localized labels when the language changes at runtime.</summary>
    private void OnLanguageChanged()
    {
        // Base texts captured from the scene are in the old language; drop them so
        // the next refresh uses the localized volume label templates instead.
        _musicBaseText = _musicLabel != null ? Localization.Get("ui.settings.music_volume") : null;
        _sfxBaseText = _sfxLabel != null ? Localization.Get("ui.settings.sfx_volume") : null;
        _sensitivityBaseText = _sensitivityLabel != null ? Localization.Get("ui.settings.sensitivity") : null;
        RefreshVolumeLabels();
        RefreshWindowModeLabel();
        RefreshResolutionLabel();
        RefreshFrameRateLabel();
        RefreshLanguageLabel();
    }

    /// <summary>
    /// Resets the settings shown in this panel to their defaults: volumes and
    /// mouse sensitivity always; resolution, frame rate and window mode only
    /// when the panel displays the video block (<see cref="_resetIncludesVideoSettings"/>).
    /// </summary>
    public void ResetToDefaults()
    {
        bool includeVideo = _resetIncludesVideoSettings;
        SystemSettings.ResetToDefaults(includeVideo);
        if (_musicSlider != null) _musicSlider.SetValueWithoutNotify(SystemSettings.MusicVolume);
        if (_sfxSlider != null) _sfxSlider.SetValueWithoutNotify(SystemSettings.SfxVolume);
        if (_sensitivitySlider != null) _sensitivitySlider.SetValueWithoutNotify(SystemSettings.MouseSensitivity);
        RefreshVolumeLabels();
        if (includeVideo)
        {
            _initialized = true;
            VideoSettingOptions.ResolutionOption defaultResolution = VideoSettingOptions.DefaultResolution;
            _resolutionIndex = Mathf.Max(0, VideoSettingOptions.IndexOfResolution(defaultResolution));
            RefreshResolutionLabel();
            RefreshFrameRateLabel();
            RefreshWindowModeLabel();
        }
    }

    /// <summary>Marks untouched labels with a "(default)" hint so the player sees which values are defaults.</summary>
    private void RefreshVolumeLabels()
    {
        string suffix = Localization.Get("ui.settings.default_suffix");
        if (_musicLabel != null)
            _musicLabel.text = (SystemSettings.IsMusicVolumeDefault ? _musicBaseText + suffix : _musicBaseText);
        if (_sfxLabel != null)
            _sfxLabel.text = (SystemSettings.IsSfxVolumeDefault ? _sfxBaseText + suffix : _sfxBaseText);
        if (_sensitivityLabel != null)
            _sensitivityLabel.text = (SystemSettings.IsMouseSensitivityDefault
                ? _sensitivityBaseText + suffix
                : _sensitivityBaseText);
        if (_sensitivityValueLabel != null)
            _sensitivityValueLabel.text = SystemSettings.MouseSensitivity.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);
    }
    private string _musicBaseText;
    private string _sfxBaseText;
    private string _sensitivityBaseText;

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
            UnityEngine.FullScreenMode.FullScreenWindow => Localization.Get("ui.settings.window.borderless"),
            UnityEngine.FullScreenMode.Windowed => Localization.Get("ui.settings.window.windowed"),
            _ => Localization.Get("ui.settings.window") + " " + SystemSettings.FullScreenMode,
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

        _resolutionLabel.text = Localization.Get("ui.settings.resolution") + " " + resolutions[_resolutionIndex].ToString();
    }

    private void RefreshFrameRateLabel()
    {
        if (_frameRateLabel != null)
            _frameRateLabel.text = Localization.Get("ui.settings.framerate") + " " + Application.targetFrameRate;
    }

    /// <summary>Switches to the next available language and refreshes all labels immediately.</summary>
    public void CycleLanguage()
    {
        Language[] languages = (Language[])Enum.GetValues(typeof(Language));
        int current = Array.IndexOf(languages, LocalizationSettings.Current);
        LocalizationSettings.SetLanguage(languages[(current + 1) % languages.Length]);
        RefreshLanguageLabel();
    }

    private void RefreshLanguageLabel()
    {
        if (_languageLabel != null)
            _languageLabel.text = Localization.Get(LocalizationKeys.SettingsLanguage) + " " + LocalizationSettings.Current.DisplayName();
    }
}
