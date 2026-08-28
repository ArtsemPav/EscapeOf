using UnityEngine;

/// <summary>
/// Pause-menu debug button that performs an immediate synchronous save
/// to a dedicated debug slot (SaveManager.DebugSlot).
/// </summary>
public class SaveBtn : BaseButton
{
    protected override void OnClick()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogError("SaveBtn: SaveManager.Instance is null.");
            return;
        }

        SaveManager.Instance.SaveImmediate(SaveManager.DebugSlot);
        Debug.Log($"[SaveBtn] Debug save written to slot {SaveManager.DebugSlot}.");
    }
}
