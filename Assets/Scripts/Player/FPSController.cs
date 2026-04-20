using UnityEngine;
using System;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour, ISaveable
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
    [Tooltip("Camera sensitivity multiplier applied while the player is dragging a drawer/door. " +
             "Lower values let the mouse drive the object without spinning the view.")]
    [SerializeField] [Range(0f, 1f)] private float _dragCameraSensitivityMultiplier = 0.25f;

    [Header("Head Bob")]
    [SerializeField] private float bobFrequency = 6f;
    [SerializeField] private float bobAmplitudeY = 0.05f;
    [SerializeField] private float bobAmplitudeX = 0.025f;
    [SerializeField] private float bobReturnSpeed = 10f;

    [Header("Interaction")]
    public float interactDistance = 2.5f;
    public LayerMask interactableLayer;

    private CharacterController _characterController;
    private IInteractable _currentInteractable;
    private IDraggable _currentDraggable;
    private string _lastHintText;
    private CrosshairMode _lastCrosshairMode;

    private bool _isGrounded;
    private bool _isCrouching;
    private bool _isRunning;
    private float _verticalVelocity;
    private float _targetHeight;

    // Прыжок: true с момента нажатия пробела до реального приземления.
    // Предотвращает повторный прыжок пока CharacterController.isGrounded
    // ошибочно возвращает true при скольжении по стене.
    private bool _isJumping;

    // Movement inertia
    private Vector3 _horizontalVelocity;
    private Vector3 _velocitySmoothRef;
    private float _speedMultiplier = 1f;

    // Position lock (used to prevent animated objects from pushing the player)
    private bool _positionLocked = false;
    private Vector3 _lockedPosition;

    // Active drag states — used to scale camera sensitivity while interacting
    private bool _isPhysicsGrabbing;

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

    // ── Public state for FootstepController ───────────────────────────────────
    /// <summary>Running value of the head-bob timer (grows with movement).</summary>
    public float BobTimer        => _bobTimer;
    /// <summary>Whether the character is currently touching the ground.</summary>
    public bool  IsGrounded      => _isGrounded;
    /// <summary>Horizontal movement speed this frame.</summary>
    public float HorizontalSpeed => _horizontalVelocity.magnitude;
    /// <summary>Whether the player is in a crouched state.</summary>
    public bool  IsCrouching     => _isCrouching;

    //Croach
    private bool _wantsToStand;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "player";

    private Vector3? _pendingPosition;
    private float    _pendingYaw;

    /// <summary>Serializes player world position and horizontal rotation.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new PlayerSaveData
    {
        x   = transform.position.x,
        y   = transform.position.y,
        z   = transform.position.z,
        yaw = transform.eulerAngles.y,
    });

    /// <summary>Stores pending position. Applied in Start() after CharacterController is ready.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<PlayerSaveData>(json);
        _pendingPosition = new Vector3(data.x, data.y, data.z);
        _pendingYaw = data.yaw;
    }

    /// <summary>Instantly moves the player to the given position and yaw without physics interaction.</summary>
    public void Teleport(Vector3 position, float yaw)
    {
        _characterController.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        _targetPitch  = 0f;
        _smoothedPitch = 0f;
        _characterController.enabled = true;
    }

    [Serializable]
    private struct PlayerSaveData
    {
        public float x, y, z, yaw;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _targetHeight = standingHeight;
        _baseCameraLocalY = cameraTransform.localPosition.y;
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_pendingPosition.HasValue)
        {
            Teleport(_pendingPosition.Value, _pendingYaw);
            _pendingPosition = null;
        }
    }

    private void OnEnable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnInteractPerformed += OnInteract;
            InputManager.Instance.OnJumpPerformed += OnJump;
            InputManager.Instance.OnSprintToggled += OnSprint;
            InputManager.Instance.OnCrouchToggled += OnCrouch;
            InputManager.Instance.OnMenuPerformed += OnMenuPressed;
        }
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnInteractPerformed -= OnInteract;
            InputManager.Instance.OnJumpPerformed -= OnJump;
            InputManager.Instance.OnSprintToggled -= OnSprint;
            InputManager.Instance.OnCrouchToggled -= OnCrouch;
            InputManager.Instance.OnMenuPerformed -= OnMenuPressed;
        }
        SaveManager.Instance?.Unregister(this);
    }

    private void OnMenuPressed()
    {
     //   UIManager.Instance?.TogglePanel(Escape.UI.PanelType.PauseMenu);
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
        HandleDragInteraction();
        ApplyCameraTransform();
    }

    /// <summary>Enables or disables all player input. Called by InventoryUI when opening/closing inventory.</summary>
    public void SetPlayerInputEnabled(bool enabled) {
        InputManager.Instance?.SetPlayerInputEnabled(enabled);
    }

    private void HandleLook()
    {
        Vector2 lookInput = InputManager.Instance != null ? InputManager.Instance.LookInput : Vector2.zero;
        float sensitivity = mouseSensitivity
            * ((_currentDraggable != null || _isPhysicsGrabbing) ? _dragCameraSensitivityMultiplier : 1f)
            * (CameraZoom.Instance != null ? CameraZoom.Instance.SensitivityMultiplier : 1f);
        float mouseX = lookInput.x * sensitivity;
        float mouseY = lookInput.y * sensitivity;

        // Horizontal rotation is instant — standard FPS feel, preserves aim precision
        transform.Rotate(Vector3.up * mouseX);

        // Vertical pitch — slightly smoothed to simulate head mass
        _targetPitch -= mouseY;
        _targetPitch = Mathf.Clamp(_targetPitch, -89f, 89f);
        _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, _targetPitch, ref _pitchSmoothRef, pitchSmoothTime);

        // Subtle camera roll when strafing — body leans into sideways movement
        Vector2 moveInput = InputManager.Instance != null ? InputManager.Instance.MoveInput : Vector2.zero;
        float targetTilt = -moveInput.x * strafeTiltAngle;
        _currentTilt = Mathf.SmoothDamp(_currentTilt, targetTilt, ref _tiltSmoothRef, tiltSmoothTime);

        cameraTransform.localRotation = Quaternion.Euler(_smoothedPitch, 0f, _currentTilt);
    }

    private void HandleMovement()
    {
        Vector2 moveInput = InputManager.Instance != null ? InputManager.Instance.MoveInput : Vector2.zero;
        float currentSpeed = (_isCrouching ? crouchSpeed : _isRunning ? runSpeed : walkSpeed) * _speedMultiplier;
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y) * currentSpeed;

        // Separate smooth times: quicker deceleration for snappy stops, gradual acceleration for weight
        float smoothTime = moveInput.magnitude > 0.01f ? accelerationTime : decelerationTime;
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

            // Y frequency is doubled so one full vertical bob matches one footstep (period = π).
            // X frequency keeps the classic 2:1 ratio (left-right sway per two steps).
            Vector3 targetBob = new Vector3(
                Mathf.Sin(_bobTimer)        * bobAmplitudeX,
                Mathf.Sin(_bobTimer * 2f)   * bobAmplitudeY,
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
        if (_wantsToStand && _isCrouching) {
            if (CanStandUp()) {
                _targetHeight = standingHeight;
                _wantsToStand = false;
                _isCrouching = false;
            } else {
                // Если не можем встать, остаемся в положении сидя
                _targetHeight = crouchingHeight;
            }
        }

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

    /// <summary>
    /// Two-pass raycast for interaction detection.
    ///
    /// Ray 1 (interactableLayer only): finds the nearest object the player can interact with.
    /// Ray 2 (all layers except interactableLayer and IgnoreRaycast): checks for solid
    ///        obstacles between the camera and the found object. Blocked = no interaction.
    ///
    /// Component resolution order on the hit object:
    ///   1. GetComponent&lt;IDraggable&gt;()             — drawers, doors (direct object only, NO GetComponentInParent
    ///                                                 to avoid claiming child items as part of the parent drawer)
    ///   2. TryGetComponent&lt;IInteractable&gt;()       — PickableItem, code locks, etc.
    ///   3. GetComponentInParent&lt;IInteractable&gt;()  — levers / gauges whose collider lives on a child
    ///
    /// Layer rules:
    ///   • Interactable objects (drawers, doors, pickables, levers) → Interactable Layer
    ///   • Furniture bodies, walls, shelves                         → Default
    ///   • Triggers / zones that must be ignored                    → Ignore Raycast
    ///
    /// WARNING: never put a furniture body (e.g. desk mesh) on Interactable Layer — Ray 2
    /// skips that layer entirely, so the body won't block interaction through closed panels.
    /// </summary>
    private void HandleInteractionDetection()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen)
            return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        // Ray 1 — find the nearest object on the Interactable Layer.
        if (!Physics.Raycast(ray, out RaycastHit interactHit, interactDistance, interactableLayer, QueryTriggerInteraction.Ignore))
        {
            ClearCurrentInteractable();
            return;
        }

        // Ray 2 — obstacle check: any solid non-interactable geometry between the camera
        // and the hit point blocks interaction (e.g. closed shelf, locked door body).
        int obstacleMask = ~interactableLayer.value & ~(1 << 2); // all except interactable layer and IgnoreRaycast
        if (Physics.Raycast(ray, out RaycastHit _, interactHit.distance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            ClearCurrentInteractable();
            return;
        }

        // Path is clear — resolve the IInteractable / IDraggable component.
        // IDraggable is checked only on the directly hit object so that DrawerDrag on a
        // parent drawer is NOT wrongly claimed when the ray hits a child (e.g. FlashLight).
        // IInteractable uses GetComponentInParent so levers/gauges whose collider is a
        // child still resolve to the script on the parent.
        IInteractable interactable = null;
        IDraggable draggable = interactHit.collider.GetComponent<IDraggable>();
        if (draggable is IInteractable draggableInteractable)
            interactable = draggableInteractable;
        else if (!interactHit.collider.TryGetComponent(out interactable))
            interactable = interactHit.collider.GetComponentInParent<IInteractable>();

        if (interactable != null)
        {
            // If the object signals it cannot currently be interacted with, treat it as
            // non-interactable — clear hint and skip setting _currentInteractable.
            if (!interactable.CanInteract())
            {
                ClearCurrentInteractable();
                return;
            }

            string newText      = interactable.GetInteractText();
            CrosshairMode newMode = interactable.GetCrosshairMode();

            if (_currentInteractable != interactable || newText != _lastHintText || newMode != _lastCrosshairMode)
            {
                _currentInteractable = interactable;
                _lastHintText        = newText;
                _lastCrosshairMode   = newMode;
                InteractionUI.Instance?.SetHint(true, newText, interactable.IsPickable(), newMode);
            }
            return;
        }

        ClearCurrentInteractable();
    }

    private void ClearCurrentInteractable()
    {
        if (_currentInteractable != null)
        {
            _currentInteractable = null;
            _lastHintText        = null;
            _lastCrosshairMode   = CrosshairMode.Default;
            InteractionUI.Instance?.SetHint(false);
        }
    }

    private void HandleGravity()
    {
        if (_isGrounded && _verticalVelocity < 0)
        {
            _verticalVelocity = initialFallVelocity;
            _isJumping = false; // персонаж реально приземлился — разрешаем следующий прыжок
        }
        _verticalVelocity += gravity * Time.deltaTime;
    }

    private void OnJump() {
        if (_isGrounded && !_isJumping)
        {
            _verticalVelocity = jumpForce;
            _isJumping = true;
        }
    }

    private void OnCrouch(bool performed) {
        if (performed) {
            // Нажали на кнопку приседания - приседаем
            _isCrouching = true;
            _targetHeight = crouchingHeight;
            _wantsToStand = false;
        } else {
            // Отпустили кнопку - хотим встать
            _wantsToStand = true;

            // Пытаемся встать сразу, если есть место
            if (CanStandUp()) {
                _isCrouching = false;
                _targetHeight = standingHeight;
                _wantsToStand = false;
            } else {
                // Если нет места, остаемся в положении сидя
                _isCrouching = true;
                _targetHeight = crouchingHeight;

                // Здесь можно добавить звуковой сигнал или визуальный индикатор,
                // что встать невозможно
                Debug.Log("Cannot stand up - obstacle above!");
            }
        }
    }

    bool CanStandUp() {
        float checkDistance = standingHeight - crouchingHeight; // Дистанция проверки вверх
        bool _canStandUp = !Physics.Raycast(transform.position + Vector3.up * crouchingHeight, Vector3.up, checkDistance);
        return _canStandUp;
    }

    private void OnSprint(bool performed) => _isRunning = performed;

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

    /// <summary>
    /// Called by PhysicsGrabber when a physics object is grabbed or released.
    /// Reduces camera sensitivity while dragging for better control feel.
    /// </summary>
    public void SetPhysicsGrabActive(bool active)
    {
        _isPhysicsGrabbing = active;
    }

    /// <summary>
    /// Сбрасывает кеш обнаружения интерактивных объектов.
    /// Следующий кадр Update() заново определит на что смотрит игрок и обновит подсказку.
    /// </summary>
    public void ResetInteractionCache()
    {
        _currentInteractable = null;
        _lastHintText = null;
        _lastCrosshairMode = CrosshairMode.Default;
    }

    /// <summary>
    /// Handles LMB interactions: drag for IDraggable, single click Interact() for UseLMBClick objects.
    /// </summary>
    private void HandleDragInteraction()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        // Do not start new interactions while any UI panel is open.
        // Active drags are still allowed to finish so release events are not lost.
        bool panelOpen = UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen;

        if (mouse.leftButton.wasPressedThisFrame && _currentDraggable == null && !panelOpen)
        {
            if (_currentInteractable is IDraggable draggable)
            {
                // Start drag (drawer, door, etc.) — pass world hit point so the object
                // can determine the correct drag direction from any camera angle.
                Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
                Vector3 hitPoint = Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer)
                    ? hit.point
                    : cameraTransform.position + cameraTransform.forward * interactDistance;

                _currentDraggable = draggable;
                _currentDraggable.OnDragStart(hitPoint);
            }
            else if (_currentInteractable != null && _currentInteractable.UseLMBClick)
            {
                // Single click: notes, pickups, etc.
                _currentInteractable.Interact();
                _currentInteractable = null;
                _lastHintText = null;
                _lastCrosshairMode = CrosshairMode.Default;
                InteractionUI.Instance?.SetHint(false);
            }
        }

        // While dragging: feed mouse delta to the drawer
        if (_currentDraggable != null)
        {
            if (mouse.leftButton.isPressed)
            {
                _currentDraggable.OnDrag(mouse.delta.ReadValue());
            }
            else
            {
                // LMB released — end drag and snap
                _currentDraggable.OnDragEnd();
                _currentDraggable = null;
            }
        }
    }

    private void OnInteract()
    {
        if (_currentInteractable != null && !_currentInteractable.UseLMBClick)
        {
            _currentInteractable.Interact();
            _currentInteractable = null;
            _lastHintText = null;
            _lastCrosshairMode = CrosshairMode.Default;

            InteractionUI.Instance?.SetHint(false);
        }
    }
}

