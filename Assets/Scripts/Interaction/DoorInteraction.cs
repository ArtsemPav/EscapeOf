using UnityEngine;
using System.Collections;

namespace Escape.Core {
    /// <summary>
    /// Обрабатывает логику взаимодействия, проверку ключей и состояние двери.
    /// Поддерживает физическое перетаскивание (IDraggable): удерживай LMB и двигай мышью.
    /// </summary>
    public class DoorInteraction : MonoBehaviour, IInteractable, IDraggable {

        [Header("Door State")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private bool _isLocked = false;

        [Header("Lock Settings")]
        [SerializeField] private ItemData _requiredKey;
        [SerializeField] private string _lockedMessage = "Дверь заперта. Нужен ключ.";
        [Tooltip("Shown below the action hint when the door is locked and the player doesn't have the key.")]
        [SerializeField] private string _requirementHint = "";

        [Header("UI Hints")]
        [SerializeField] private string _openText = "Открыть дверь";
        [SerializeField] private string _closeText = "Закрыть дверь";

        [Header("Animation")]
        [SerializeField] private DoorAnimator _doorAnimator;

        [Header("Drag Physics")]
        [Tooltip("How far the door swings when fully open (degrees). Negative = swings the other way.")]
        [SerializeField] private float _maxOpenAngle = 90f;
        [Tooltip("How much each pixel of mouse movement adds to angular velocity. Lower = heavier.")]
        [SerializeField] private float _dragSensitivity = 0.4f;
        [Tooltip("Angular velocity damping per second. Higher = stops faster.")]
        [SerializeField] private float _friction = 5f;
        [Tooltip("Max angular velocity (open-fraction / sec).")]
        [SerializeField] private float _maxVelocity = 1.2f;
        [Tooltip("Speed at which door snaps to open/closed after releasing LMB.")]
        [SerializeField] private float _snapSpeed = 2f;
        [Tooltip("If open fraction exceeds this on release, door snaps fully open; otherwise closes.")]
        [SerializeField] [Range(0f, 1f)] private float _snapThreshold = 0.35f;

        // ── Drag state ──────────────────────────────────────────────────────────

        private Animator    _animator;
        private float       _closedLocalEulerY;
        private float       _openFraction;     // 0 = closed, 1 = fully open
        private float       _targetFraction;
        private float       _velocity;         // open-fraction per second
        private bool        _isDragging;
        private Vector2     _screenSwingDir;   // screen-space tangent of door's swing arc

        // ── Unity ───────────────────────────────────────────────────────────────

        private void Start() {
            if (_doorAnimator != null) {
                _doorAnimator.SetInitialState(_isOpen);
                _animator            = _doorAnimator.GetComponent<Animator>();
                _closedLocalEulerY   = _doorAnimator.transform.localEulerAngles.y;
                _openFraction        = _isOpen ? 1f : 0f;
                _targetFraction      = _openFraction;
            }
        }

        private void Update() {
            // Only run drag physics when the Animator has been handed over to us.
            if (_animator == null || _animator.enabled) return;

            // Apply velocity to open-fraction.
            if (Mathf.Abs(_velocity) > 0.0001f) {
                _openFraction = Mathf.Clamp01(_openFraction + _velocity * Time.deltaTime);
                _velocity    *= Mathf.Clamp01(1f - _friction * Time.deltaTime);
                ApplyAngle();
                if (!_isDragging) return;
            }

            if (_isDragging) return;

            // Velocity settled — smoothly snap to nearest rest position.
            _openFraction = Mathf.Lerp(_openFraction, _targetFraction, _snapSpeed * Time.deltaTime);
            ApplyAngle();
        }

        // ── IDraggable ──────────────────────────────────────────────────────────

        /// <summary>Called by FPSController when LMB is pressed while looking at the door.</summary>
        public void OnDragStart() {
            if (_isLocked && !_isOpen) return;

            _isDragging = true;

            // Disable Animator so we control rotation directly.
            if (_animator != null)
                _animator.enabled = false;

            // Project the door's swing tangent (local Z = direction the edge moves) onto screen.
            // This makes drag feel correct from any approach angle.
            Camera cam = Camera.main;
            if (cam != null && _doorAnimator != null) {
                Transform pivot = _doorAnimator.transform;
                Vector3 swingTangent = pivot.TransformDirection(Vector3.forward);
                Vector3 screenA = cam.WorldToScreenPoint(pivot.position);
                Vector3 screenB = cam.WorldToScreenPoint(pivot.position + swingTangent * 0.5f);
                Vector2 dir     = new Vector2(screenB.x - screenA.x, screenB.y - screenA.y);
                _screenSwingDir = dir.sqrMagnitude > 1f ? dir.normalized : Vector2.right;
            } else {
                _screenSwingDir = Vector2.right;
            }
        }

        /// <summary>Called every frame while LMB is held.</summary>
        public void OnDrag(Vector2 mouseDelta) {
            if (_isLocked && !_isOpen) return;

            float input = Vector2.Dot(mouseDelta, _screenSwingDir);
            _velocity  += input * _dragSensitivity;
            _velocity   = Mathf.Clamp(_velocity, -_maxVelocity, _maxVelocity);
        }

        /// <summary>Called when LMB is released. Door coasts then snaps.</summary>
        public void OnDragEnd() {
            _isDragging     = false;
            _targetFraction = _openFraction >= _snapThreshold ? 1f : 0f;
            _isOpen         = _targetFraction > 0.5f;
        }

        // ── IInteractable ───────────────────────────────────────────────────────

        /// <summary>Kept for locked-door E-key fallback; drag is the primary interaction.</summary>
        public void Interact() {
            if (_isLocked && !_isOpen) {
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    _isLocked = false;
                } else {
                    Debug.Log("<color=orange>Взаимодействие: " + _lockedMessage + "</color>");
                    if (!string.IsNullOrEmpty(_requirementHint))
                        InteractionUI.Instance?.ShowBlockedHint(_requirementHint);
                }
            }
        }

        public string GetInteractText() {
            return _isOpen ? _closeText : _openText;
        }

        public bool IsPickable() => false;
        public bool UseLMBClick  => false; // drag-only; Interact() used only for locked-door hint

        /// <summary>Shows a lock icon when the door is locked, hand otherwise.</summary>
        public CrosshairMode GetCrosshairMode() {
            if (!_isLocked || _isOpen) return CrosshairMode.Grab;
            bool hasKey = _requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey);
            return hasKey ? CrosshairMode.Unlocked : CrosshairMode.Locked;
        }

        public string GetBlockedHint() {
            if (_isLocked && !_isOpen && (_requiredKey == null || !InventorySystem.Instance.HasItem(_requiredKey)))
                return _requirementHint;
            return string.Empty;
        }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Unlocks the door programmatically without opening it.</summary>
        public void Unlock() { _isLocked = false; }

        /// <summary>Unlocks and immediately opens the door. Wire to CodeLock.OnUnlocked.</summary>
        public void UnlockAndOpen() {
            _isLocked = false;
            if (!_isOpen) {
                _isOpen         = true;
                _targetFraction = 1f;
                if (_animator != null && _animator.enabled)
                    _doorAnimator.PlayAnimation(true);
            }
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        private void ApplyAngle() {
            if (_doorAnimator == null) return;
            Transform pivot = _doorAnimator.transform;
            Vector3 e       = pivot.localEulerAngles;
            e.y             = _closedLocalEulerY + _openFraction * _maxOpenAngle;
            pivot.localEulerAngles = e;
        }
    }
}
