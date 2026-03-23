/// <summary>
/// Implemented by any MonoBehaviour whose state must be persisted across sessions.
/// Register with SaveManager in Awake(), unregister in OnDestroy().
/// SaveManager distributes loaded data to all registered saveables before other Start() calls.
/// </summary>
public interface ISaveable
{
    /// <summary>Stable unique identifier. Never change after assigning — it's how save data is matched on load.</summary>
    string SaveId { get; }

    /// <summary>Serialize current state to a JSON string.</summary>
    string GetSaveData();

    /// <summary>Restore state from a previously saved JSON string.</summary>
    void LoadSaveData(string json);
}
