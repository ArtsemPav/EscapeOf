using UnityEngine;
using TMPro;

/// <summary>
/// Manages a digital lock with 0-9 digits, Enter, and Clear buttons.
/// Interacts with PuzzleModeController upon successful code entry.
/// </summary>
public class DigitalLockSystem : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The correct combination to solve the puzzle.")]
    [SerializeField] private string _correctCode = "1234";
    
    [Tooltip("Maximum number of digits that can be entered.")]
    [SerializeField] private int _maxCodeLength = 8;

    [Tooltip("Text to display on the screen when the puzzle is solved.")]
    [SerializeField] private string _solvedText = "SOLVED";

    [Tooltip("Text to display on the screen when the code is incorrect.")]
    [SerializeField] private string _errorText = "ERROR";

    [Tooltip("How long to display the error message before clearing (seconds).")]
    [SerializeField] private float _errorDisplayDuration = 1.5f;

    [Header("References")]
    [Tooltip("TextMeshPro component used to display the entered code.")]
    [SerializeField] private TextMeshPro _screenText;
    
    [Tooltip("The puzzle controller to notify when the code is correct.")]
    [SerializeField] private PuzzleModeController _puzzleController;

    [Header("Audio")]
    [Tooltip("Sounds played when any digit, Clear, or Enter is pressed (randomly selected).")]
    [SerializeField] private AudioClip[] _buttonPressSounds;

    [Tooltip("Sound played when the correct code is entered.")]
    [SerializeField] private AudioClip _successSound;

    [Tooltip("Sound played when an incorrect code is entered.")]
    [SerializeField] private AudioClip _errorSound;

    private string _currentInput = string.Empty;
    private bool _isDisplayingTemporaryMessage = false;
    private bool _blinkState = true;
    private float _blinkTimer = 0f;
    private const float BLINK_INTERVAL = 0.5f;

    private void Start()
    {
        UpdateDisplay();
    }

    private void Update()
    {
        if (_isDisplayingTemporaryMessage || _currentInput == _solvedText) return;

        _blinkTimer += Time.deltaTime;
        if (_blinkTimer >= BLINK_INTERVAL)
        {
            _blinkTimer = 0f;
            _blinkState = !_blinkState;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Adds a digit to the current input string.
    /// </summary>
    /// <param name="digit">Digit from 0 to 9.</param>
    public void AddDigit(int digit)
    {
        if (_isDisplayingTemporaryMessage)
        {
            StopAllCoroutines();
            _isDisplayingTemporaryMessage = false;
            _currentInput = string.Empty;
        }

        if (_currentInput.Length >= _maxCodeLength) return;

        _currentInput += digit.ToString();
        PlayRandomButtonSound();
        UpdateDisplay();
    }

    /// <summary>
    /// Clears the current input string.
    /// </summary>
    public void Clear()
    {
        _currentInput = string.Empty;
        PlayRandomButtonSound();
        UpdateDisplay();
    }

    /// <summary>
    /// Checks if the entered code is correct and notifies the puzzle controller.
    /// </summary>
    public void Submit()
    {
        if (_isDisplayingTemporaryMessage) return;

        if (_currentInput == _correctCode)
        {
            if (_puzzleController != null)
            {
                _puzzleController.SetSolved();
            }
            _currentInput = _solvedText;
            PlaySound(_successSound);
            UpdateDisplay();
        }
        else
        {
            PlaySound(_errorSound);
            StartCoroutine(ShowErrorRoutine());
        }
    }

    private void PlayRandomButtonSound()
    {
        if (_buttonPressSounds == null || _buttonPressSounds.Length == 0) return;
        
        int randomIndex = Random.Range(0, _buttonPressSounds.Length);
        PlaySound(_buttonPressSounds[randomIndex]);
    }

    private void PlaySound(AudioClip clip)
    {
        if (AudioManager.Instance != null && clip != null)
        {
            AudioManager.Instance.PlaySFX(clip);
        }
    }

    private System.Collections.IEnumerator ShowErrorRoutine()
    {
        _isDisplayingTemporaryMessage = true;
        string previousInput = _currentInput;
        
        if (_screenText != null)
        {
            _screenText.text = _errorText;
        }

        yield return new WaitForSeconds(_errorDisplayDuration);

        if (_isDisplayingTemporaryMessage)
        {
            _currentInput = string.Empty;
            _isDisplayingTemporaryMessage = false;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_screenText == null) return;

        if (_isDisplayingTemporaryMessage) return;

        // If solved, just show the solved text
        if (_currentInput == _solvedText)
        {
            _screenText.text = _solvedText;
            return;
        }

        System.Text.StringBuilder displayBuilder = new System.Text.StringBuilder();

        // Use fixed width for each character to prevent layout shifting
        displayBuilder.Append("<mspace=0.6em>");

        for (int i = 0; i < _maxCodeLength; i++)
        {
            if (i < _currentInput.Length)
            {
                // If it's the last entered character, show the digit, otherwise show '*'
                if (i == _currentInput.Length - 1)
                {
                    displayBuilder.Append(_currentInput[i]);
                }
                else
                {
                    displayBuilder.Append('*');
                }
            }
            else
            {
                // Current slot to fill
                if (i == _currentInput.Length)
                {
                    // Use a transparent underscore instead of a space to prevent layout shifting
                    if (_blinkState)
                    {
                        displayBuilder.Append('_');
                    }
                    else
                    {
                        displayBuilder.Append("<color=#00000000>_</color>");
                    }
                }
                else
                {
                    displayBuilder.Append('_');
                }
            }
        }

        displayBuilder.Append("</mspace>");
        _screenText.text = displayBuilder.ToString();
    }
}
