using UnityEngine;

namespace Escape.Interaction
{
    /// <summary>
    /// Кнопка внутри кабины лифта. Реализует IInteractable — по клику вызывает
    /// ElevatorController.MoveToFloor с привязанным индексом этажа.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorButton : MonoBehaviour, IInteractable
    {
        [Header("Target")]
        [Tooltip("Индекс этажа: 0 = подвал, 1 = 1-й этаж, 2 = 2-й этаж.")]
        [SerializeField] private int _floorIndex;

        /// <summary>Этаж, к которому привязана эта кнопка.</summary>
        public int FloorIndex => _floorIndex;
        [Tooltip("Ссылка на контроллер лифта. Если пусто — ищется на родительском объекте.")]
        [SerializeField] private ElevatorController _controller;

        [Header("UI Hints")]
        [Tooltip("Localization key for the button hint. Override per instance for a floor-specific label (e.g. 'Морг'). Empty value uses the generic press-button hint.")]
        [SerializeField] private string _hintTextKey = LocalizationKeys.InteractPressButton;

        private bool _lastCallAccepted;

        /// <summary>Результат последнего вызова MoveToFloor: true — запрос принят.</summary>
        public bool LastCallAccepted => _lastCallAccepted;

        private void Awake()
        {
            if (_controller == null)
                _controller = GetComponentInParent<ElevatorController>();
        }

        /// <summary>Called by FPSController when the player interacts with this button.</summary>
        public void Interact()
        {
            if (_controller == null)
            {
                Debug.LogWarning($"[{nameof(ElevatorButton)}] No ElevatorController assigned on {name}.", this);
                return;
            }

            // MoveToFloor checks HasPower internally — the button press animation
            // still plays so the player understands the button exists but is inactive.
            _lastCallAccepted = _controller.MoveToFloor(_floorIndex);
        }

        /// <summary>Returns the localized button hint or the controller's localized power hint.</summary>
        public string GetInteractText()
        {
            if (_controller != null && !_controller.HasPower)
                return _controller.NoPowerHint;

            return Localization.Get(string.IsNullOrEmpty(_hintTextKey)
                ? LocalizationKeys.InteractPressButton
                : _hintTextKey);
        }

        public bool IsPickable() => false;

        public bool UseLMBClick => true;

        public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
    }
}
