using UnityEngine;

/// <summary>
/// DEV BUTTON (temporary): resets the MRI room cutscene so it can be played again with the green button.
/// Put it on the red button mesh together with a collider on the Interactable layer.
/// Delete this component (and the script) when the testing is done.
/// </summary>
public class MriCutsceneResetButton : MonoBehaviour, IInteractable
{
    [SerializeField] private MriUvCutscene _cutscene;

    [Header("UI Feedback")]
    [SerializeField] private string _hintText = "[DEV] Сбросить анимацию";

    public bool CanInteract() => _cutscene != null;

    public void Interact()
    {
        if (_cutscene != null)
            _cutscene.DevResetState();
    }

    public string GetInteractText() => _hintText;

    public bool IsPickable() => false;

    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    public bool UseLMBClick => true;
}
