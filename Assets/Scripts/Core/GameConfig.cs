using UnityEngine;

/// <summary>
/// Central ScriptableObject for game-wide texts and UI colors.
/// Create via right-click → Create → Game → Game Config.
/// Assign on the UIManager component in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "Game/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Interaction Hints")]
    [Tooltip("Word shown before the item name when the player looks at a pickable item. Example: 'Взять Ключ'.")]
    public string pickUpPrefix = "Взять";

    [Header("CodeLock Feedback")]
    [Tooltip("Text shown on the code lock screen when the correct code is entered.")]
    public string codeLockSuccessText = "Доступ открыт";
    [Tooltip("Text shown on the code lock screen when the wrong code is entered.")]
    public string codeLockWrongText = "Неверный код";

    [Header("UI Colors")]
    [Tooltip("Color for success messages, correct code highlight, etc.")]
    public Color successColor = new Color(0.2f, 0.9f, 0.3f);
    [Tooltip("Color for error messages, wrong code highlight, etc.")]
    public Color errorColor = new Color(0.9f, 0.2f, 0.2f);
    [Tooltip("Default color for UI display text.")]
    public Color normalColor = Color.white;

    [Header("Pickable Item Shimmer")]
    [Tooltip("Particle prefab spawned above pickable items to draw the player's attention. One burst every ~20 seconds.")]
    public GameObject shimmerPrefab;

    [Tooltip("Maximum distance (meters) at which the shimmer is visible. Beyond this it stops playing to save performance.")]
    public float shimmerRange = 10f;

    [Tooltip("Seconds between shimmer bursts. Lower = more frequent flickering.")]
    public float shimmerInterval = 20f;

    [Tooltip("Global toggle for the shimmer effect. Disable to turn off all pickable item hints at once.")]
    public bool shimmerEnabled = true;
}
