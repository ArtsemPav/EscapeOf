using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Manages a fake UI cursor that follows the mouse position.
    /// Hides the hardware cursor when active.
    /// Supports sprite changes for hover, drag, and default states.
    /// </summary>
    public class PuzzleCursor : MonoBehaviour
    {
        public static PuzzleCursor Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private RectTransform _cursorTransform;
        [SerializeField] private bool _hideHardwareCursor = true;

        [Header("Interaction")]
        [SerializeField] private LayerMask _interactableLayer;
        [SerializeField] private float _interactDistance = 2.5f;

        private Canvas _canvas;
        private Vector2 _originalAnchoredPosition;
        private Image _cursorImage;

        // State
        private bool _isDragging;
        private bool _isOverUI; // New flag to block 3D hover from overriding UI hover
        private ItemData _draggedItem;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            _canvas = GetComponentInParent<Canvas>();

            if (_cursorTransform == null)
            {
                _cursorTransform = GetComponent<RectTransform>();
            }

            if (_cursorTransform != null)
            {
                _originalAnchoredPosition = _cursorTransform.anchoredPosition;
                _cursorImage = _cursorTransform.GetComponent<Image>();
            }

            // ApplyCursorSprite(_cursorDefault);

            // Initially disabled (only component, not gameObject)
            this.enabled = false;
        }

        private void OnEnable()
        {
            if (_hideHardwareCursor)
            {
                Cursor.visible = false;
            }

            // Ensure the cursor stays on top of other UI elements in the same Canvas
            transform.SetAsLastSibling();

            // Ensure we start with the default sprite and clean state
            _isDragging = false;
            _draggedItem = null;
            // ApplyCursorSprite(_cursorDefault);
        }

        private void OnDisable()
        {
            if (_cursorTransform != null)
            {
                _cursorTransform.anchoredPosition = _originalAnchoredPosition;
            }

            if (_hideHardwareCursor)
            {
                Cursor.visible = true;
            }

            // Clean up drag state when disabled
            _isDragging = false;
            _draggedItem = null;
        }

        private void Update()
        {
            if (_cursorTransform == null || Mouse.current == null) return;

            Vector2 mousePosition = Mouse.current.position.ReadValue();

            if (_canvas == null) return;

            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _cursorTransform.position = mousePosition;
            }
            else
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.transform as RectTransform,
                    mousePosition,
                    _canvas.worldCamera,
                    out Vector2 localPoint);

                _cursorTransform.localPosition = localPoint;
            }
            // Block 3D interaction if the pointer is over a UI element
            if (UnityEngine.EventSystems.EventSystem.current != null && 
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                if (!_isOverUI)
                {
                    _isOverUI = true;
                    // Force clean interaction state when entering UI
                    InteractionUI.Instance?.SetHint(false);
                    InteractionUI.Instance?.SetCrosshair(CrosshairMode.Default);
                }
                return;
            }

            _isOverUI = false;
            HandleInteraction();
        }

        public void SwithCursor(bool _hideHardwareCursor) {

        }
        private void HandleInteraction()
        {
            Camera eventCamera = (_canvas != null && _canvas.worldCamera != null) ? _canvas.worldCamera : Camera.main;
            if (eventCamera == null || Mouse.current == null) return;

            Ray ray = eventCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            // Strictly use PuzzleInteractable layer to avoid triggering FPS interactables through UI
            LayerMask mask = _interactableLayer;
            if (mask == 0)
            {
                int puzzleLayer = LayerMask.NameToLayer("PuzzleInteractable");
                if (puzzleLayer != -1) mask = 1 << puzzleLayer;
            }

            // If no valid mask is found, skip interaction to prevent accidental triggers
            if (mask == 0) return;

            if (_isDragging)
            {
                HandleDragInteraction(ray, mask);
                return;
            }

            HandleStandardInteraction(ray, mask);
        }

        private void HandleDragInteraction(Ray ray, LayerMask mask)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _interactDistance, mask))
            {
                IPuzzleDropTarget dropTarget = hit.collider.GetComponent<IPuzzleDropTarget>();
                if (dropTarget == null)
                    dropTarget = hit.collider.GetComponentInParent<IPuzzleDropTarget>();

                if (dropTarget != null)
                {
                    string itemName = _draggedItem != null ? _draggedItem.itemName : string.Empty;
                    InteractionUI.Instance?.ShowDragHint(itemName, dropTarget.GetDropHint());
                    return;
                }
            }

            InteractionUI.Instance?.HideDragHint();
        }

        private void HandleStandardInteraction(Ray ray, LayerMask mask)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _interactDistance, mask))
            {
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                if (interactable == null)
                    interactable = hit.collider.GetComponentInParent<IInteractable>();

                if (interactable != null && interactable.CanInteract())
                {
                    string text = interactable.GetInteractText();
                    CrosshairMode mode = interactable.GetCrosshairMode();
                    InteractionUI.Instance?.SetHint(true, text, interactable.IsPickable(), mode);
                    
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        interactable.Interact();
                    }
                    return;
                }
            }

            InteractionUI.Instance?.SetHint(false);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Switches the cursor into drag mode or back to default.
        /// Call from PuzzleInventoryBar on begin/end drag.
        /// </summary>
        public void SetDragMode(bool isDragging, ItemData draggedItem)
        {
            _isDragging = isDragging;
            _draggedItem = draggedItem;

            if (!isDragging)
            {
                InteractionUI.Instance?.HideDragHint();
            }
        }

        /// <summary>
        /// Changes the crosshair to Hand when hovering over a slot that has an item,
        /// and restores Default on exit. Drag mode takes priority.
        /// Call from PuzzleInventorySlot on OnPointerEnter / OnPointerExit.
        /// </summary>
        public void SetSlotHover(bool isHover)
        {
            if (_isDragging) return;

            InteractionUI.Instance?.SetCrosshair(isHover ? CrosshairMode.Hand : CrosshairMode.Default);
        }

        /// <summary>
        /// Switches the crosshair to Grab while the mouse button is held down on a slot with an item.
        /// Hand is restored on release unless the cursor left the slot.
        /// Call from PuzzleInventorySlot on OnPointerDown / OnPointerUp.
        /// </summary>
        public void SetSlotPress(bool isPressed)
        {
            if (_isDragging) return;

            InteractionUI.Instance?.SetCrosshair(isPressed ? CrosshairMode.Grab : CrosshairMode.Hand);
        }
        public void Show()
        {
            this.enabled = true;
        }

        public void Hide()
        {
            this.enabled = false;
        }
    }
}
