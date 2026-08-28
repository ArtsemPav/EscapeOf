using UnityEngine;

/// <summary>
/// Pause-menu debug button that deletes the debug save file and its backups.
/// Does not reload the scene — the current game session continues as-is.
/// </summary>
public class DeleteDebugSaveBtn : BaseButton
{
    protected override void OnClick()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogError("DeleteDebugSaveBtn: SaveManager.Instance is null.");
            return;
        }

        if (!SaveManager.Instance.HasSave(SaveManager.DebugSlot))
        {
            Debug.Log("[DeleteDebugSaveBtn] No debug save file to delete.");
            return;
        }

        SaveManager.Instance.DeleteSave(SaveManager.DebugSlot);
        Debug.Log($"[DeleteDebugSaveBtn] Debug save slot {SaveManager.DebugSlot} deleted.");
    }
}
