using UnityEngine;

/// <summary>
/// Defines the visual style/priority category of a popup message.
/// </summary>
public enum PopupMessageType
{
    /// <summary>General gameplay hint (white).</summary>
    Hint,

    /// <summary>Event or story notification (yellow).</summary>
    Event,

    /// <summary>Warning or danger signal (red).</summary>
    Warning
}

/// <summary>
/// Data container for a single popup message.
/// Pass instances to PopupMessageSystem.Show() at runtime.
/// </summary>
[System.Serializable]
public class PopupMessageData
{
    /// <summary>Main message text. Supports TextMeshPro rich-text tags.</summary>
    public string text;

    /// <summary>Optional icon displayed to the left of the text. Leave null for no icon.</summary>
    public Sprite icon;

    /// <summary>Controls color and sort priority of the popup.</summary>
    public PopupMessageType messageType;

    /// <summary>How long the popup stays fully visible before fading out (seconds).</summary>
    public float duration = 3f;

    public PopupMessageData() { }

    public PopupMessageData(string text, PopupMessageType type = PopupMessageType.Hint,
                            float duration = 3f, Sprite icon = null)
    {
        this.text        = text;
        this.messageType = type;
        this.duration    = duration;
        this.icon        = icon;
    }
}
