using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the World Space Canvas on Screen_Analise.
/// Switches between idle, scanning (with live % counter + fill bar), and result screens.
/// </summary>
public class AnalyzerScreenController : MonoBehaviour
{
    [Header("Screens")]
    [SerializeField] private GameObject _idleScreen;
    [SerializeField] private GameObject _scanningScreen;
    [SerializeField] private GameObject _resultScreen;

    [Header("Scanning")]
    [Tooltip("Filled Image used as a progress bar (fillAmount 0→1).")]
    [SerializeField] private Image _scanProgressBar;

    [Tooltip("TMP text showing the scan percentage (0%–100%).")]
    [SerializeField] private TextMeshProUGUI _scanPercentText;

    [Header("Result Texts")]
    [SerializeField] private TextMeshProUGUI _compoundNameText;
    [SerializeField] private TextMeshProUGUI _descriptionText;
    [SerializeField] private TextMeshProUGUI _verdictText;

    private const string SuccessVerdict = "СИНТЕЗ УСПЕШЕН";
    private const string FailVerdict    = "ВЕЩЕСТВО НЕ ИДЕНТИФИЦИРОВАНО";

    private void Awake() => ShowIdle();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Switches to the idle screen.</summary>
    public void ShowIdle()
    {
        SetActive(_idleScreen, true);
        SetActive(_scanningScreen, false);
        SetActive(_resultScreen, false);
    }

    /// <summary>Switches to the scanning screen and resets the percent counter to 0%.</summary>
    public void ShowScanning()
    {
        SetActive(_idleScreen, false);
        SetActive(_scanningScreen, true);
        SetActive(_resultScreen, false);

        if (_scanProgressBar != null) _scanProgressBar.fillAmount = 0f;
        if (_scanPercentText  != null) _scanPercentText.text = "0%";
    }

    /// <summary>
    /// Updates the scan progress bar and percent text. Called each frame by AnalyzerController.
    /// </summary>
    public void SetScanPercent(int percent)
    {
        if (_scanProgressBar != null) _scanProgressBar.fillAmount = percent / 100f;
        if (_scanPercentText  != null) _scanPercentText.text = $"{percent}%";
    }

    /// <summary>Shows the result screen with compound name, description, and success/fail verdict.</summary>
    public void ShowResult(string compoundName, string description, bool isSuccess)
    {
        SetActive(_idleScreen, false);
        SetActive(_scanningScreen, false);
        SetActive(_resultScreen, true);

        if (_compoundNameText != null)
            _compoundNameText.text = compoundName;

        if (_descriptionText != null)
            _descriptionText.text = description;

        if (_verdictText != null)
            _verdictText.text = isSuccess ? SuccessVerdict : FailVerdict;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private static void SetActive(GameObject screen, bool active)
    {
        if (screen != null) screen.SetActive(active);
    }
}
