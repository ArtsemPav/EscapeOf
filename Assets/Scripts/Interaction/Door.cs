using UnityEngine;
// Удалено using Escape.Inventory; так как пространство имен не используется в проекте

namespace Escape.Core {
    /// <summary>
    /// Компонент для управления дверью с поддержкой проверки ключа в инвентаре.
    /// </summary>
    public class Door : MonoBehaviour, IInteractable {
        [Header("Settings")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _smoothSpeed = 5f;

        [Header("Lock System")]
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private ItemData _requiredKey; // Ссылка на ScriptableObject ключа
        [SerializeField] private string _lockedMessage = "Дверь заперта. Нужен ключ.";

        [Header("UI Hints")]
        [SerializeField] private string _openText = "Открыть дверь";
        [SerializeField] private string _closeText = "Закрыть дверь";

        private Quaternion _closedRotation;
        private Quaternion _targetRotation;

        private void Start() {
            _closedRotation = transform.localRotation;
            UpdateTargetRotation();
        }

        private void Update() {
            // Плавная анимация поворота
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation,
                _targetRotation,
                Time.deltaTime * _smoothSpeed
            );
        }

        /// <summary>
        /// Вызывается при взаимодействии (клавиша E).
        /// </summary>
        public void Interact() {
            if (_isLocked && !_isOpen) {
                // Проверяем наличие ключа через глобальный InventorySystem.Instance
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    _isLocked = false; // Отпираем дверь
                    ToggleDoor();
                } else {
                    Debug.Log(_lockedMessage);
                }
            } else {
                ToggleDoor();
            }
        }

        public string GetInteractText() {
            if (_isLocked && !_isOpen) {
                return _lockedMessage;
            }
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
    }
}
