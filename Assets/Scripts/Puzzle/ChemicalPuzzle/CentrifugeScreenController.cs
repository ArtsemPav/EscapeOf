using TMPro;
using UnityEngine;

/// <summary>
/// Manages the World Space Canvas on screen_Centrifuga.
/// Shows a countdown timer during the spin cycle and switches back to the idle screen when done.
/// </summary>
public class CentrifugeScreenController : MonoBehaviour
{
    [SerializeField] private GameObject _idleScreen;
    [SerializeField] private GameObject _runningScreen;
    [SerializeField] private TextMeshProUGUI _timerText;

    private void Awake()
    {
        ShowIdle();
    }

    /// <summary>Updates the countdown timer display. remaining is in seconds.</summary>
    public void UpdateTimer(float remaining)
    {
        if (_idleScreen != null) _idleScreen.SetActive(false);
        if (_runningScreen != null) _runningScreen.SetActive(true);

        if (_timerText != null)
        {
            int totalSeconds = Mathf.CeilToInt(remaining);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            _timerText.text = $"{minutes}:{seconds:D2}";
        }
    }

    /// <summary>Switches to the idle screen.</summary>
    public void ShowIdle()
    {
        if (_idleScreen != null) _idleScreen.SetActive(true);
        if (_runningScreen != null) _runningScreen.SetActive(false);
    }
}
