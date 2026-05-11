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

        [Header("Cursor Sprites")]
        [SerializeField] private Sprite _cursorDefault;
        [SerializeField] private Sprite _cursorHover;
        [SerializeField] private Sprite _cursorDrag;
        [SerializeField] private Sprite _cursorHand;
        [SerializeField] private Sprite _cursorRead;

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

            ApplyCursorSprite(_cursorDefault);

            // Initially disabled (only component, not gameObject)
            this.enabled = false;
        }

        private void OnEnable()
        {
            if (_hideHardwareCursor)
            {
                Cursor.visible = false;
            }

            // Ensure we start with the default sprite and clean state
            _isDragging = false;
            _draggedItem = null;
            ApplyCursorSprite(_cursorDefault);
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

            HandleInteraction();
        }

        private void HandleInteraction()
        {
            Camera eventCamera = (_canvas != null && _canvas.worldCamera != null) ? _canvas.worldCamera : Camera.main;
            if (eventCamera == null || Mouse.current == null) return;

            Ray ray = eventCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            // If layer mask is not set in inspector, try to find "PuzzleInteractable" layer, fallback to "Interactable Layer"
            LayerMask mask = _interactableLayer;
            if (mask == 0)
            {
                int puzzleLayer = LayerMask.NameToLayer("PuzzleInteractable");
                if (puzzleLayer != -1) mask = 1 << puzzleLayer;
                else mask = LayerMask.GetMask("Interactable Layer");
            }

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
            // If we are hovering over UI, let the UI events control the sprite.
            if (_isOverUI) return;

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
                    
                    // Select appropriate cursor sprite based on mode
                    Sprite cursorSprite = mode switch
                    {
                        CrosshairMode.Hand => _cursorHand != null ? _cursorHand : _cursorHover,
                        CrosshairMode.Read => _cursorRead != null ? _cursorRead : _cursorHover,
                        _ => _cursorHover
                    };
                    ApplyCursorSprite(cursorSprite);

                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        interactable.Interact();
                    }
                    return;
                }
            }

            InteractionUI.Instance?.SetHint(false);
            ApplyCursorSprite(_cursorDefault);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Switches the cursor into drag mode (cursorDrag sprite) or back to default.
        /// Call from PuzzleInventoryBar on begin/end drag.
        /// </summary>
        public void SetDragMode(bool isDragging, ItemData draggedItem)
        {
            _isDragging = isDragging;
            _draggedItem = draggedItem;

            if (isDragging)
            {
                ApplyCursorSprite(_cursorDrag);
            }
            else
            {
                // Return to hover if still over UI, otherwise default
                ApplyCursorSprite(_isOverUI ? _cursorHover : _cursorDefault);
                InteractionUI.Instance?.HideDragHint();
            }
        }

        /// <summary>
        /// Switches the cursor sprite to hover state when the pointer is over a slot with an item.
        /// Drag mode takes priority — hover sprite is not applied while dragging.
        /// Call from PuzzleInventorySlot on OnPointerEnter / OnPointerExit.
        /// </summary>
        public void SetHoverSprite(bool isHover)
        {
            // FIX: If we are dragging, ignore UI hover exit events. 
            // The drag state itself handles the cursor sprite.
            if (_isDragging) return;

            _isOverUI = isHover;
            ApplyCursorSprite(isHover ? _cursorHover : _cursorDefault);
        }

        public void Show()
        {
            this.enabled = true;
        }

        public void Hide()
        {
            this.enabled = false;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void ApplyCursorSprite(Sprite sprite)
        {
            if (_cursorImage == null) return;
            _cursorImage.sprite = sprite;
        }
    }
}
