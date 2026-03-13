using UnityEngine;
using System.Collections;
using System;

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

        [Header("UI Hints")]
        [SerializeField] private string _openText = "Открыть дверь";
        [SerializeField] private string _closeText = "Закрыть дверь";

        // Ссылка на компонент анимации (может быть на этом же объекте или дочернем)
        [SerializeField] private DoorAnimator _doorAnimator;
        private bool _showingLockedMessage = false;

        private void Start() {
            // Устанавливаем начальное состояние в аниматоре без проигрывания анимации входа
            if (_doorAnimator != null)
                _doorAnimator.SetInitialState(_isOpen);
        }

        public void Interact() {
            if (_isLocked && !_isOpen) {
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    _isLocked = false;
                    ToggleDoor();
                } else {
                    Debug.Log("<color=orange>Взаимодействие: " + _lockedMessage + "</color>");
                    StartCoroutine(ShowTemporaryLockedMessage());
                }
            } else {
                ToggleDoor();
            }
        }

        private void ToggleDoor() {
            _isOpen = !_isOpen;
            if (_doorAnimator != null)
                _doorAnimator.PlayAnimation(_isOpen);
        }

        public string GetInteractText() {
            if (_showingLockedMessage) return _lockedMessage;
            return _isOpen ? _closeText : _openText;
        }

        public bool IsPickable() => false;

        /// <summary>Shows a lock icon when the door is locked, hand otherwise.</summary>
        public CrosshairMode GetCrosshairMode() => (_isLocked && !_isOpen) ? CrosshairMode.Locked : CrosshairMode.Hand;

        private IEnumerator ShowTemporaryLockedMessage() {
            _showingLockedMessage = true;
            yield return new WaitForSeconds(1.5f);
            _showingLockedMessage = false;
        }
    }
}
