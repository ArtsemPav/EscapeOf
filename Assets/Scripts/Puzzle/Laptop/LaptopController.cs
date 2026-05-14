using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the laptop password puzzle.
/// Handles keyboard text input and submit logic while in puzzle mode.
/// Attach to LaptopContainer alongside PuzzleModeController.
/// Wire the submit 3D-button's SimpleInteractable.OnInteract → Submit().
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class LaptopController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Password")]
    [Tooltip("The correct password that solves the puzzle.")]
    [SerializeField] private string _correctPassword = "1234";

    [Header("References")]
    [Tooltip("TMP_InputField on the laptop Canvas that displays the typed text.")]
    [SerializeField] private TMP_InputField _inputField;

    [Tooltip("Label shown on status feedback (correct / wrong).")]
    [SerializeField] private TextMeshProUGUI _statusText;

    [Header("Feedback")]
    [SerializeField] private string _successMessage = "Доступ разрешён";
    [SerializeField] private string _errorMessage   = "Неверный пароль";
    [SerializeField] private Color  _successColor   = new Color(0.2f, 0.9f, 0.3f);
    [SerializeField] private Color  _errorColor     = new Color(0.9f, 0.2f, 0.2f);

    [Header("Shake (error feedback)")]
    [SerializeField] private RectTransform _shakeTarget;
    [SerializeField] private float _shakeStrength = 10f;
    [SerializeField] private float _shakeDuration  = 0.4f;

    [Header("Events")]
    [Tooltip("Fired when the correct password is submitted.")]
    [SerializeField] private UnityEngine.Events.UnityEvent _onSolved;

    // ── Private state ──────────────────────────────────────────────────────────

    private PuzzleModeController _puzzleMode;
    private bool _isActive;
    private bool _isProcessing;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
    }

    private void OnEnable()
    {
        _puzzleMode.OnEntered += HandleEntered;
        _puzzleMode.OnExited  += HandleExited;
        _puzzleMode.OnSolved  += HandleSolved;
    }

    private void OnDisable()
    {
        _puzzleMode.OnEntered -= HandleEntered;
        _puzzleMode.OnExited  -= HandleExited;
        _puzzleMode.OnSolved  -= HandleSolved;
    }

    private void Update()
    {
        if (!_isActive || _isProcessing) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        // Backspace
        if (kb.backspaceKey.wasPressedThisFrame)
        {
            DeleteLastCharacter();
        }

        // Submit on Enter / Numpad Enter
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            Submit();
        }
    }

    private void HandleEntered()
    {
        _isActive = true;
        _isProcessing = false;

        ClearStatus();
        SetInputText(string.Empty);

        // Focus the input field so the TMP caret is visible
        if (_inputField != null)
            _inputField.Select();

        // Subscribe to text input events from the Input System
        if (Keyboard.current != null)
            Keyboard.current.onTextInput += OnCharacterTyped;
    }

    private void HandleExited()
    {
        _isActive = false;

        if (Keyboard.current != null)
            Keyboard.current.onTextInput -= OnCharacterTyped;
    }

    private void HandleSolved()
    {
        // Already solved — no extra work needed here.
    }

    // ── Character input ────────────────────────────────────────────────────────

    private void OnCharacterTyped(char c)
    {
        if (!_isActive || _isProcessing) return;

        // Ignore control characters
        if (char.IsControl(c)) return;

        if (_inputField != null)
        {
            _inputField.text += c;
        }
    }

    private void DeleteLastCharacter()
    {
        if (_inputField == null || _inputField.text.Length == 0) return;

        _inputField.text = _inputField.text[..^1];
        ClearStatus();
    }

    // ── Submit ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the current input against the correct password.
    /// Call this from the 3D submit button's SimpleInteractable.OnInteract event.
    /// </summary>
    public void Submit()
    {
        if (_isProcessing || _puzzleMode.IsSolved) return;

        string entered = _inputField != null ? _inputField.text : string.Empty;

        if (entered == _correctPassword)
        {
            StartCoroutine(SuccessRoutine());
        }
        else
        {
            StartCoroutine(ErrorRoutine());
        }
    }

    // ── Routines ───────────────────────────────────────────────────────────────

    private IEnumerator SuccessRoutine()
    {
        _isProcessing = true;

        ShowStatus(_successMessage, _successColor);

        yield return new WaitForSecondsRealtime(1f);

        _onSolved?.Invoke();
        _puzzleMode.SetSolved();
    }

    private IEnumerator ErrorRoutine()
    {
        _isProcessing = true;

        ShowStatus(_errorMessage, _errorColor);

        if (_shakeTarget != null)
            yield return StartCoroutine(ShakeRoutine());
        else
            yield return new WaitForSecondsRealtime(0.5f);

        SetInputText(string.Empty);
        ClearStatus();

        _isProcessing = false;
    }

    private IEnumerator ShakeRoutine()
    {
        Vector3 origin  = _shakeTarget.localPosition;
        float   elapsed = 0f;

        while (elapsed < _shakeDuration)
        {
            float t = elapsed / _shakeDuration;
            float x = Mathf.Sin(elapsed * 55f) * _shakeStrength * (1f - t);
            _shakeTarget.localPosition = origin + new Vector3(x, 0f, 0f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _shakeTarget.localPosition = origin;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetInputText(string text)
    {
        if (_inputField == null) return;
        _inputField.text = text;
    }

    private void ShowStatus(string message, Color color)
    {
        if (_statusText == null) return;
        _statusText.text  = message;
        _statusText.color = color;
    }

    private void ClearStatus()
    {
        if (_statusText == null) return;
        _statusText.text = string.Empty;
    }
}
