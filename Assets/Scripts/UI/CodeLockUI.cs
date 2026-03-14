using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the code lock numpad UI panel.
/// Attach this to the Canvas (not to CodeLockPanel itself) to prevent
/// coroutines from being killed when the panel is hidden via SetActive.
/// </summary>
public class CodeLockUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject _panel;

    [Header("Display")]
    [SerializeField] private TextMeshProUGUI _displayText;
    [SerializeField] private RectTransform _displayRect;
    [SerializeField] private TextMeshProUGUI _statusText;

    [Header("Shake")]
    [SerializeField] private float _shakeStrength = 12f;
    [SerializeField] private float _shakeDuration = 0.45f;

    private GameConfig Config => UIManager.Instance != null ? UIManager.Instance.Config : null;

    private static readonly Key[] DigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    private static readonly Key[] NumpadKeys =
    {
        Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
        Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
    };

    private CodeLock _currentLock;
    private string _enteredCode = "";
    private bool _isProcessing;

    private void Awake()
    {
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (!_panel.activeSelf || _isProcessing) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i <= 9; i++)
        {
            if (kb[DigitKeys[i]].wasPressedThisFrame || kb[NumpadKeys[i]].wasPressedThisFrame)
            {
                OnDigitPressed(i.ToString());
                return;
            }
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            OnEnterPressed();
        else if (kb.backspaceKey.wasPressedThisFrame)
            OnClearPressed();
        else if (kb.escapeKey.wasPressedThisFrame)
            Close();
    }

    /// <summary>Opens the numpad panel for the specified lock.</summary>
    public void Open(CodeLock codeLock)
    {
        _currentLock = codeLock;
        _enteredCode = "";
        _isProcessing = false;

        Color normal = Config?.normalColor ?? Color.white;
        _displayText.color = normal;
        _statusText.color = normal;
        _statusText.text = "";
        UpdateDisplay();

        UIManager.Instance?.OpenPanel(_panel);
    }

    /// <summary>Closes the panel and restores player control.</summary>
    public void Close()
    {
        if (_isProcessing) return;

        UIManager.Instance?.ClosePanel(_panel);
        _currentLock = null;
    }

    /// <summary>Appends a digit. Called by NumPadButton or keyboard.</summary>
    public void OnDigitPressed(string digit)
    {
        if (_isProcessing || _currentLock == null) return;
        if (_enteredCode.Length >= _currentLock.CodeLength) return;

        _enteredCode += digit;
        UpdateDisplay();

        if (_enteredCode.Length == _currentLock.CodeLength)
            StartCoroutine(AutoSubmit());
    }

    /// <summary>Removes the last digit.</summary>
    public void OnClearPressed()
    {
        if (_isProcessing) return;
        if (_enteredCode.Length > 0)
            _enteredCode = _enteredCode[..^1];
        _statusText.text = "";
        UpdateDisplay();
    }

    /// <summary>Submits the current code.</summary>
    public void OnEnterPressed()
    {
        if (_isProcessing || _currentLock == null || _enteredCode.Length == 0) return;
        ValidateCode();
    }

    private IEnumerator AutoSubmit()
    {
        yield return new WaitForSecondsRealtime(0.25f);
        if (!_isProcessing) ValidateCode();
    }

    private void ValidateCode()
    {
        if (_currentLock.TryUnlock(_enteredCode))
            StartCoroutine(SuccessRoutine());
        else
            StartCoroutine(ErrorRoutine());
    }

    private IEnumerator SuccessRoutine()
    {
        _isProcessing = true;
        Color success = Config?.successColor ?? new Color(0.2f, 0.9f, 0.3f);
        _displayText.color = success;
        _statusText.color = success;
        _statusText.text = Config?.codeLockSuccessText ?? "Доступ открыт";
        yield return new WaitForSecondsRealtime(1f);
        _isProcessing = false;
        Close();
    }

    private IEnumerator ErrorRoutine()
    {
        _isProcessing = true;
        Color error = Config?.errorColor ?? new Color(0.9f, 0.2f, 0.2f);
        _displayText.color = error;
        _statusText.color = error;
        _statusText.text = Config?.codeLockWrongText ?? "Неверный код";

        yield return StartCoroutine(ShakeRoutine());

        _enteredCode = "";
        _displayText.color = Config?.normalColor ?? Color.white;
        _statusText.text = "";
        UpdateDisplay();
        _isProcessing = false;
    }

    private IEnumerator ShakeRoutine()
    {
        Vector3 origin = _displayRect.localPosition;
        float elapsed = 0f;

        while (elapsed < _shakeDuration)
        {
            float t = elapsed / _shakeDuration;
            float x = Mathf.Sin(elapsed * 55f) * _shakeStrength * (1f - t);
            _displayRect.localPosition = origin + new Vector3(x, 0f, 0f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _displayRect.localPosition = origin;
    }

    private void UpdateDisplay()
    {
        if (_currentLock == null) return;

        var sb = new StringBuilder();
        for (int i = 0; i < _currentLock.CodeLength; i++)
        {
            sb.Append(i < _enteredCode.Length ? '●' : '_');
            if (i < _currentLock.CodeLength - 1) sb.Append(' ');
        }
        _displayText.text = sb.ToString();
    }
}
