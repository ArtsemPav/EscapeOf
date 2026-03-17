using UnityEngine;
using UnityEngine.InputSystem;

namespace Escape.Core {
    /// <summary>
    /// Обрабатывает логику взаимодействия, проверку ключей и состояние двери.
    /// Поддерживает физическое перетаскивание (IDraggable): удерживай LMB и двигай мышью.
    /// Заперта дверь — слегка поддаётся при попытке потянуть и возвращается назад.
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
        [Tooltip("Максимальный угол подёргивания при попытке открыть запертую дверь (доля от _maxOpenAngle).")]
        [SerializeField] [Range(0f, 0.15f)] private float _lockedJiggleFraction = 0.05f;

        [Header("UI Hints")]
        [SerializeField] private string _openText = "Открыть дверь";
        [SerializeField] private string _closeText = "Закрыть дверь";

        [Header("Pivot")]
        [Tooltip("Transform двери, который физически вращается. Обычно — родительский объект с петлёй.")]
        [SerializeField] private Transform _pivot;

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
        [SerializeField] private float _snapSpeed = 8f;
        [Tooltip("If open fraction exceeds this on release, door snaps fully open; otherwise closes.")]
        [SerializeField] [Range(0f, 1f)] private float _snapThreshold = 0.35f;
        [Tooltip("Min velocity at release to trigger a binary snap to open/close. " +
                 "Below this threshold door stays wherever it naturally coasts to.")]
        [SerializeField] private float _snapVelocityThreshold = 0.35f;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Звук воспроизводится когда дверь досылается в открытое положение.")]
        [SerializeField] private AudioClip _openClip;
        [Tooltip("Звук воспроизводится когда дверь закрывается.")]
        [SerializeField] private AudioClip _closeClip;
        [Tooltip("Звук подёргивания запертой двери.")]
        [SerializeField] private AudioClip _lockedClip;
        [Tooltip("Звук щелчка замка при вводе правильного кода. Дверь приоткрывается на _unlockAjarFraction.")]
        [SerializeField] private AudioClip _unlockClip;
        [Tooltip("Насколько дверь приоткрывается после разблокировки (доля от _maxOpenAngle). 0.1 ≈ 9°.")]
        [SerializeField] [Range(0f, 0.4f)] private float _unlockAjarFraction = 0.12f;

        // ── Runtime state ────────────────────────────────────────────────────────

        private float   _closedLocalEulerY;
        private float   _openFraction;        // 0 = closed, 1 = fully open
        private float   _targetFraction;
        private float   _velocity;            // open-fraction per second
        private bool    _isDragging;
        private bool    _isLockedDrag;
        private bool    _dragActive;
        // Grab point in pivot's local space (XZ only — door rotates around Y)
        private Vector3 _grabOffsetLocal;
        // Для защиты от тривиального клика без реального перетаскивания
        private float   _dragStartFraction;
        private float   _preDragTarget;

        private const float MinScreenMoveSqr = 4f * 4f;   // px² — мин. движение экранной точки захвата
        private const float MinDragFraction  = 0.04f;
        private const float MinDragVelocity  = 0.08f;

        // ── Unity ────────────────────────────────────────────────────────────────

