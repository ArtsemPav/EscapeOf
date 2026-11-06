using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable button for the journal board's left column — one instance per document category.
/// Label and click behavior are provided by <see cref="Setup"/>.
/// </summary>
public class JournalEntryButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _label;
    [SerializeField] private Button _button;

    private Action _onClick;

    private void Awake()
    {
        if (_button != null)
            _button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>
    /// Configures the button with a label text and a click callback.
    /// </summary>
    public void Setup(string label, Action onClick)
    {
        if (_label != null)
            _label.text = label;

        _onClick = onClick;
    }

    /// <summary>Highlights the button as the active category (or restores its normal tint).</summary>
    public void SetSelected(bool selected)
    {
        if (_button == null) return;

        // Lock the visual state so losing focus / pointer exit keeps the selected tint.
        if (_button is JournalCategoryButton categoryButton)
            categoryButton.SetForcedSelected(selected);

        var colors = _button.colors;
        Color target = selected ? colors.selectedColor : colors.normalColor;
        _button.targetGraphic?.CrossFadeColor(target, colors.fadeDuration, true, true);
    }

    private void HandleClick() => _onClick?.Invoke();
}
