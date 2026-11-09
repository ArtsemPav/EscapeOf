using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Milestones of player progress that change the loading screen art.</summary>
public enum BuildingProgressStage
{
    /// <summary>Power has not been restored yet — dark building.</summary>
    NoPower = 0,
    /// <summary>The electric puzzle has been solved — the building has power.</summary>
    PowerRestored = 1
}

/// <summary>
/// Art set for loading screens, driven by the player's progress milestones.
/// Adding a new loading screen image means adding a BuildingProgressStage
/// value and one entry here — no code changes in the scenes that use it.
/// </summary>
[CreateAssetMenu(menuName = "Escape/Loading Screen Theme", fileName = "LoadingScreenTheme")]
public class LoadingScreenTheme : ScriptableObject
{
    /// <summary>One art entry for a progress stage.</summary>
    [Serializable]
    public class StageArt
    {
        public BuildingProgressStage stage;
        public Sprite sprite;
    }

    [Tooltip("Art per progress stage. The entry with the highest stage not exceeding the player's progress is used.")]
    [SerializeField] private List<StageArt> _stageArt = new List<StageArt>();

    [Tooltip("Fallback when no stage entry matches (e.g. an empty list).")]
    [SerializeField] private Sprite _fallback;

    /// <summary>Returns the background art for the given progress stage.</summary>
    public Sprite GetBackground(BuildingProgressStage stage)
    {
        Sprite best = _fallback;
        int bestStage = int.MinValue;        foreach (StageArt entry in _stageArt)
        {
            if (entry == null || entry.sprite == null) continue;
            int entryStage = (int)entry.stage;
            if (entryStage > (int)stage || entryStage <= bestStage) continue;
            bestStage = entryStage;
            best = entry.sprite;
        }
        return best;
    }
}
