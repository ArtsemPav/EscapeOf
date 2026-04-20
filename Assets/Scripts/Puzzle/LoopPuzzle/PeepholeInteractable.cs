using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Interactable peephole in the wall. On click, activates the peephole camera
/// and blocks FPS movement (modal state) — cursor stays locked, no UI shown.
/// Player exits by clicking LMB anywhere, pressing WASD, or pressing Esc (Menu action).
///
/// Exit detection uses owned InputActions that are NOT part of the managed Player
/// action map. This is intentional: UIManager.PushModalState() calls
/// InputManager.SetPlayerInputEnabled(false) which disables the Player map —
/// including any LMB/WASD bindings in it. Our owned actions bypass this entirely
/// and remain active as long as this component is enabled.
/// </summary>
public class PeepholeInteractable : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private string _interactText = "Заглянуть";

    [Header("Camera")]
    [Tooltip("CinemachineCamera positioned to look into the painting room. Starts inactive.")]
    [SerializeField] private CinemachineCamera _peepholeCamera;

    private bool _isActive;
    private bool _canExit;   // prevents same-frame exit on the click that entered
    private bool _isSubscribed;

    // Owned actions — independent of the Player action map managed by InputManager.
    // They fire even when SetPlayerInputEnabled(false) has been called.
    private InputAction _clickAction;
    private InputAction _moveAction;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_peepholeCamera != null)
            _peepholeCamera.gameObject.SetActive(false);

        // Button type: WasPerformedThisFrame() is true only on the initial press,
        // not while held — prevents immediate re-exit if WASD was already held on enter.
        _clickAction = new InputAction("PeepholeExit_Click", InputActionType.Button, "<Mouse>/leftButton");

        _moveAction = new InputAction("PeepholeExit_Move", InputActionType.Button);
        _moveAction.AddBinding("<Keyboard>/w");
        _moveAction.AddBinding("<Keyboard>/a");
        _moveAction.AddBinding("<Keyboard>/s");
        _moveAction.AddBinding("<Keyboard>/d");
    }

    private void OnEnable()
    {
        _clickAction.Enable();
        _moveAction.Enable();
        SubscribeToMenuInput();
    }

    private void OnDisable()
    {
        _clickAction.Disable();
        _moveAction.Disable();
        UnsubscribeFromMenuInput();
        if (_isActive) ExitPeepholeMode();
    }

    private void OnDestroy()
    {
        _clickAction.Dispose();
        _moveAction.Dispose();
    }

    private void Update()
    {
        if (!_isActive) return;

        // Skip the very first frame after entering to ignore the activating click/press.
        if (!_canExit)
        {
            _canExit = true;
            return;
        }

        if (_clickAction.WasPerformedThisFrame() || _moveAction.WasPerformedThisFrame())
            ExitPeepholeMode();
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract()       => !_isActive;
    public void Interact()          => EnterPeepholeMode();
    public bool IsPickable()        => false;
    public bool UseLMBClick         => true;
    public string GetInteractText() => _isActive ? string.Empty : _interactText;

    // ── Mode Control ───────────────────────────────────────────────────────────

    private void EnterPeepholeMode()
    {
        if (_isActive) return;
        _isActive = true;
        _canExit  = false;

        if (_peepholeCamera != null)
            _peepholeCamera.gameObject.SetActive(true);

        UIManager.Instance?.PushModalState();
        SetCursorLocked(true);
    }

    private void ExitPeepholeMode()
    {
        if (!_isActive) return;
        _isActive = false;

        if (_peepholeCamera != null)
            _peepholeCamera.gameObject.SetActive(false);

        UIManager.Instance?.PopModalState();
        SetCursorLocked(true);
    }

    // ── Esc / Menu action ──────────────────────────────────────────────────────

    private void SubscribeToMenuInput()
    {
        if (_isSubscribed || InputManager.Instance == null) return;
        InputManager.Instance.OnMenuPerformed += OnMenuPerformed;
        _isSubscribed = true;
    }

    private void UnsubscribeFromMenuInput()
    {
        if (!_isSubscribed || InputManager.Instance == null) return;
        InputManager.Instance.OnMenuPerformed -= OnMenuPerformed;
        _isSubscribed = false;
    }

    private void OnMenuPerformed()
    {
        if (!_isActive) return;
        // Defer to next frame so the Esc event fully resolves before state changes
        // (same pattern as PuzzleModeController to prevent pause menu from opening).
        StartCoroutine(ExitNextFrame());
    }

    private IEnumerator ExitNextFrame()
    {
        yield return null;
        ExitPeepholeMode();
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
