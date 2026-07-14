using System;
using UnityEngine;

/// <summary>
/// Tooltip text fields for each device in the Chemical Synthesis puzzle.
/// Declared as a serializable class so Unity draws it as a foldout in the Inspector.
/// Populate the title/description pairs with device-specific text.
/// </summary>
[Serializable]
public class DeviceTooltipInfo
{
    [Header("Centrifuge")]
    [TextArea(1, 2)] public string centrifugeTitle;
    [TextArea(2, 5)] public string centrifugeDescription;

    [Header("Burner")]
    [TextArea(1, 2)] public string burnerTitle;
    [TextArea(2, 5)] public string burnerDescription;

    [Header("Mixer")]
    [TextArea(1, 2)] public string mixerTitle;
    [TextArea(2, 5)] public string mixerDescription;

    [Header("Analyzer")]
    [TextArea(1, 2)] public string analyzerTitle;
    [TextArea(2, 5)] public string analyzerDescription;

    [Header("Trash")]
    [TextArea(1, 2)] public string trashTitle;
    [TextArea(2, 5)] public string trashDescription;
}
