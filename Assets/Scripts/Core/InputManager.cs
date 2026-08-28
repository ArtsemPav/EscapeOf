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
    public event Action<bool> OnCrouchToggled;
    public event Action OnMenuPerformed;
    public event Action OnInventoryPerformed;

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
        _playerInputActions.Player.Crouch.performed += OnCrouchPerformed;
        _playerInputActions.Player.Crouch.canceled += OnCrouchCanceled;

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
        _playerInputActions.Player.Crouch.performed -= OnCrouchPerformed;
        _playerInputActions.Player.Crouch.canceled -= OnCrouchCanceled;

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
    private void OnCrouchPerformed(InputAction.CallbackContext ctx) => OnCrouchToggled?.Invoke(true);
    private void OnCrouchCanceled(InputAction.CallbackContext ctx) => OnCrouchToggled?.Invoke(false);
    private void OnMenuPerformedAction(InputAction.CallbackContext ctx) => OnMenuPerformed?.Invoke();
    private void OnInventoryPerformedAction(InputAction.CallbackContext ctx) => OnInventoryPerformed?.Invoke();

    public void SetPlayerInputEnabled(bool enabled) {
        if (enabled) {
            _playerInputActions.Player.Enable();
        } else {
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            _playerInputActions.Player.Disable();
        }
    }
}
