using UnityEngine;

namespace Escape.Core {
    /// <summary>
    /// Отвечает только за визуальное воспроизведение анимации двери.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class DoorAnimator : MonoBehaviour {
        private Animator _animator;
        private const string IS_OPEN_PARAM = "IsOpen";

        private void Awake() {
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// Устанавливает состояние параметра без учета переходов (полезно для инициализации).
        /// </summary>
        public void SetInitialState(bool isOpen) {
            if (_animator != null)
                _animator.SetBool(IS_OPEN_PARAM, isOpen);
        }

        /// <summary>
        /// Запускает анимацию открытия или закрытия.
        /// </summary>
        public void PlayAnimation(bool isOpen) {
            if (_animator != null)
                _animator.SetBool(IS_OPEN_PARAM, isOpen);

            // Здесь можно добавить звуки открытия/закрытия
            // AudioSource.PlayClipAtPoint(isOpen ? openClip : closeClip, transform.position);
        }
    }
}
