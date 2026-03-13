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
/// ScriptableObject that defines all configurable parameters of the flashlight:
/// the inventory condition required to use it, light properties for each state,
/// and transition speed between states.
/// </summary>
[CreateAssetMenu(menuName = "Game/Flashlight Config")]
public class FlashlightConfig : ScriptableObject
{
    [Header("Operating Condition")]
    [Tooltip("Inventory condition that must be met for the flashlight to work.")]
    public InventoryCondition operatingCondition;

    [Header("On State")]
    public FlashlightState onState = new FlashlightState
    {
        intensity = 3.5f,
        range     = 20f,
        spotAngle = 60f,
        color     = Color.white
    };

    [Header("Off State")]
    public FlashlightState offState = new FlashlightState
    {
        intensity = 0f,
        range     = 20f,
        spotAngle = 60f,
        color     = Color.white
    };

    [Header("Transition")]
    [Tooltip("How fast intensity transitions between states (units per second).")]
    public float transitionSpeed = 8f;
}
