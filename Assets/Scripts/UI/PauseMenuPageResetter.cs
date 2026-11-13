using UnityEngine;

/// <summary>
/// Resets the pause menu to its main button block every time the pause menu
/// opens. ESC can close the whole menu while a sub-page (e.g. sound settings)
/// is open, and the sub-pages' activeSelf flags persist across the menu root
/// being deactivated — without this reset the next menu open would land the
/// player back in the sub-page instead of the main button block.
/// Attach to the root of the pause menu (the GameManager menuUI panel).
/// </summary>
public class PauseMenuPageResetter : MonoBehaviour
{
    [Tooltip("Main pause-menu button block, shown every time the menu opens.")]
    [SerializeField] private GameObject _pauseMenuRoot;

    [Tooltip("Sub-pages hidden every time the menu opens (e.g. the sound settings panel).")]
    [SerializeField] private GameObject[] _subPages;

    private void OnEnable()
    {
        if (_pauseMenuRoot != null) _pauseMenuRoot.SetActive(true);

        if (_subPages == null) return;
        foreach (GameObject subPage in _subPages)
        {
            if (subPage != null) subPage.SetActive(false);
        }
    }
}
