using UnityEngine;
using System.Collections;

namespace Escape.Core {
    /// <summary>
    /// Обрабатывает логику взаимодействия, проверку ключей и состояние двери.
    /// Передает команды компоненту анимации.
    /// </summary>
    public class DoorInteraction : MonoBehaviour, IInteractable {
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
        [Tooltip("Duration of the open/close animation. Colliders are triggers during this time so the player is not pushed.")]
        [SerializeField] private float animationDuration = 0.5f;

        [SerializeField] private DoorAnimator _doorAnimator;

        private FPSController _fpsController;

        private void Start() {
            if (_doorAnimator != null)
                _doorAnimator.SetInitialState(_isOpen);

            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                _fpsController = player.GetComponent<FPSController>();
        }

        public void Interact() {
            if (_isLocked && !_isOpen) {
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    _isLocked = false;
                    ToggleDoor();
                } else {
                    Debug.Log("<color=orange>Взаимодействие: " + _lockedMessage + "</color>");
                    if (!string.IsNullOrEmpty(_requirementHint))
                        InteractionUI.Instance?.ShowBlockedHint(_requirementHint);
                }
            } else {
                ToggleDoor();
            }
        }

        private void ToggleDoor() {
            _isOpen = !_isOpen;
            if (_doorAnimator != null)
                _doorAnimator.PlayAnimation(_isOpen);

            _fpsController?.LockPositionFor(animationDuration);
        }

        public string GetInteractText() {
            return _isOpen ? _closeText : _openText;
        }

        public bool IsPickable() => false;

        /// <summary>Shows a lock icon when the door is locked, hand otherwise.</summary>
        public CrosshairMode GetCrosshairMode()
        {
            if (!_isLocked || _isOpen) return CrosshairMode.Hand;
            bool hasKey = _requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey);
            return hasKey ? CrosshairMode.Unlocked : CrosshairMode.Locked;
        }

        /// <summary>
        /// Unlocks the door programmatically without opening it.
        /// </summary>
        public void Unlock()
        {
            _isLocked = false;
        }

        /// <summary>
        /// Unlocks and immediately opens the door.
        /// Wire this to CodeLock.OnUnlocked in the Inspector.
        /// </summary>
        public void UnlockAndOpen()
        {
            _isLocked = false;
            if (!_isOpen)
                ToggleDoor();
        }
    }
}
