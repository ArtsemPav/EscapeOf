using UnityEngine;

/// <summary>
/// Contract implemented by anything that registers itself in <see cref="LightingSystem"/>
/// as a controllable lamp (one zone entry with on/off, fade and flicker support).
/// </summary>
public interface ILampZone
{
    /// <summary>Zone name this lamp belongs to (matches LightSwitch.ZoneId). Case-sensitive.</summary>
    string ZoneId { get; }

    /// <summary>True when the lamp is logically on (after SetActive(true)).</summary>
    bool IsOn { get; }

    /// <summary>Turns the lamp (and any lights it controls) on or off, managing flicker accordingly.</summary>
    void SetActive(bool on);

    /// <summary>
    /// Sets intensity of all controlled lights as a multiplier of their original intensities
    /// (used during fade transitions). Does NOT start/stop flicker.
    /// </summary>
    void SetIntensityMultiplier(float multiplier);

    /// <summary>Stops any active flicker coroutine and restores original intensities.</summary>
    void StopFlicker();

    /// <summary>Main light intensity as authored in the scene/prefab (max across controlled lights).</summary>
    float OriginalIntensity { get; }
}
