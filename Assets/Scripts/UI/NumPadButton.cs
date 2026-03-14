using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Configurable numpad button for CodeLockUI.
/// Set Value to "0"–"9" for digit input, "clear" for backspace, "enter" to submit.
/// </summary>
[RequireComponent(typeof(Button))]
public class NumPadButton : MonoBehaviour
{
    [SerializeField] private CodeLockUI _lockUI;

    [Tooltip("Digit '0'–'9', or 'clear' / 'enter' for action buttons.")]
    [SerializeField] private string _value;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        switch (_value.ToLower())
        {
            case "clear":  _lockUI.OnClearPressed();      break;
            case "enter":  _lockUI.OnEnterPressed();      break;
            default:       _lockUI.OnDigitPressed(_value); break;
        }
    }
}
