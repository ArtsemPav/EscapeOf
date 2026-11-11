using System;
using UnityEngine;

/// <summary>
/// Tooltip key fields for each device in the Chemical Synthesis puzzle.
/// Declared as a serializable class so Unity draws it as a foldout in the Inspector.
/// Text lives in the localization tables under puzzle.synthesis.*; each field holds a
/// localization key override — when cleared, the default key from <see cref="LocalizationKeys"/> is used.
/// </summary>
[Serializable]
public class DeviceTooltipInfo
{
    [Header("Centrifuge")]
    [Tooltip("Localization key for the title. Empty — use the default puzzle.synthesis.centrifuge_title.")]
    public string centrifugeTitleKey = LocalizationKeys.SynthesisCentrifugeTitle;
    [Tooltip("Localization key for the description. Empty — use the default puzzle.synthesis.centrifuge_desc.")]
    public string centrifugeDescriptionKey = LocalizationKeys.SynthesisCentrifugeDesc;

    [Header("Burner")]
    [Tooltip("Localization key for the title. Empty — use the default puzzle.synthesis.burner_title.")]
    public string burnerTitleKey = LocalizationKeys.SynthesisBurnerTitle;
    [Tooltip("Localization key for the description. Empty — use the default puzzle.synthesis.burner_desc.")]
    public string burnerDescriptionKey = LocalizationKeys.SynthesisBurnerDesc;

    [Header("Mixer")]
    [Tooltip("Localization key for the title. Empty — use the default puzzle.synthesis.mixer_title.")]
    public string mixerTitleKey = LocalizationKeys.SynthesisMixerTitle;
    [Tooltip("Localization key for the description. Empty — use the default puzzle.synthesis.mixer_desc.")]
    public string mixerDescriptionKey = LocalizationKeys.SynthesisMixerDesc;

    [Header("Analyzer")]
    [Tooltip("Localization key for the title. Empty — use the default puzzle.synthesis.analyzer_title.")]
    public string analyzerTitleKey = LocalizationKeys.SynthesisAnalyzerTitle;
    [Tooltip("Localization key for the description. Empty — use the default puzzle.synthesis.analyzer_desc.")]
    public string analyzerDescriptionKey = LocalizationKeys.SynthesisAnalyzerDesc;

    [Header("Trash")]
    [Tooltip("Localization key for the title. Empty — use the default puzzle.synthesis.trash_title.")]
    public string trashTitleKey = LocalizationKeys.SynthesisTrashTitle;
    [Tooltip("Localization key for the description. Empty — use the default puzzle.synthesis.trash_desc.")]
    public string trashDescriptionKey = LocalizationKeys.SynthesisTrashDesc;

    /// <summary>Resolves a tooltip title: localized text of the stored key.</summary>
    public string ResolveTitle(string titleKey) => Localization.Get(titleKey);

    /// <summary>Resolves a tooltip description: localized text of the stored key.</summary>
    public string ResolveDescription(string descriptionKey) => Localization.Get(descriptionKey);
}
