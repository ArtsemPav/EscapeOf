using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main-menu settings panel: music volume, SFX volume, graphics quality and a
/// back action. Values apply immediately and persist in PlayerPrefs.
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider _musicSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Button _qualityButton;
    [SerializeField] private TMP_Text _qualityLabel;

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
        if (_qualityButton != null) _qualityButton.onClick.AddListener(CycleQuality);
        RefreshQualityLabel();
    }

    private void OnDisable()
    {
        if (_musicSlider != null) _musicSlider.onValueChanged.RemoveListener(SystemSettings.SetMusicVolume);
        if (_sfxSlider != null) _sfxSlider.onValueChanged.RemoveListener(SystemSettings.SetSfxVolume);
        if (_qualityButton != null) _qualityButton.onClick.RemoveListener(CycleQuality);
    }

    /// <summary>Switches to the next graphics quality level and refreshes the label.</summary>
    public void CycleQuality()
    {
        int next = (QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length;
        SystemSettings.SetQualityLevel(next);
        RefreshQualityLabel();
    }

    private void RefreshQualityLabel()
    {
        if (_qualityLabel != null)
            _qualityLabel.text = "КАЧЕСТВО: " + QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpper();
    }
}
