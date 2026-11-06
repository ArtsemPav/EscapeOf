using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Button that keeps the selected tint even when it loses focus or the pointer
/// moves away — used for the journal's category column.
/// </summary>
public class JournalCategoryButton : Button
{
    private bool _forcedSelected;

    /// <summary>Locks the visual state to "selected" until unset.</summary>
    public void SetForcedSelected(bool selected)
    {
        _forcedSelected = selected;
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (_forcedSelected && targetGraphic != null)
        {
            // Paint the selected tint no matter what state the pointer/focus is in.
            Color target = colors.selectedColor;
            targetGraphic.CrossFadeColor(target, instant ? 0f : colors.fadeDuration, true, true);
            return;
        }

        base.DoStateTransition(state, instant);
    }
}
