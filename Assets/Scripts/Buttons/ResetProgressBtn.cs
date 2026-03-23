using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pause-menu button that wipes all save data and restarts the game from scratch.
/// Destroys DontDestroyOnLoad singletons so they reinitialize cleanly after scene reload.
/// </summary>
public class ResetProgressBtn : BaseButton
{
    protected override void OnClick()
    {
        SaveManager.Instance?.DeleteSave();
        SaveManager.Instance?.ClearRegistry();

        // Destroy persistent singletons so the reloaded scene creates fresh instances
        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        if (SaveManager.Instance != null) Destroy(SaveManager.Instance.gameObject);

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
