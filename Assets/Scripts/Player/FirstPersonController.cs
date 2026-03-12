using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 6f;
    public float crouchSpeed = 1.5f;
    public float jumpHeight = 1.2f;
    public float gravity = -9.81f;

    [Header("Look")]
    public Transform cameraRoot;
    public float mouseSensitivity = 0.2f;
    public float verticalClampAngle = 80f;

    [Header("Crouch")]
    public float standingHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float crouchTransitionSpeed = 8f;

    [Header("Interaction")]
    public float interactDistance = 2.5f;
    public LayerMask interactableLayer;

    private CharacterController _characterController;
    private PlayerInputActions _input;

    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool _jumpPressed;
    private bool _isSprinting;
    private bool _isCrouching;

    private Vector3 _velocity;
    private float _verticalRotation;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _input = new PlayerInputActions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.Player.Jump.performed += OnJump;
        _input.Player.Interact.performed += OnInteract;
        _input.Player.Crouch.performed += OnCrouchStart;
        _input.Player.Crouch.canceled += OnCrouchEnd;
    }

    private void OnDisable()
    {
        _input.Player.Jump.performed -= OnJump;
        _input.Player.Interact.performed -= OnInteract;
        _input.Player.Crouch.performed -= OnCrouchStart;
        _input.Player.Crouch.canceled -= OnCrouchEnd;
        _input.Player.Disable();
    }

    private void Update()
    {
        _moveInput = _input.Player.Move.ReadValue<Vector2>();
        _lookInput = _input.Player.Look.ReadValue<Vector2>();
        _isSprinting = _input.Player.Sprint.IsPressed();

        HandleLook();
        HandleMovement();
        HandleCrouchTransition();
    }

    private bool _lookEnabled = true;

    /// <summary>Enables or disables camera look and player movement input. Used by InventoryUI.</summary>
    public void SetPlayerInputEnabled(bool enabled)
    {
        _lookEnabled = enabled;

        if (enabled)
            _input.Player.Enable();
        else
            _input.Player.Disable();
    }

    private void HandleLook()
    {
        if (!_lookEnabled) return;

        transform.Rotate(Vector3.up * _lookInput.x * mouseSensitivity);

        _verticalRotation -= _lookInput.y * mouseSensitivity;
        _verticalRotation = Mathf.Clamp(_verticalRotation, -verticalClampAngle, verticalClampAngle);
        cameraRoot.localRotation = Quaternion.Euler(_verticalRotation, 0f, 0f);
    }

    private void HandleMovement()
    {
        bool isGrounded = _characterController.isGrounded;

        if (isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;

        float currentSpeed = _isCrouching ? crouchSpeed : (_isSprinting ? runSpeed : walkSpeed);
        Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        _characterController.Move(move * currentSpeed * Time.deltaTime);

        if (_jumpPressed && isGrounded && !_isCrouching)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _jumpPressed = false;

        _velocity.y += gravity * Time.deltaTime;
        _characterController.Move(_velocity * Time.deltaTime);
    }

    private void HandleCrouchTransition()
    {
        float targetHeight = _isCrouching ? crouchHeight : standingHeight;
        _characterController.height = Mathf.Lerp(
            _characterController.height,
            targetHeight,
            Time.deltaTime * crouchTransitionSpeed
        );
        _characterController.center = new Vector3(0f, _characterController.height / 2f, 0f);
    }

    private void OnJump(InputAction.CallbackContext ctx) => _jumpPressed = true;
    private void OnCrouchStart(InputAction.CallbackContext ctx) => _isCrouching = true;
    private void OnCrouchEnd(InputAction.CallbackContext ctx) => _isCrouching = false;

    private void OnInteract(InputAction.CallbackContext ctx)
    {
        Ray ray = new Ray(cameraRoot.position, cameraRoot.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out IInteractable interactable))
                interactable.Interact();
        }
    }
}
