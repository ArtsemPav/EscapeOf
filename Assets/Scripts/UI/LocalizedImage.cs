using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Replaces the sprite of a UI Image with the translated sprite for its key.
/// The sprite assigned in the Image before adding this component acts as the
/// fallback shown when the key has no entry in the current language's table.
/// Refreshes live on language change.
/// </summary>
[RequireComponent(typeof(Image))]
public class LocalizedImage : MonoBehaviour
{
    [Tooltip("Localization key, e.g. 'whiteboard.lab'. Empty = component inactive.")]
    [SerializeField] private string _key;

    [Tooltip("Sprite shown when the key is missing from the current language's table.")]
    [SerializeField] private Sprite _fallbackSprite;

    private Image _image;

    public string Key
    {
        get => _key;
        set => _key = value;
    }

    private void Awake()
    {
        _image = GetComponent<Image>();
        // Default fallback: whatever the artist assigned in the Image itself.
        if (_fallbackSprite == null && _image != null) _fallbackSprite = _image.sprite;
    }

    private void OnEnable()
    {
        if (_image == null) _image = GetComponent<Image>();
        LocalizationSettings.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => LocalizationSettings.LanguageChanged -= Refresh;

    /// <summary>Re-applies the translated sprite (or the fallback) for the current key.</summary>
    public void Refresh()
    {
        if (_image == null || string.IsNullOrEmpty(_key)) return;

        Sprite localized = Localization.GetSprite(_key);
        _image.sprite = localized != null ? localized : _fallbackSprite;
    }
}
