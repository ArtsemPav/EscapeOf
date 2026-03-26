using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Escape.Core {
    /// <summary>
    /// Обрабатывает логику взаимодействия, проверку ключей и состояние двери.
    /// Поддерживает физическое перетаскивание (IDraggable): удерживай LMB и двигай мышью.
    /// Заперта дверь — слегка поддаётся при попытке потянуть и возвращается назад.
    /// Implements ISaveable: persists isOpen, isLocked and openFraction across sessions.
    /// </summary>
    public class DoorInteraction : MonoBehaviour, IInteractable, IDraggable, ISaveable {

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
        [Tooltip("Зацикленный звук — играет пока дверь движется в сторону открытия.")]
        [SerializeField] private AudioClip _openLoopClip;
        [Tooltip("Зацикленный звук — играет пока дверь движется в сторону закрытия.")]
        [SerializeField] private AudioClip _closeLoopClip;
        [Tooltip("Одиночный удар — играет когда дверь достигает полностью закрытого положения.")]
        [SerializeField] private AudioClip _latchClip;
        [Tooltip("Звук подёргивания запертой двери.")]
        [SerializeField] private AudioClip _lockedClip;
        [Tooltip("Звук щелчка замка при вводе правильного кода. Дверь приоткрывается на _unlockAjarFraction.")]
        [SerializeField] private AudioClip _unlockClip;
        [Tooltip("Насколько дверь приоткрывается после разблокировки (доля от _maxOpenAngle). 0.1 ≈ 9°.")]
        [SerializeField] [Range(0f, 0.4f)] private float _unlockAjarFraction = 0.12f;
        [Tooltip("Скорость плавного приоткрытия после разблокировки (fraction/sec). Больше — быстрее.")]
        [SerializeField] private float _unlockAjarSpeed = 0.6f;
        [Tooltip("Громкость звуков движения двери.")]
        [SerializeField] [Range(0f, 1f)] private float _motionVolume = 0.7f;

        [Header("Save")]
        [Tooltip("Stable unique ID for the save system. Right-click → Generate Save ID to auto-fill.")]
        [SerializeField] private string _saveId;

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
        private Vector3 _grabOffsetWorld;
        private float   _dragStartFraction;

        // Pending load state: applied in Start() after _closedLocalEulerY is initialized
        private bool    _hasPendingLoad;
        private bool    _pendingIsOpen;
        private bool    _pendingIsLocked;
        private float   _pendingOpenFraction;
        private bool    _pendingWasUnlocked;

        // ── Audio runtime ─────────────────────────────────────────────────────────
        // Dedicated AudioSources for looping motion sounds, spawned on first use.
        private AudioSource _openLoopSource;
        private AudioSource _closeLoopSource;
        // Tracks which loop is currently active to avoid redundant Play/Stop calls.
        private enum LoopState { None, Opening, Closing }
        private LoopState _currentLoop = LoopState.None;

        private const float MinDragFraction     = 0.04f;
        private const float MinDragVelocity     = 0.08f;
        private const float MotionLoopThreshold = 0.02f; // min |velocity| to keep loop alive

        // ── ISaveable ────────────────────────────────────────────────────────────

        public string SaveId => _saveId;

        /// <summary>Serializes door state: open fraction, locked state, open state.</summary>
        public string GetSaveData() => JsonUtility.ToJson(new DoorSaveData
        {
            isOpen       = _isOpen || _openFraction > 0f,
            isLocked     = _isLocked,
            openFraction = _openFraction,
            wasUnlocked  = !_isLocked && (_isOpen || _openFraction > 0f || _isUnlockAnimating),
        });

        /// <summary>Stores pending state. Applied in Start() after pivot is initialized.</summary>
        public void LoadSaveData(string json)
        {
            var data             = JsonUtility.FromJson<DoorSaveData>(json);
            _hasPendingLoad      = true;
            _pendingIsOpen       = data.isOpen;
            _pendingIsLocked     = data.isLocked;
            _pendingOpenFraction = data.openFraction;
            _pendingWasUnlocked  = data.wasUnlocked;
        }

        [Serializable]
        private struct DoorSaveData
        {
            public bool  isOpen;
            public bool  isLocked;
            public float openFraction;
            // True when the door was unlocked via CodeLock but the save fired before openFraction settled.
            // On load this triggers UnlockAndOpen() so the door swings open visually.
            public bool  wasUnlocked;
        }

        // ── Unity ────────────────────────────────────────────────────────────────

        private void Awake()
        {
            SaveManager.Instance?.Register(this);
        }

        private void OnDestroy()
        {
            SaveManager.Instance?.Unregister(this);
        }

        private void Start() {
            if (_pivot == null)
                _pivot = transform.parent != null ? transform.parent : transform;

            _closedLocalEulerY = _pivot.localEulerAngles.y;

            // Apply loaded state if available, otherwise use serialized defaults
            if (_hasPendingLoad)
            {
                _isOpen       = _pendingIsOpen;
                _isLocked     = _pendingIsLocked;
                _openFraction = _pendingOpenFraction;
                _hasPendingLoad = false;

                // Door was unlocked by CodeLock but openFraction may be near 0.
                // Trigger the visual unlock swing so it doesn't just sit closed.
                if (_pendingWasUnlocked && _openFraction < _unlockAjarFraction)
                {
                    _isUnlockAnimating = true;
                    _unlockAjarTarget  = _unlockAjarFraction;
                    _dragActive        = true;
                }
            }
            else
            {
                _openFraction = _isOpen ? 1f : 0f;
            }

            // Do not overwrite _dragActive when an unlock animation is already pending —
            // it was set to true above. Only derive from position for normal loads.
            if (!_isUnlockAnimating)
                _dragActive = _openFraction > 0f || _isOpen;
            if (_dragActive) ApplyAngle();
        }

        private void Update() {
            if (_isDragging) return;

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
                CheckLatch(prev, _openFraction);
                ApplyAngle();

                // Kill velocity and stop loops once it fades below threshold
                if (Mathf.Abs(_velocity) < MotionLoopThreshold)
                {
                    _velocity = 0f;
                    StopMotionLoops();
                }
            } else {
                _velocity   = 0f;
                _dragActive = false;
                StopMotionLoops();
            }
        }

        // ── IDraggable ───────────────────────────────────────────────────────────

        /// <summary>Called by FPSController when LMB is pressed while looking at the door.</summary>
        public void OnDragStart(Vector3 hitPoint) {
            _isDragging        = true;
            _dragActive        = true;
            _snappingBack      = false;
            _isUnlockAnimating = false;
            _isLockedDrag      = _isLocked && !_isOpen;
            _dragStartFraction = _openFraction;

            if (_isLockedDrag)
                AudioManager.Instance.PlaySFX(_lockedClip);

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

            float   openedAngle  = _openFraction * _maxOpenAngle;
            Vector3 grabWorld    = Quaternion.AngleAxis(openedAngle, Vector3.up) * _grabOffsetWorld;
            float   grabDist     = grabWorld.magnitude;

            if (grabDist < 0.001f) return;

            Vector3 swingTangent = Vector3.Cross(Vector3.up, grabWorld / grabDist)
                                   * Mathf.Sign(_maxOpenAngle);

            Vector3 grabWorldPos = _pivot.position + grabWorld;
            Vector2 screenGrab   = cam.WorldToScreenPoint(grabWorldPos);
            Vector2 screenAhead  = cam.WorldToScreenPoint(grabWorldPos + swingTangent * 0.5f);
            Vector2 openDir      = screenAhead - screenGrab;
            float   openDirMag   = openDir.magnitude;

            if (openDirMag < 0.5f) return;

            float screenPerFraction = Mathf.Abs(_maxOpenAngle) * Mathf.Deg2Rad
                                      * (openDirMag / 0.5f);

            float input        = Vector2.Dot(mouseDelta, openDir / openDirMag);
            float deltaFraction = input * _dragSensitivity / Mathf.Max(screenPerFraction, 0.01f);

            float maxFraction = _isLockedDrag ? _lockedJiggleFraction : 1f;
            float prev         = _openFraction;
            _openFraction = Mathf.Clamp(_openFraction + deltaFraction, 0f, maxFraction);
            _velocity     = Mathf.Clamp((_openFraction - prev) / Mathf.Max(Time.deltaTime, 0.0001f), -_maxVelocity, _maxVelocity);

            if (!_isLockedDrag)
            {
                UpdateMotionLoop();
                CheckLatch(prev, _openFraction);
            }

            ApplyAngle();
        }

        /// <summary>Called when LMB is released. Door coasts via inertia to its natural stop.</summary>
        public void OnDragEnd() {
            bool wasLockedDrag = _isLockedDrag;
            _isDragging   = false;
            _isLockedDrag = false;

            StopMotionLoops();

            if (wasLockedDrag) {
                _snappingBack = true;
                return;
            }

            bool trivial = Mathf.Abs(_openFraction - _dragStartFraction) < MinDragFraction
                        && Mathf.Abs(_velocity)                           < MinDragVelocity;
            if (trivial) {
                _velocity   = 0f;
                _dragActive = false;
                return;
            }

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
                    // Snapshot must be taken AFTER both RemoveItem and UnlockAndOpen so the
                    // save captures the correct state: key gone + door unlocked simultaneously.
                    SaveManager.Instance?.Save();
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

        /// <summary>Gets or lazily creates a looping AudioSource for the given clip.</summary>
        private AudioSource GetLoopSource(ref AudioSource source, AudioClip clip)
        {
            if (source != null) return source;
            GameObject obj = new GameObject("DoorLoop");
            obj.transform.SetParent(transform);
            obj.transform.localPosition = Vector3.zero;
            source            = obj.AddComponent<AudioSource>();
            source.clip       = clip;
            source.loop       = true;
            source.spatialBlend = 1f;
            source.minDistance  = 0.5f;
            source.maxDistance  = 6f;
            source.volume     = _motionVolume;
            source.playOnAwake = false;
            return source;
        }

        /// <summary>Starts the correct motion loop based on current velocity, stops the other.</summary>
        private void UpdateMotionLoop()
        {
            if (_velocity > MotionLoopThreshold)
            {
                // Opening
                if (_currentLoop != LoopState.Opening)
                {
                    StopLoop(ref _closeLoopSource);
                    if (_openLoopClip != null)
                    {
                        var src = GetLoopSource(ref _openLoopSource, _openLoopClip);
                        if (!src.isPlaying) src.Play();
                    }
                    _currentLoop = LoopState.Opening;
                }
            }
            else if (_velocity < -MotionLoopThreshold)
            {
                // Closing
                if (_currentLoop != LoopState.Closing)
                {
                    StopLoop(ref _openLoopSource);
                    if (_closeLoopClip != null)
                    {
                        var src = GetLoopSource(ref _closeLoopSource, _closeLoopClip);
                        if (!src.isPlaying) src.Play();
                    }
                    _currentLoop = LoopState.Closing;
                }
            }
            else
            {
                StopMotionLoops();
            }
        }

        /// <summary>Stops both motion loops immediately.</summary>
        private void StopMotionLoops()
        {
            StopLoop(ref _openLoopSource);
            StopLoop(ref _closeLoopSource);
            _currentLoop = LoopState.None;
        }

        private static void StopLoop(ref AudioSource source)
        {
            if (source != null && source.isPlaying)
                source.Stop();
        }

        /// <summary>Fires the latch clip when door crosses fully closed (openFraction → 0).</summary>
        private void CheckLatch(float prev, float current)
        {
            if (prev > 0f && current <= 0f)
            {
                StopMotionLoops();
                AudioManager.Instance.PlaySFX(_latchClip);
            }
        }
        [ContextMenu("Generate Save ID")]
        private void GenerateSaveId()
        {
            if (!string.IsNullOrEmpty(_saveId)) return;
            _saveId = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
