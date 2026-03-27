using UnityEngine;

/// <summary>
/// Describes the light properties for one flashlight state (on or off).
/// </summary>
[System.Serializable]
public class FlashlightState
{
    public float intensity = 0f;
    public float range     = 20f;
    public float spotAngle = 60f;
    public Color color     = Color.white;
}

/// <summary>
/// Identifies a flashlight filter mode. Add new values here to introduce new lens types.
/// </summary>
public enum FlashlightMode
{
    Normal,
    Blue,
    Red,
    UV
}

/// <summary>
/// Describes a single switchable flashlight mode: which inventory item unlocks it
/// and what light properties it uses when on.
/// </summary>
[System.Serializable]
public class FlashlightModeConfig
{
    [Tooltip("The mode identifier used by HiddenWallSign and other systems.")]
    public FlashlightMode mode = FlashlightMode.Normal;

    [Tooltip("Inventory condition that must be met to unlock this mode (e.g. Blue Lens in inventory). " +
             "Leave null for the default Normal mode which is always available.")]
    public InventoryCondition requiredItem;

    [Tooltip("Light properties used when the flashlight is ON in this mode.")]
    public FlashlightState onState;
}

/// <summary>
/// ScriptableObject that defines all configurable parameters of the flashlight:
/// the inventory condition required to use it, the list of switchable modes,
/// the base off state, and the transition speed.
/// </summary>
[CreateAssetMenu(menuName = "Game/Flashlight Config")]
public class FlashlightConfig : ScriptableObject
{
    [Header("Operating Condition")]
    [Tooltip("Inventory condition that must be met for the flashlight to work at all.")]
    public InventoryCondition operatingCondition;

    [Header("Modes")]
    [Tooltip("List of available modes in cycle order. The first entry is used as the default (Normal). " +
             "Modes with an unmet requiredItem are skipped during cycling.")]
    public FlashlightModeConfig[] modes = new FlashlightModeConfig[]
    {
        new FlashlightModeConfig
        {
            mode    = FlashlightMode.Normal,
            onState = new FlashlightState { intensity = 3.5f, range = 20f, spotAngle = 60f, color = Color.white }
        }
    };

    [Header("Off State")]
    [Tooltip("Light properties used when the flashlight is OFF regardless of mode.")]
    public FlashlightState offState = new FlashlightState
    {
        intensity = 0f,
        range     = 20f,
        spotAngle = 60f,
        color     = Color.white
    };

    [Header("Transition")]
    [Tooltip("How fast intensity transitions between on/off states (units per second).")]
    public float transitionSpeed = 8f;
}
