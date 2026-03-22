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
        [Tooltip("Speed at which a locked door snaps back to closed after a jiggle attempt.")]
        [SerializeField] private float _lockedSnapBackSpeed = 8f;

        [Header("Audio")]
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
        [Tooltip("Скорость плавного приоткрытия после разблокировки (fraction/sec). Больше — быстрее.")]
        [SerializeField] private float _unlockAjarSpeed = 0.6f;

        // ── Runtime state ────────────────────────────────────────────────────────

        private float   _closedLocalEulerY;
        private float   _openFraction;        // 0 = closed, 1 = fully open
        private float   _velocity;            // open-fraction per second
        private bool    _isDragging;
        private bool    _isLockedDrag;
        private bool    _dragActive;
        private bool    _snappingBack;        // true after locked jiggle — lerp back to 0
        private bool    _isUnlockAnimating;   // true while smoothly swinging ajar after unlock
        private float   _unlockAjarTarget;    // target fraction for the unlock swing
        // Grab point offset from pivot in WORLD space XZ at drag start (not normalized — stores real distance).
        // Stored in world coords to avoid InverseTransformDirection issues with negative-scale FBX meshes.
        private Vector3 _grabOffsetWorld;
        private float   _dragStartFraction;   // для trivial-drag guard

        private const float MinDragFraction = 0.04f;
        private const float MinDragVelocity  = 0.08f;

        // ── Unity ────────────────────────────────────────────────────────────────

        private void Start() {
            if (_pivot == null)
                _pivot = transform.parent != null ? transform.parent : transform;

            _closedLocalEulerY = _pivot.localEulerAngles.y;
            _openFraction      = _isOpen ? 1f : 0f;

            if (_isOpen) {
                _dragActive = true;
                ApplyAngle();
            }
        }

        private void Update() {
            if (!_dragActive) return;
            if (_isDragging) return; // OnDrag напрямую двигает дверь — Update только после отпускания

            // ── Smooth unlock ajar ───────────────────────────────────────────────
            if (_isUnlockAnimating) {
                _openFraction = Mathf.MoveTowards(_openFraction, _unlockAjarTarget, _unlockAjarSpeed * Time.deltaTime);
                ApplyAngle();
                if (Mathf.Approximately(_openFraction, _unlockAjarTarget)) {
                    _isUnlockAnimating = false;
                    _dragActive        = false;
                }
                return;
            }

            // ── Post-release inertia ─────────────────────────────────────────────
            if (_snappingBack) {
                _openFraction = Mathf.Lerp(_openFraction, 0f, _lockedSnapBackSpeed * Time.deltaTime);
                ApplyAngle();
                if (_openFraction < 0.001f) {
                    _openFraction = 0f;
                    _snappingBack = false;
                    _dragActive   = false;
                    ApplyAngle();
                }
                return;
            }

            if (Mathf.Abs(_velocity) > 0.0001f) {
                float prev    = _openFraction;
                _openFraction = Mathf.Clamp(_openFraction + _velocity * Time.deltaTime, 0f, 1f);
                _velocity    *= Mathf.Clamp01(1f - _friction * Time.deltaTime);
                PlayBoundaryClips(prev, _openFraction);
                ApplyAngle();
            } else {
                _velocity   = 0f;
                _dragActive = false;
            }
        }

        // ── IDraggable ───────────────────────────────────────────────────────────

        /// <summary>Called by FPSController when LMB is pressed while looking at the door.</summary>
        public void OnDragStart(Vector3 hitPoint) {
            _isDragging        = true;
            _dragActive        = true;
            _snappingBack      = false;
            _isLockedDrag      = _isLocked && !_isOpen;
            _dragStartFraction = _openFraction;

            if (_isLockedDrag)
                AudioManager.Instance.PlaySFX(_lockedClip);
            else
                AudioManager.Instance.PlaySFX(_openClip);

            // Сохраняем offset в «закрытом» системе координат (pivot = 0°).
            // В OnDrag мы вращаем его на openFraction * maxAngle, получая правильное
            // мировое положение точки захвата при любом угле двери.
            // Без обратного поворота здесь offset был бы уже в открытом положении,
            // и дополнительный поворот в OnDrag давал бы двойную ротацию → инверсию.
            if (_pivot != null) {
                Vector3 offset = hitPoint - _pivot.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < 0.01f)
                    offset = Vector3.right;
                float currentOpenAngle = _dragStartFraction * _maxOpenAngle;
                _grabOffsetWorld = Quaternion.AngleAxis(-currentOpenAngle, Vector3.up) * offset;
            }
        }

        /// <summary>Called every frame while LMB is held.</summary>
        public void OnDrag(Vector2 mouseDelta) {
            Camera cam = Camera.main;
            if (cam == null || _pivot == null) return;

            // Текущее положение точки захвата в мире: вращаем начальный offset
            // вместе с открытием двери (openFraction * maxAngle — точный угол поворота пивота).
            float   openedAngle  = _openFraction * _maxOpenAngle;
            Vector3 grabWorld    = Quaternion.AngleAxis(openedAngle, Vector3.up) * _grabOffsetWorld;
            float   grabDist     = grabWorld.magnitude;

            if (grabDist < 0.001f) return;

            // Касательная к дуге вращения вокруг мировой оси Y.
            // cross(up, grabDir) даёт направление движения точки при ПОЛОЖИТЕЛЬНОМ вращении;
            // умножение на sign(maxOpenAngle) корректирует знак для дверей с отрицательным углом.
            Vector3 swingTangent = Vector3.Cross(Vector3.up, grabWorld / grabDist)
                                   * Mathf.Sign(_maxOpenAngle);

            // Проецируем касательную на экран от реальной позиции точки захвата.
            Vector3 grabWorldPos = _pivot.position + grabWorld;
            Vector2 screenGrab   = cam.WorldToScreenPoint(grabWorldPos);
            Vector2 screenAhead  = cam.WorldToScreenPoint(grabWorldPos + swingTangent * 0.5f);
            Vector2 openDir      = screenAhead - screenGrab;
            float   openDirMag   = openDir.magnitude;

            if (openDirMag < 0.5f) return;

            // Sensitivity: pixels mouse → fraction of door rotation.
            // grabDist intentionally excluded — same swipe = same rotation angle
            // regardless of where the player grabbed (game feel over physics).
            // openDirMag / 0.5f accounts for perspective (camera distance).
            float screenPerFraction = Mathf.Abs(_maxOpenAngle) * Mathf.Deg2Rad
                                      * (openDirMag / 0.5f);

            float input        = Vector2.Dot(mouseDelta, openDir / openDirMag);
            float deltaFraction = input * _dragSensitivity / Mathf.Max(screenPerFraction, 0.01f);

            // Phasmophobia style: напрямую двигаем дверь, без накопления скорости.
            // Скорость отслеживаем для инерции после отпускания (fraction/sec).
            float maxFraction = _isLockedDrag ? _lockedJiggleFraction : 1f;
            float prev         = _openFraction;
            _openFraction = Mathf.Clamp(_openFraction + deltaFraction, 0f, maxFraction);
            _velocity     = Mathf.Clamp((_openFraction - prev) / Mathf.Max(Time.deltaTime, 0.0001f), -_maxVelocity, _maxVelocity);
            if (!_isLockedDrag) PlayBoundaryClips(prev, _openFraction);
            ApplyAngle();
        }

        /// <summary>Called when LMB is released. Door coasts via inertia to its natural stop.</summary>
        public void OnDragEnd() {
            bool wasLockedDrag = _isLockedDrag;
            _isDragging   = false;
            _isLockedDrag = false;

            if (wasLockedDrag) {
                // Locked jiggle: return to closed via Update() lerp.
                _snappingBack = true;
                return;
            }

            // Trivial drag guard: случайный клик без движения — сбрасываем скорость и останавливаемся.
            bool trivial = Mathf.Abs(_openFraction - _dragStartFraction) < MinDragFraction
                        && Mathf.Abs(_velocity)                           < MinDragVelocity;
            if (trivial) {
                _velocity   = 0f;
                _dragActive = false;
                return;
            }

            // Door coasts to wherever velocity naturally takes it — no snap.
            _isOpen = _openFraction > 0.5f;
        }

        // ── IInteractable ────────────────────────────────────────────────────────

        /// <summary>E-key: использует ключ из инвентаря чтобы разблокировать и плавно приоткрыть дверь.</summary>
        public void Interact() {
            if (_isLocked && !_isOpen) {
                if (_requiredKey != null && InventorySystem.Instance.HasItem(_requiredKey)) {
                    if (_requiredKey.consumeOnUse)
                        InventorySystem.Instance.RemoveItem(_requiredKey);
                    UnlockAndOpen();
                } else {
                    PopupMessageSystem.Instance.Show("Нужен ключ от этой двери", PopupMessageType.Warning, 4f);
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

        /// <summary>Unlocks and smoothly swings the door ajar. Wire to CodeLock.OnUnlocked.</summary>
        public void UnlockAndOpen() {
            _isLocked          = false;
            _isLockedDrag      = false;
            _snappingBack      = false;
            _isUnlockAnimating = true;
            _unlockAjarTarget  = _unlockAjarFraction;
            _dragActive        = true;
            AudioManager.Instance.PlaySFX(_unlockClip);
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
            AudioManager.Instance.PlaySFX(clip);
        }

        /// <summary>Воспроизводит close клип когда дверь достигает закрытого положения.</summary>
        private void PlayBoundaryClips(float prev, float current) {
            if (prev > 0f && current <= 0f) AudioManager.Instance.PlaySFX(_closeClip);
        }
    }
}
