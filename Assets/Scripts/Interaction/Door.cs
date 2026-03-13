using UnityEngine;
using System.Collections;

namespace Escape.Core {
    public class Door : MonoBehaviour, IInteractable {
        [Header("Settings")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _smoothSpeed = 5f;

        [Header("Lock System")]
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private ItemData _requiredKey;
        [SerializeField] private string _lockedMessage = "Дверь заперта. Нужен ключ.";

        [Header("UI Hints")]
        [SerializeField] private string _openText = "Открыть дверь";
        [SerializeField] private string _closeText = "Закрыть дверь";

        private Quaternion _closedRotation;
        private Quaternion _targetRotation;
        private bool _showingLockedMessage = false;

        private void Start() {
            _closedRotation = transform.localRotation;
            UpdateTargetRotation();
        }

        private void Update() {
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation,
                _targetRotation,
                Time.deltaTime * _smoothSpeed
            );
        }

        public void Interact() {
            if (_isLocked && !_isOpen) {
                // Проверяем наличие ключа только при нажатии E
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    _isLocked = false;
                    ToggleDoor();
                } else {
                    // Выводим сообщение в консоль (или запустите здесь свою корутину для UI)
                    Debug.Log("<color=red>" + _lockedMessage + "</color>");

                    // Опционально: можно на секунду изменить текст подсказки
                    StartCoroutine(ShowTemporaryLockedMessage());
                }
            } else {
                ToggleDoor();
            }
        }

        /// <summary>
        /// Теперь всегда возвращает стандартный текст, 
        /// если только мы не хотим временно показать "Заперто" после нажатия.
        /// </summary>
        public string GetInteractText() {
            if (_showingLockedMessage) return _lockedMessage;
            return _isOpen ? _closeText : _openText;
        }

        public bool IsPickable() => false;

        private void ToggleDoor() {
            _isOpen = !_isOpen;
            UpdateTargetRotation();
        }

        private void UpdateTargetRotation() {
            _targetRotation = _isOpen
                ? _closedRotation * Quaternion.Euler(0, _openAngle, 0)
                : _closedRotation;
        }

        // Корутина для временной смены текста в UI подсказке
        private IEnumerator ShowTemporaryLockedMessage() {
            _showingLockedMessage = true;
            yield return new WaitForSeconds(1.5f); // Сообщение повисит 1.5 секунды
            _showingLockedMessage = false;
        }
    }
}
