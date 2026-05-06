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

    private string _currentInput = string.Empty;
    private bool _isDisplayingTemporaryMessage = false;

    private void Start()
    {
        UpdateDisplay();
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
        UpdateDisplay();
    }

    /// <summary>
    /// Clears the current input string.
    /// </summary>
    public void Clear()
    {
        _currentInput = string.Empty;
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
            UpdateDisplay();
        }
        else
        {
            StartCoroutine(ShowErrorRoutine());
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
                // Fill remaining slots with '_'
                displayBuilder.Append('_');
            }
        }

        _screenText.text = displayBuilder.ToString();
    }
}
