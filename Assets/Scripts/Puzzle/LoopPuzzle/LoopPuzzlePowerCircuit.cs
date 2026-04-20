using System;
using UnityEngine;

[Serializable]
public struct SwitchRequirement
{
    [Tooltip("Index of the switch (0 = S1, 5 = S6 Master).")]
    public int switchIndex;
    [Tooltip("State the switch must be in for this requirement to pass.")]
    public bool mustBeOn;
}

[Serializable]
public struct SpotlightActivationRule
{
    [Tooltip("ALL requirements must match (AND logic). Empty = always active.")]
    public SwitchRequirement[] requirements;
}

[Serializable]
public struct SpotlightPowerConfig
{
    public PaintingSpotlight spotlight;
    [Tooltip("At least ONE rule must match (OR logic). S6 master overrides all.")]
    public SpotlightActivationRule[] activationRules;
}

/// <summary>
/// Evaluates which spotlights (L1–L4) are powered based on button states (S1–S6).
/// S6 (last element) is the master switch: if off, all spotlights are forced off.
/// Rules use OR-of-AND logic: configurable per spotlight in the Inspector.
/// </summary>
public class LoopPuzzlePowerCircuit : MonoBehaviour
{
    [Header("Switches (S1 at index 0, S6 Master at last index)")]
    [SerializeField] private LoopPuzzleButton[] _switches;

    [Header("Spotlight Rules")]
    [SerializeField] private SpotlightPowerConfig[] _spotlightConfigs;

    /// <summary>Raised whenever any spotlight power state changes.</summary>
    public event Action OnPowerChanged;

    private void Awake()
    {
        foreach (var button in _switches)
            if (button != null)
                button.OnToggled += _ => EvaluateAndApply();
    }

    private void Start() => EvaluateAndApply();

    /// <summary>Returns the current on/off state of switch at the given index.</summary>
    public bool GetSwitchState(int index)
    {
        if (index < 0 || index >= _switches.Length) return false;
        return _switches[index] != null && _switches[index].IsActive;
    }

    /// <summary>Restores all switch states from saved data without firing events.</summary>
    public void RestoreSwitchStates(bool[] states)
    {
        for (int i = 0; i < _switches.Length && i < states.Length; i++)
            _switches[i]?.SetStateSilent(states[i]);
        EvaluateAndApply();
    }

    /// <summary>Returns current states of all switches for serialization.</summary>
    public bool[] GetAllSwitchStates()
    {
        var states = new bool[_switches.Length];
        for (int i = 0; i < _switches.Length; i++)
            states[i] = _switches[i] != null && _switches[i].IsActive;
        return states;
    }

    /// <summary>Recomputes and applies spotlight power based on current switch states.</summary>
    public void EvaluateAndApply()
    {
        bool masterOn = GetSwitchState(_switches.Length - 1);

        foreach (var config in _spotlightConfigs)
        {
            if (config.spotlight == null) continue;
            bool powered = masterOn && EvaluateSpotlight(config);
            config.spotlight.SetPowered(powered);
        }

        OnPowerChanged?.Invoke();
    }

    private bool EvaluateSpotlight(SpotlightPowerConfig config)
    {
        if (config.activationRules == null || config.activationRules.Length == 0) return true;
        foreach (var rule in config.activationRules)
            if (EvaluateRule(rule)) return true; // OR logic
        return false;
    }

    private bool EvaluateRule(SpotlightActivationRule rule)
    {
        if (rule.requirements == null || rule.requirements.Length == 0) return true;
        foreach (var req in rule.requirements)
            if (GetSwitchState(req.switchIndex) != req.mustBeOn) return false; // AND logic
        return true;
    }
}
