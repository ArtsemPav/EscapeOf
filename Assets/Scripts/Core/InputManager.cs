using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }
    private PlayerInputActions _playerInputActions;

    // Movement and Look
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    
    // Actions
    public event Action OnInteractPerformed;
    public event Action OnJumpPerformed;
    public event Action<bool> OnSprintToggled;
    public event Action OnMenuPerformed;
    public event Action OnInventoryPerformed;

    // True while the Player action map is enabled. While false (inventory panel,
    // puzzle, cinematic) crouch state must not change — see FPSController.
    private bool _playerInputEnabled = true;
    public bool IsPlayerInputEnabled => _playerInputEnabled;

    /// <summary>True while the Crouch button is physically held. Polled by FPSController
    /// instead of canceled events, because disabling the action map (panel opens)
    /// fires a spurious Crouch.canceled even when the key is still down.</summary>
    public bool CrouchIsHeld => _playerInputEnabled && _playerInputActions.Player.Crouch.IsPressed();

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
            return;
        }
        _playerInputActions = new PlayerInputActions();
    }

    private void OnEnable() {
        _playerInputActions.Player.Enable();
        _playerInputActions.UI.Enable();
        
        // Player Actions
        _playerInputActions.Player.Move.performed += OnMovePerformed;
        _playerInputActions.Player.Move.canceled += OnMoveCanceled;
        _playerInputActions.Player.Look.performed += OnLookPerformed;
        _playerInputActions.Player.Look.canceled += OnLookCanceled;
        _playerInputActions.Player.Interact.performed += OnInteractPerformedAction;
        _playerInputActions.Player.Jump.performed += OnJumpPerformedAction;
        _playerInputActions.Player.Sprint.performed += OnSprintPerformed;
        _playerInputActions.Player.Sprint.canceled += OnSprintCanceled;

        // UI Actions
        _playerInputActions.UI.Menu.performed += OnMenuPerformedAction;
        _playerInputActions.UI.Inventory.performed += OnInventoryPerformedAction;
    }

    private void OnDisable() {
        if (_playerInputActions == null) return;

        _playerInputActions.Player.Move.performed -= OnMovePerformed;
        _playerInputActions.Player.Move.canceled -= OnMoveCanceled;
        _playerInputActions.Player.Look.performed -= OnLookPerformed;
        _playerInputActions.Player.Look.canceled -= OnLookCanceled;
        _playerInputActions.Player.Interact.performed -= OnInteractPerformedAction;
        _playerInputActions.Player.Jump.performed -= OnJumpPerformedAction;
        _playerInputActions.Player.Sprint.performed -= OnSprintPerformed;
        _playerInputActions.Player.Sprint.canceled -= OnSprintCanceled;

        _playerInputActions.UI.Menu.performed -= OnMenuPerformedAction;
        _playerInputActions.UI.Inventory.performed -= OnInventoryPerformedAction;

        _playerInputActions.Player.Disable();
        _playerInputActions.UI.Disable();
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx) => MoveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => MoveInput = Vector2.zero;
    private void OnLookPerformed(InputAction.CallbackContext ctx) => LookInput = ctx.ReadValue<Vector2>();
    private void OnLookCanceled(InputAction.CallbackContext ctx) => LookInput = Vector2.zero;
    private void OnInteractPerformedAction(InputAction.CallbackContext ctx) => OnInteractPerformed?.Invoke();
    private void OnJumpPerformedAction(InputAction.CallbackContext ctx) => OnJumpPerformed?.Invoke();
    private void OnSprintPerformed(InputAction.CallbackContext ctx) => OnSprintToggled?.Invoke(true);
    private void OnSprintCanceled(InputAction.CallbackContext ctx) => OnSprintToggled?.Invoke(false);
    private void OnMenuPerformedAction(InputAction.CallbackContext ctx) => OnMenuPerformed?.Invoke();
    private void OnInventoryPerformedAction(InputAction.CallbackContext ctx) => OnInventoryPerformed?.Invoke();

    public void SetPlayerInputEnabled(bool enabled) {
        if (enabled) {
            _playerInputEnabled = true;
            _playerInputActions.Player.Enable();
        } else {
            _playerInputEnabled = false;
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            _playerInputActions.Player.Disable();
        }
    }

    /// <summary>
    /// Enables or disables the UI action map (Menu, Inventory). Used by
    /// cinematics that must not be interrupted by UI input — after the
    /// final puzzle is solved no UI toggles should respond.
    /// </summary>
    public void SetUIInputEnabled(bool enabled) {
        if (_playerInputActions == null) return;

        if (enabled) {
            _playerInputActions.UI.Enable();
        } else {
            _playerInputActions.UI.Disable();
        }
    }
}
