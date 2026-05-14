using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Main laptop OS controller. Manages Login → Desktop state transition.
    /// Handles keyboard password input and delegates file management to LaptopWindowManager.
    /// Attach to LaptopContainer alongside PuzzleModeController.
    /// </summary>
    [RequireComponent(typeof(PuzzleModeController))]
    public class LaptopOS : MonoBehaviour, ISaveable
    {
        // ── Inspector ──────────────────────────────────────────────────────────────

        [Header("Save")]
        [SerializeField] private string _saveId = "";

        [Header("Password")]
        [Tooltip("Correct password to unlock the desktop.")]
        [SerializeField] private string _password = "1234";

        [Header("Screens")]
        [SerializeField] private GameObject _loginScreen;
        [SerializeField] private GameObject _desktopScreen;

        [Header("Login UI")]
        [SerializeField] private TMP_InputField     _passwordField;
        [SerializeField] private TMP_Text           _statusText;
        [SerializeField] private string             _errorMessage = "Неверный пароль";
        [SerializeField] private Color              _errorColor   = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private RectTransform      _shakeTarget;
        [SerializeField] private float              _shakeStrength = 10f;
        [SerializeField] private float              _shakeDuration  = 0.4f;

        [Header("References")]
        [SerializeField] private LaptopWindowManager _windowManager;

        [Header("Events")]
        [Tooltip("Fired once when the correct password is entered for the first time.")]
        [SerializeField] private UnityEvent _onFirstUnlocked;

        // ── ISaveable ──────────────────────────────────────────────────────────────

        public string SaveId => _saveId;

        public string GetSaveData() =>
            JsonUtility.ToJson(new SaveData { isUnlocked = _isUnlocked });

        public void LoadSaveData(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            _isUnlocked = data.isUnlocked;
        }

        [System.Serializable]
        private struct SaveData { public bool isUnlocked; }

        [ContextMenu("Generate Save ID")]
        private void GenerateSaveId()
        {
            if (!string.IsNullOrEmpty(_saveId)) return;
            _saveId = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // ── Private state ──────────────────────────────────────────────────────────

        private PuzzleModeController _puzzleMode;
        private bool _isUnlocked;
        private bool _isProcessing;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            _puzzleMode = GetComponent<PuzzleModeController>();
            SaveManager.Instance?.Register(this);
        }

        private void OnEnable()
        {
            _puzzleMode.OnEntered += HandleEntered;
            _puzzleMode.OnExited  += HandleExited;
        }

        private void OnDisable()
        {
            _puzzleMode.OnEntered -= HandleEntered;
            _puzzleMode.OnExited  -= HandleExited;
        }

        private void OnDestroy()
        {
            SaveManager.Instance?.Unregister(this);
        }

        private void Update()
        {
            if (_isProcessing || _isUnlocked) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.backspaceKey.wasPressedThisFrame)
                DeleteLastCharacter();

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                SubmitPassword();
        }

        /// <summary>Restores input focus to the password field without selecting all text.</summary>
        public void FocusInputField()
        {
            if (_passwordField != null && !_isUnlocked)
            {
                _passwordField.ActivateInputField();
                _passwordField.caretPosition = _passwordField.text.Length;
            }
        }

        // ── Puzzle Mode hooks ──────────────────────────────────────────────────────

        private void HandleEntered()
        {
            _isProcessing = false;

            if (_isUnlocked)
            {
                ShowDesktop();
            }
            else
            {
                ShowLogin();
                if (Keyboard.current != null)
                    Keyboard.current.onTextInput += OnCharacterTyped;
            }
        }

        private void HandleExited()
        {
            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= OnCharacterTyped;

            _windowManager?.CloseAll();
        }

        // ── Screens ────────────────────────────────────────────────────────────────

        private void ShowLogin()
        {
            _loginScreen?.SetActive(true);
            _desktopScreen?.SetActive(false);
            ClearStatus();
            SetPasswordText(string.Empty);
            _passwordField?.Select();
        }

        private void ShowDesktop()
        {
            _loginScreen?.SetActive(false);
            _desktopScreen?.SetActive(true);

            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= OnCharacterTyped;
        }

        // ── Password input ─────────────────────────────────────────────────────────

        private void OnCharacterTyped(char c)
        {
            // Handled by TMP_InputField directly to avoid duplication
        }

        private void DeleteLastCharacter()
        {
            if (_passwordField == null || _passwordField.text.Length == 0) return;
            _passwordField.text = _passwordField.text[..^1];
            ClearStatus();
        }

        /// <summary>
        /// Validates the typed password. Wire to the Login screen Submit button's onClick.
        /// </summary>
        public void SubmitPassword()
        {
            if (_isProcessing) return;

            string entered = _passwordField != null ? _passwordField.text : string.Empty;

            if (entered == _password)
                StartCoroutine(SuccessRoutine());
            else
                StartCoroutine(ErrorRoutine());
        }

        private IEnumerator SuccessRoutine()
        {
            _isProcessing = true;
            yield return new WaitForSecondsRealtime(0.2f);

            _isUnlocked = true;
            _onFirstUnlocked?.Invoke();
            SaveManager.Instance?.Save();

            ShowDesktop();
            _isProcessing = false;
        }

        private IEnumerator ErrorRoutine()
        {
            _isProcessing = true;
            ShowStatus(_errorMessage, _errorColor);

            if (_shakeTarget != null)
                yield return StartCoroutine(ShakeRoutine());
            else
                yield return new WaitForSecondsRealtime(0.5f);

            SetPasswordText(string.Empty);
            ClearStatus();
            _passwordField?.Select();
            _isProcessing = false;
        }

        private IEnumerator ShakeRoutine()
        {
            Vector3 origin  = _shakeTarget.localPosition;
            float   elapsed = 0f;

            while (elapsed < _shakeDuration)
            {
                float t = elapsed / _shakeDuration;
                _shakeTarget.localPosition = origin
                    + new Vector3(Mathf.Sin(elapsed * 55f) * _shakeStrength * (1f - t), 0f, 0f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _shakeTarget.localPosition = origin;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private void SetPasswordText(string text)
        {
            if (_passwordField != null) _passwordField.text = text;
        }

        private void ShowStatus(string message, Color color)
        {
            if (_statusText == null) return;
            _statusText.text  = message;
            _statusText.color = color;
        }

        private void ClearStatus()
        {
            if (_statusText != null) _statusText.text = string.Empty;
        }
    }
}
