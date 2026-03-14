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
}
