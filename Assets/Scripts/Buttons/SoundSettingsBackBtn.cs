using UnityEngine;

/// <summary>
/// Back button of the sound settings sub-page in the pause menu: closes the
/// sub-page and shows the main pause-menu block again.
/// </summary>
public class SoundSettingsBackBtn : BaseButton
{
    [Tooltip("Root of the sound settings sub-page to hide.")]
    [SerializeField] private GameObject _soundSettingsPanel;

    [Tooltip("Main pause-menu button block (all buttons container) to show again.")]
    [SerializeField] private GameObject _pauseMenuRoot;

    protected override void OnClick()
    {
        if (_soundSettingsPanel != null) _soundSettingsPanel.SetActive(false);
        if (_pauseMenuRoot != null) _pauseMenuRoot.SetActive(true);
    }
}
