using TMPro;
using UnityEngine;

/// <summary>
/// Replaces the text of any TMP_Text (TextMeshProUGUI or 3D TextMeshPro) with the
/// translation of its key. Add next to an existing TMP component and put the
/// localization key into the text field (or the component's key field).
/// Refreshes live on language change.
/// </summary>
public class LocalizedTMP : MonoBehaviour
{
    [Tooltip("Localization key, e.g. 'ui.hints.lmb'.")]
    [SerializeField] private string _key;

    private TMP_Text _text;

    public string Key
    {
        get => _key;
        set => _key = value;
    }

    private void Awake() => _text = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        LocalizationSettings.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => LocalizationSettings.LanguageChanged -= Refresh;

    /// <summary>Re-applies the translation for the current key.</summary>
    public void Refresh()
    {
        if (_text != null && !string.IsNullOrEmpty(_key))
            _text.text = Localization.Get(_key);
    }
}

