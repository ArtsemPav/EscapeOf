using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pause-menu debug button that reloads the scene from the debug save slot.
/// Sets a static pending-load flag so the freshly created SaveManager loads
/// from DebugSlot instead of the default slot, then destroys ALL persistent
/// singletons and reloads the scene so everything reinitializes cleanly.
/// </summary>
public class LoadSaveBtn : BaseButton
{
    protected override void OnClick()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave(SaveManager.DebugSlot))
        {
            Debug.LogWarning("LoadSaveBtn: No debug save file found — nothing to load.");
            return;
        }

        // Tell the next SaveManager instance to load from the debug slot.
        SaveManager.RequestLoadFromSlot(SaveManager.DebugSlot);

        // Destroy ALL DontDestroyOnLoad singletons so the reloaded scene
        // creates fresh instances with no stale references or broken state.
        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        if (SaveManager.Instance != null) Destroy(SaveManager.Instance.gameObject);
        if (AudioManager.Instance != null) Destroy(AudioManager.Instance.gameObject);
        if (InputManager.Instance != null) Destroy(InputManager.Instance.gameObject);
        if (ResolutionManager.Instance != null) Destroy(ResolutionManager.Instance.gameObject);
        if (PopupMessageSystem.Instance != null) Destroy(PopupMessageSystem.Instance.gameObject);

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
