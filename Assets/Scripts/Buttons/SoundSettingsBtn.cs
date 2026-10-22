using UnityEngine;

/// <summary>
/// Pause-menu button that opens the sound settings sub-page. Hides the main
/// pause-menu button block while the sub-page is open; the sub-page's Back
/// button restores it. Attach to the "Sound Settings" button in the pause menu.
/// </summary>
public class SoundSettingsBtn : BaseButton
{
    [Tooltip("Root of the sound settings sub-page to show.")]
    [SerializeField] private GameObject _soundSettingsPanel;

    [Tooltip("Main pause-menu button block (all buttons container), hidden while the sub-page is open.")]
    [SerializeField] private GameObject _pauseMenuRoot;

    protected override void OnClick()
    {
        if (_soundSettingsPanel == null)
        {
            Debug.LogWarning("SoundSettingsBtn: Sound settings panel is not assigned.", this);
            return;
        }

        _soundSettingsPanel.SetActive(true);
        if (_pauseMenuRoot != null) _pauseMenuRoot.SetActive(false);
    }

    private void OnEnable()
    {
        // ESC closed the whole pause menu while the sub-page was open —
        // reset to the main button block for the next time the menu opens.
        if (_soundSettingsPanel != null && _soundSettingsPanel.activeSelf)
        {
            _soundSettingsPanel.SetActive(false);
            if (_pauseMenuRoot != null) _pauseMenuRoot.SetActive(true);
        }
    }
}
