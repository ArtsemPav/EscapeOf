using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2f;

    [Header("Jump and Fall")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float gravity = -12f;
    [SerializeField] private float initialFallVelocity = -2f;

    [Header("Crouching")]
    [SerializeField] private float standingHeight = 2f;
    [SerializeField] private float crouchingHeight = 1f;
    [SerializeField] private float cameraOffset = 0.4f;

    [Header("Look")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 0.2f;
    [SerializeField] private float rotationSmoothTime = 0.12f;

    [Header("Interaction")]
    public float interactDistance = 2.5f;
    public LayerMask interactableLayer;

    private CharacterController _characterController;
    private PlayerInputActions _input;
    private IInteractable _currentInteractable;

    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private float _cameraPitch;
    private bool _isGrounded;
    private bool _isCrouching;
    private bool _isRunning;
    private float _verticalVelocity;
    private float _targetHeight;

    private void Awake() {
        _characterController = GetComponent<CharacterController>();
        _input = new PlayerInputActions();
        _targetHeight = standingHeight;
    }

    private void OnEnable() {
        _input.Player.Enable();
        _input.Player.Move.performed += StoreMovementInput;
        _input.Player.Move.canceled += StoreMovementInput;
        _input.Player.Look.performed += StoreLookInput;
        _input.Player.Look.canceled += StoreLookInput;
        _input.Player.Interact.performed += Interact;
        _input.Player.Jump.performed += Jump;
        _input.Player.Sprint.performed += Sprint;
        _input.Player.Sprint.canceled += Sprint;
        _input.Player.Crouch.performed += Crouch;
    }

    private void OnDisable() {
        _input.Player.Disable();
        _input.Player.Move.performed -= StoreMovementInput;
        _input.Player.Move.canceled -= StoreMovementInput;
        _input.Player.Look.performed -= StoreLookInput;
        _input.Player.Look.canceled -= StoreLookInput;
        _input.Player.Interact.performed -= Interact;
        _input.Player.Jump.performed -= Jump;
        _input.Player.Sprint.performed -= Sprint;
        _input.Player.Sprint.canceled -= Sprint;
        _input.Player.Crouch.performed -= Crouch;
    }

    void Update()
    {
        _isGrounded = _characterController.isGrounded;
        HandleGravity();
        HandleLook();
        HandleMovement();
        HandleCrouchTransition();
        HandleInteractionDetection();
    }

    private void HandleLook() {
        float mouseX = _lookInput.x * mouseSensitivity;
        float mouseY = _lookInput.y * mouseSensitivity;

        _cameraPitch -= mouseY;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -89f, 89f);

        cameraTransform.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleMovement() {
        Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        float currentSpeed = _isCrouching ? crouchSpeed : _isRunning ? runSpeed : walkSpeed;
        Vector3 finalMove = move * currentSpeed;
        finalMove.y = _verticalVelocity;

        var collisions = _characterController.Move(finalMove * Time.deltaTime);
        if ((collisions & CollisionFlags.Above) != 0) {
            _verticalVelocity = initialFallVelocity;
        }
    }

    private void HandleCrouchTransition() {
        float currentHeight = _characterController.height;
        if (Mathf.Abs(currentHeight - _targetHeight) < 0.01f) {
            _characterController.height = _targetHeight;
            return;
        }
        float newHeight = Mathf.Lerp(currentHeight, _targetHeight, crouchSpeed * Time.deltaTime);
        _characterController.height = newHeight;
        _characterController.center = new Vector3(0, newHeight * 0.5f, 0);

        float targetCameraY = newHeight - cameraOffset;
        Vector3 currentCamPos = new Vector3(cameraTransform.localPosition.x, targetCameraY, cameraTransform.localPosition.z);
        cameraTransform.localPosition = new Vector3(currentCamPos.x, Mathf.Lerp(currentCamPos.y, targetCameraY, crouchSpeed * Time.deltaTime), currentCamPos.z);
    }

    private void HandleInteractionDetection() {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer)) {
            if (hit.collider.TryGetComponent(out IInteractable interactable)) {
                if (_currentInteractable != interactable) {
                    _currentInteractable = interactable;
                    if (Assets.Scripts.UI.InteractionUI.Instance != null) {
                        Assets.Scripts.UI.InteractionUI.Instance.SetHint(true, _currentInteractable.GetInteractText(), _currentInteractable.IsPickable());
                    }
                }
                return;
            }
        }

        if (_currentInteractable != null) {
            _currentInteractable = null;
            if (Assets.Scripts.UI.InteractionUI.Instance != null) {
                Assets.Scripts.UI.InteractionUI.Instance.SetHint(false);
            }
        }
    }

    private void HandleGravity() {
        if (_isGrounded && _verticalVelocity < 0) {
            _verticalVelocity = initialFallVelocity;
        }
        _verticalVelocity += gravity * Time.deltaTime;
    }

    private void StoreMovementInput(InputAction.CallbackContext context) {
        _moveInput = context.ReadValue<Vector2>();
    }

    private void StoreLookInput(InputAction.CallbackContext context) {
        _lookInput = context.ReadValue<Vector2>();
    }

    private void Jump(InputAction.CallbackContext context) {
        if (_isGrounded) {
            _verticalVelocity = jumpForce;
        }
    }

    private void Crouch(InputAction.CallbackContext context) {
        _isCrouching = !_isCrouching;
        _targetHeight = _isCrouching ? crouchingHeight : standingHeight;
    }

    private void Sprint(InputAction.CallbackContext context) {
        _isRunning = context.performed;
    }

    private void Interact(InputAction.CallbackContext ctx) {
        if (_currentInteractable != null) {
            _currentInteractable.Interact();
            // Force refresh hint after interaction
            _currentInteractable = null;
            if (Assets.Scripts.UI.InteractionUI.Instance != null) {
                Assets.Scripts.UI.InteractionUI.Instance.SetHint(false);
            }
        }
    }
}
