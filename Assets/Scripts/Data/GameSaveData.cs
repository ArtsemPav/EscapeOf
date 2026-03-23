using System;
using System.Collections.Generic;

/// <summary>Root serializable structure written to each save file slot.</summary>
[Serializable]
public class GameSaveData
{
    public int version = 1;
    public string timestamp;

    /// <summary>One entry per ISaveable object: its stable ID and serialized JSON blob.</summary>
    public List<EntityRecord> entities = new List<EntityRecord>();
}

/// <summary>Save data blob for a single ISaveable object.</summary>
[Serializable]
public class EntityRecord
{
    public string id;
    public string data;
}
