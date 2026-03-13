using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2f;

    [Header("Movement Feel")]
    [SerializeField] private float accelerationTime = 0.12f;
    [SerializeField] private float decelerationTime = 0.07f;

    [Header("Jump and Fall")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float gravity = -12f;
    [SerializeField] private float initialFallVelocity = -2f;

    [Header("Crouching")]
    [SerializeField] private float standingHeight = 2f;
    [SerializeField] private float crouchingHeight = 1f;
    [SerializeField] private float cameraOffset = 0.4f;
    [SerializeField] private float crouchSpeedUp = 6f;

    [Header("Look")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 0.2f;
    [SerializeField] private float pitchSmoothTime = 0.03f;
    [SerializeField] private float strafeTiltAngle = 2f;
    [SerializeField] private float tiltSmoothTime = 0.12f;

    [Header("Head Bob")]
    [SerializeField] private float bobFrequency = 6f;
    [SerializeField] private float bobAmplitudeY = 0.05f;
    [SerializeField] private float bobAmplitudeX = 0.025f;
    [SerializeField] private float bobReturnSpeed = 10f;

    [Header("Interaction")]
    public float interactDistance = 2.5f;
    public LayerMask interactableLayer;

    private CharacterController _characterController;
    private PlayerInputActions _input;
    private IInteractable _currentInteractable;
    private string _lastHintText;
    private CrosshairMode _lastCrosshairMode;

    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool _isGrounded;
    private bool _isCrouching;
    private bool _isRunning;
    private float _verticalVelocity;
    private float _targetHeight;

    // Movement inertia
    private Vector3 _horizontalVelocity;
    private Vector3 _velocitySmoothRef;
    private float _speedMultiplier = 1f;

    // Position lock (used to prevent animated objects from pushing the player)
    private bool _positionLocked = false;
    private Vector3 _lockedPosition;

    // Camera look
    private float _targetPitch;
    private float _smoothedPitch;
    private float _pitchSmoothRef;
    private float _currentTilt;
    private float _tiltSmoothRef;

    // Head bob
    private float _bobTimer;
    private Vector3 _bobOffset;
    private Vector3 _bobOffsetSmoothRef;
    private float _baseCameraLocalY;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _input = new PlayerInputActions();
        _targetHeight = standingHeight;
        _baseCameraLocalY = cameraTransform.localPosition.y;
    }

    private void OnEnable()
    {
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
        _input.Player.Crouch.canceled += Crouch;
    }

    /// <summary>Enables or disables all player input. Called by InventoryUI when opening/closing inventory.</summary>
    public void SetPlayerInputEnabled(bool enabled)
    {
        if (enabled)
        {
            _input.Player.Enable();
        }
        else
        {
            _lookInput = Vector2.zero;
            _moveInput = Vector2.zero;
            _input.Player.Disable();
        }
    }

    private void OnDisable()
    {
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
        _input.Player.Crouch.canceled -= Crouch;
    }

    private void Update()
    {
        _isGrounded = _characterController.isGrounded;
        HandleGravity();
        HandleLook();
        HandleMovement();
        HandleHeadBob();
        HandleCrouchTransition();
        HandleInteractionDetection();
        ApplyCameraTransform();
    }

    private void HandleLook()
    {
        float mouseX = _lookInput.x * mouseSensitivity;
        float mouseY = _lookInput.y * mouseSensitivity;

        // Horizontal rotation is instant — standard FPS feel, preserves aim precision
        transform.Rotate(Vector3.up * mouseX);

        // Vertical pitch — slightly smoothed to simulate head mass
        _targetPitch -= mouseY;
        _targetPitch = Mathf.Clamp(_targetPitch, -89f, 89f);
        _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, _targetPitch, ref _pitchSmoothRef, pitchSmoothTime);

        // Subtle camera roll when strafing — body leans into sideways movement
        float targetTilt = -_moveInput.x * strafeTiltAngle;
        _currentTilt = Mathf.SmoothDamp(_currentTilt, targetTilt, ref _tiltSmoothRef, tiltSmoothTime);

        cameraTransform.localRotation = Quaternion.Euler(_smoothedPitch, 0f, _currentTilt);
    }

    private void HandleMovement()
    {
        float currentSpeed = (_isCrouching ? crouchSpeed : _isRunning ? runSpeed : walkSpeed) * _speedMultiplier;
        Vector3 targetVelocity = (transform.right * _moveInput.x + transform.forward * _moveInput.y) * currentSpeed;

        // Separate smooth times: quicker deceleration for snappy stops, gradual acceleration for weight
        float smoothTime = _moveInput.magnitude > 0.01f ? accelerationTime : decelerationTime;
        _horizontalVelocity = Vector3.SmoothDamp(_horizontalVelocity, targetVelocity, ref _velocitySmoothRef, smoothTime);

        Vector3 finalMove = _horizontalVelocity;
        finalMove.y = _verticalVelocity;

        var collisions = _characterController.Move(finalMove * Time.deltaTime);
        if ((collisions & CollisionFlags.Above) != 0)
            _verticalVelocity = initialFallVelocity;

        // Restore XZ position if locked — prevents animated colliders from pushing the player
        if (_positionLocked)
        {
            Vector3 pos = transform.position;
            transform.position = new Vector3(_lockedPosition.x, pos.y, _lockedPosition.z);
        }
    }

    private void HandleHeadBob()
    {
        bool isMoving = _isGrounded && _horizontalVelocity.magnitude > 0.5f;

        if (isMoving)
        {
            float speedFactor = _horizontalVelocity.magnitude / walkSpeed;
            _bobTimer += Time.deltaTime * bobFrequency * speedFactor;

            Vector3 targetBob = new Vector3(
                Mathf.Sin(_bobTimer * 0.5f) * bobAmplitudeX,
                Mathf.Sin(_bobTimer) * bobAmplitudeY,
                0f
            );
            _bobOffset = Vector3.SmoothDamp(_bobOffset, targetBob, ref _bobOffsetSmoothRef, 0.05f);
        }
        else
        {
            // Smoothly return camera to neutral when stopping
            _bobOffset = Vector3.SmoothDamp(_bobOffset, Vector3.zero, ref _bobOffsetSmoothRef, 1f / bobReturnSpeed);
        }
    }

    private void HandleCrouchTransition()
    {
        float currentHeight = _characterController.height;

        if (Mathf.Abs(currentHeight - _targetHeight) < 0.01f)
        {
            _characterController.height = _targetHeight;
        }
        else
        {
            float newHeight = Mathf.Lerp(currentHeight, _targetHeight, crouchSpeedUp * Time.deltaTime);
            _characterController.height = newHeight;
        }

        _characterController.center = new Vector3(0f, _characterController.height * 0.5f, 0f);
        _baseCameraLocalY = _characterController.height - cameraOffset;
    }

    // Combines crouch base position and head bob into final camera local position
    private void ApplyCameraTransform()
    {
        cameraTransform.localPosition = new Vector3(
            _bobOffset.x,
            _baseCameraLocalY + _bobOffset.y,
            cameraTransform.localPosition.z
        );
    }

    private void HandleInteractionDetection()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out IInteractable interactable))
            {
                string newText = interactable.GetInteractText();
                CrosshairMode newMode = interactable.GetCrosshairMode();

                if (_currentInteractable != interactable || newText != _lastHintText || newMode != _lastCrosshairMode)
                {
                    _currentInteractable = interactable;
                    _lastHintText = newText;
                    _lastCrosshairMode = newMode;

                    if (Assets.Scripts.UI.InteractionUI.Instance != null)
                        Assets.Scripts.UI.InteractionUI.Instance.SetHint(
                            true,
                            newText,
                            interactable.IsPickable(),
                            newMode
                        );
                }
                return;
            }
        }

        if (_currentInteractable != null)
        {
            _currentInteractable = null;
            _lastHintText = null;
            _lastCrosshairMode = CrosshairMode.Default;

            if (Assets.Scripts.UI.InteractionUI.Instance != null)
                Assets.Scripts.UI.InteractionUI.Instance.SetHint(false);
        }
    }

    private void HandleGravity()
    {
        if (_isGrounded && _verticalVelocity < 0)
            _verticalVelocity = initialFallVelocity;
        _verticalVelocity += gravity * Time.deltaTime;
    }

    private void StoreMovementInput(InputAction.CallbackContext context) => _moveInput = context.ReadValue<Vector2>();
    private void StoreLookInput(InputAction.CallbackContext context) => _lookInput = context.ReadValue<Vector2>();

    private void Jump(InputAction.CallbackContext context)
    {
        if (_isGrounded)
            _verticalVelocity = jumpForce;
    }

    private void Crouch(InputAction.CallbackContext context)
    {
        _isCrouching = !_isCrouching;
        _targetHeight = _isCrouching ? crouchingHeight : standingHeight;
    }

    private void Sprint(InputAction.CallbackContext context) => _isRunning = context.performed;

    /// <summary>
    /// Locks the player's XZ position for the given duration so animated objects
    /// (drawers, doors) cannot push the character during their animation.
    /// Gravity and vertical movement are unaffected.
    /// </summary>
    public void LockPositionFor(float duration)
    {
        StartCoroutine(LockPositionCoroutine(duration));
    }

    private IEnumerator LockPositionCoroutine(float duration)
    {
        _lockedPosition = transform.position;
        _positionLocked = true;
        yield return new WaitForSeconds(duration);
        _positionLocked = false;
    }

    /// <summary>Returns true while the player is holding the Sprint key.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Scales all movement speeds by the given multiplier (0..1).
    /// Pass 1 to restore normal speed. Used by PhysicsGrabber to slow the player
    /// while dragging heavy objects.
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        _speedMultiplier = Mathf.Clamp01(multiplier);
    }

    private void Interact(InputAction.CallbackContext ctx)
    {
        if (_currentInteractable != null)
        {
            _currentInteractable.Interact();
            // Reset cache so hint refreshes on next frame with updated state
            _currentInteractable = null;
            _lastHintText = null;
            _lastCrosshairMode = CrosshairMode.Default;

            if (Assets.Scripts.UI.InteractionUI.Instance != null)
                Assets.Scripts.UI.InteractionUI.Instance.SetHint(false);
        }
    }
}
