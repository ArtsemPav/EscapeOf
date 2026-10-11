using UnityEngine;

/// <summary>
/// Interactable button that starts the MRI room UV cutscene (MriUvCutscene).
/// Put it on the button mesh together with a collider on the Interactable layer.
/// A ButtonPressAnimation on the same object plays the press animation.
/// Becomes non-interactable once the cutscene has started.
/// </summary>
public class MriCutsceneButton : MonoBehaviour, IInteractable
{
    [SerializeField] private MriUvCutscene _cutscene;

    [Header("UI Feedback")]
    [SerializeField] private string _hintText = "Нажать";

    public bool CanInteract() => _cutscene != null && _cutscene.CanStart;

    public void Interact()
    {
        if (_cutscene != null)
            _cutscene.Play();
    }

    public string GetInteractText() => _hintText;

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    /// <summary>Single LMB click, same as the other puzzle buttons.</summary>
    public bool UseLMBClick => true;
}
