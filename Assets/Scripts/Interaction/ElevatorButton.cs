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
        [Tooltip("Ссылка на контроллер лифта. Если пусто — ищется на родительском объекте.")]
        [SerializeField] private ElevatorController _controller;

        [Header("UI Hints")]
        [SerializeField] private string _hintText = "Нажать кнопку";

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

            _controller.MoveToFloor(_floorIndex);
        }

        public bool CanInteract()
        {
            if (_controller == null) return false;
            return _controller.HasPower;
        }

        public string GetInteractText()
        {
            if (_controller != null && !_controller.HasPower)
                return _controller.NoPowerHint;
            return _hintText;
        }

        public bool IsPickable() => false;

        public bool UseLMBClick => true;

        public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
    }
}