        private void Start() {
            if (_pivot == null)
                _pivot = transform.parent != null ? transform.parent : transform;

            _closedLocalEulerY = _pivot.localEulerAngles.y;
            _openFraction      = _isOpen ? 1f : 0f;
            _targetFraction    = _openFraction;

            if (_isOpen) {
                _dragActive = true;
                ApplyAngle();
            }

            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        private void Update() {
            if (!_dragActive) return;

            // Apply velocity to open-fraction.
            float maxFraction = _isLockedDrag ? _lockedJiggleFraction : 1f;
            if (Mathf.Abs(_velocity) > 0.0001f) {
                _openFraction = Mathf.Clamp(_openFraction + _velocity * Time.deltaTime, 0f, maxFraction);
                _velocity    *= Mathf.Clamp01(1f - _friction * Time.deltaTime);
                ApplyAngle();
                if (!_isDragging) return;
            }

            if (_isDragging) return;

            // Velocity settled — smoothly snap to nearest rest position.
            _openFraction = Mathf.Lerp(_openFraction, _targetFraction, _snapSpeed * Time.deltaTime);
            ApplyAngle();
        }

        // ── IDraggable ───────────────────────────────────────────────────────────

        /// <summary>Called by FPSController when LMB is pressed while looking at the door.</summary>
        public void OnDragStart(Vector3 hitPoint) {
            _isDragging        = true;
            _dragActive        = true;
            _isLockedDrag      = _isLocked && !_isOpen;
            _dragStartFraction = _openFraction;
            _preDragTarget     = _targetFraction;

            if (_isLockedDrag)
                PlayClip(_lockedClip);

            // Сохраняем точку захвата в локальном пространстве шарнира (только XZ).
            // Это позволяет вычислять правильное направление открытия с любого ракурса.
            if (_pivot != null) {
                Vector3 offset = hitPoint - _pivot.position;
                offset.y = 0f;
                _grabOffsetLocal = _pivot.InverseTransformDirection(offset.sqrMagnitude > 0.01f
                    ? offset.normalized
                    : _pivot.TransformDirection(Vector3.right));
            }
        }

        /// <summary>Called every frame while LMB is held.</summary>
        public void OnDrag(Vector2 mouseDelta) {
            Camera cam = Camera.main;
            if (cam == null || _pivot == null) return;

            // Текущий мировой вектор от шарнира к точке захвата (вращается вместе с дверью).
            Vector3 grabWorld = _pivot.TransformDirection(_grabOffsetLocal);

            // Куда переместится точка захвата на экране, если дверь откроется ещё на 5°?
            Quaternion openStep  = Quaternion.AngleAxis(Mathf.Sign(_maxOpenAngle) * 5f, Vector3.up);
            Vector3    grabMore  = openStep * grabWorld;

            Vector2 screenCur  = cam.WorldToScreenPoint(_pivot.position + grabWorld);
            Vector2 screenMore = cam.WorldToScreenPoint(_pivot.position + grabMore);
            Vector2 openDir    = screenMore - screenCur;
            float   openDirMag = openDir.magnitude;

            // Если экранное смещение достаточно велико — проецируем дельту мыши на него.
            // Иначе дверь смотрит прямо в камеру (маловероятно, но защищаемся).
            if (openDirMag >= 0.5f) {
                float input = Vector2.Dot(mouseDelta, openDir / openDirMag);
                _velocity += input * _dragSensitivity;
            }

            _velocity = Mathf.Clamp(_velocity, -_maxVelocity, _maxVelocity);
        }

        /// <summary>Called when LMB is released. Door coasts then snaps.</summary>
        public void OnDragEnd() {
            bool wasLockedDrag = _isLockedDrag;
            _isDragging   = false;
            _isLockedDrag = false;  // всегда сбрасываем — будет пересчитан в следующем OnDragStart

            if (wasLockedDrag) {
                _targetFraction = 0f;
                return;
            }

            // Trivial drag guard: если перемещение и скорость малы, восстанавливаем pre-drag цель.
            // Без этого guard'а любой случайный клик при открытой/приоткрытой двери
            // пересчитывал _targetFraction и мог начать закрывать дверь.
            bool trivial = Mathf.Abs(_openFraction - _dragStartFraction) < MinDragFraction
                        && Mathf.Abs(_velocity)                           < MinDragVelocity;
            if (trivial) {
                _targetFraction = _preDragTarget;
                return;
            }

            // Прогнозируем куда дверь докатится после отпускания с текущей скоростью.
            // При непрерывном затухании: Δx = v₀ / friction (аналитически точно).
            float projected    = Mathf.Clamp01(_openFraction + _velocity / _friction);
            bool  highVelocity = Mathf.Abs(_velocity) >= _snapVelocityThreshold;
            bool  nearEndpoint = projected < 0.05f || projected > 0.95f;

            bool wasOpen = _isOpen;
            if (highVelocity || nearEndpoint) {
                // Бросок с достаточной силой или почти у края — бинарный snap к 0 или 1.
                _targetFraction = projected >= _snapThreshold ? 1f : 0f;
                _isOpen         = _targetFraction > 0.5f;
                if (_isOpen && !wasOpen)      PlayClip(_openClip);
                else if (!_isOpen && wasOpen) PlayClip(_closeClip);
            } else {
                // Медленное отпускание — дверь остаётся там куда докатится.
                // Звук не играем: дверь просто встаёт на промежуточной позиции.
                _targetFraction = projected;
                _isOpen         = projected > 0.5f;
            }
        }

        // ── IInteractable ────────────────────────────────────────────────────────

        /// <summary>E-key fallback: разблокировка если есть ключ.</summary>
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

        public string GetInteractText()   => _isOpen ? _closeText : _openText;
        public bool   IsPickable()        => false;
        public bool   UseLMBClick         => false;

        /// <summary>Иконка прицела: замок если заперта, рука если можно открыть.</summary>
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

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Unlocks the door programmatically without opening it.</summary>
        public void Unlock() => _isLocked = false;

        /// <summary>Unlocks and immediately snaps the door open. Wire to CodeLock.OnUnlocked.</summary>
        public void UnlockAndOpen() {
            _isLocked       = false;
            _isLockedDrag   = false;      // сбрасываем на случай если последний drag был locked
            _dragActive     = true;
            _targetFraction = _unlockAjarFraction;
            PlayClip(_unlockClip);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void ApplyAngle() {
            if (_pivot == null) return;
            Vector3 e = _pivot.localEulerAngles;
            e.y = _closedLocalEulerY + _openFraction * _maxOpenAngle;
            _pivot.localEulerAngles = e;
        }

        /// <summary>Воспроизводит клип если AudioSource и клип назначены.</summary>
        private void PlayClip(AudioClip clip) {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }
    }
}
