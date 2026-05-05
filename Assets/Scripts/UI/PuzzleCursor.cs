using UnityEngine;
using UnityEngine.InputSystem;

namespace UI
{
    /// <summary>
    /// Manages a fake UI cursor that follows the mouse position.
    /// Hides the hardware cursor when active.
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
            }

            // Initially disabled (only component, not gameObject)
            this.enabled = false;
        }

        private void OnEnable()
        {
            if (_hideHardwareCursor)
            {
                Cursor.visible = false;
            }
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
            if (Camera.main == null || Mouse.current == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            
            // If layer mask is not set in inspector, try to find "Puzzle Interactable" layer, fallback to "Interactable Layer"
            LayerMask mask = _interactableLayer;
            if (mask == 0)
            {
                int puzzleLayer = LayerMask.NameToLayer("Puzzle Interactable");
                if (puzzleLayer != -1) mask = 1 << puzzleLayer;
                else mask = LayerMask.GetMask("Interactable Layer");
            }

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
