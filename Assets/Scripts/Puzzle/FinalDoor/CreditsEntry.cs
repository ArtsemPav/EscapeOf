using UnityEngine;

/// <summary>
/// One entry of the end-game credits screen: an optional heading
/// (role / section name) and the body text below it.
/// Create assets from the context menu: Create > EscapeOf > Credits Entry.
/// </summary>
[CreateAssetMenu(fileName = "CreditsEntry", menuName = "EscapeOf/Credits Entry")]
public class CreditsEntry : ScriptableObject
{
    [Header("Content")]
    [Tooltip("Optional heading shown above the text (role, section name). Leave empty to show only the text.")]
    [TextArea(1, 2)]
    [SerializeField] private string _title;

    [Tooltip("Main text of this credits line.")]
    [TextArea(3, 10)]
    [SerializeField] private string _text;

    /// <summary>Optional heading shown above the text (empty string when not set).</summary>
    public string Title => _title;

    /// <summary>Main text of this credits line.</summary>
    public string Text => _text;

    /// <summary>
    /// Localized heading. Key: credits.&lt;asset name&gt;.title; falls back to <see cref="Title"/> when missing.
    /// </summary>
    public string LocalizedTitle
    {
        get
        {
            string key = $"credits.{name}.title";
            return Localization.TryGet(key, out string localized) ? localized : _title;
        }
    }

    /// <summary>
    /// Localized body text. Key: credits.&lt;asset name&gt;.text; falls back to <see cref="Text"/> when missing.
    /// </summary>
    public string LocalizedText
    {
        get
        {
            string key = $"credits.{name}.text";
            return Localization.TryGet(key, out string localized) ? localized : _text;
        }
    }
}
