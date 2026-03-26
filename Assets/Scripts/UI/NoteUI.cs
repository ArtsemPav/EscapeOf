using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the note reading panel.
/// Attach to the Canvas (not to NotePanel) to keep the MonoBehaviour active
/// while the panel is hidden.
/// </summary>
public class NoteUI : MonoBehaviour
{
    public static NoteUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject _panel;

    [Header("Text Fields")]
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _contentText;

    [Header("Audio")]
    [SerializeField] private AudioClip _openClip;

    private bool _justOpened;

    private void Awake()
    {
        Instance = this;
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (!_panel.activeSelf) return;

        if (_justOpened) { _justOpened = false; return; }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
            Close();
    }

    /// <summary>Opens the panel and displays the given note.</summary>
    public void Open(NoteData noteData)
    {
        _titleText.text   = noteData.title;
        _contentText.text = noteData.content;
        _justOpened       = true;
        UIManager.Instance?.OpenPanel(_panel);
        AudioManager.Instance?.PlaySFX(_openClip);
    }

    /// <summary>Closes the panel and restores player control.</summary>
    public void Close()
    {
        UIManager.Instance?.ClosePanel(_panel);
    }
}
