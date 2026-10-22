using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main-menu settings panel: music volume, SFX volume, resolution, frame rate
/// and a back action. Values apply immediately and persist in PlayerPrefs.
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider _musicSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Button _resolutionButton;
    [SerializeField] private TMP_Text _resolutionLabel;
    [SerializeField] private Button _frameRateButton;
    [SerializeField] private TMP_Text _frameRateLabel;

    private int _resolutionIndex;

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
        // Cycle buttons (resolution/fps) are wired via persistent onClick
        // calls in the scene — do NOT add code listeners too, or every click
        // fires twice and two-step cycles return to the same value.
        RefreshResolutionLabel();
        RefreshFrameRateLabel();
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
        _resolutionIndex = Mathf.Clamp(index, 0, VideoSettingOptions.Resolutions.Count - 1);
        SystemSettings.SetResolutionIndex(_resolutionIndex);
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

    private void RefreshResolutionLabel()
    {
        if (_resolutionLabel == null) return;

        _resolutionIndex = SystemSettings.ResolutionIndex;
        if (_resolutionIndex < 0)
        {
            _resolutionIndex = VideoSettingOptions.IndexOfResolution(VideoSettingOptions.DefaultResolution);
            if (_resolutionIndex >= 0) SystemSettings.SetResolutionIndex(_resolutionIndex);
        }
        if (_resolutionIndex >= 0)
            _resolutionLabel.text = "РАЗРЕШЕНИЕ: " + VideoSettingOptions.Resolutions[_resolutionIndex].ToString();
    }

    private void RefreshFrameRateLabel()
    {
        if (_frameRateLabel != null)
            _frameRateLabel.text = "КАДРЫ В СЕКУНДУ: " + Application.targetFrameRate;
    }
}
