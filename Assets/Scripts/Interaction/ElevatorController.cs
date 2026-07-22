using System;
using System.Collections;
using UnityEngine;

namespace Escape.Interaction
{
    /// <summary>
    /// Stores references to the two sliding door wings on a single floor.
    /// </summary>
    [Serializable]
    public struct ElevatorDoorPair
    {
        [Tooltip("Правая створка (elevatorDoors_wing_A) — уезжает в +Z при открытии.")]
        public Transform wingA;
        [Tooltip("Левая створка (elevatorDoors_wing_B) — уезжает в -Z при открытии.")]
        public Transform wingB;
    }

    /// <summary>
    /// Управляет кабиной лифта: перемещение между этажами по оси Y,
    /// синхронное открытие/закрытие дверей кабины и коридорных дверей.
    ///During movement the player is carried by applying the same Y-delta to the player transform.
    /// Implements ISaveable: persists current floor index and door state.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorController : MonoBehaviour, ISaveable
    {
        private const string DEFAULT_SAVE_ID = "elevator_controller";
        private const int DOOR_CLOSED = 0;
        private const int DOOR_OPEN = 1;

        [Header("Save")]
        [SerializeField] private string _saveId = DEFAULT_SAVE_ID;

        [Header("Cab")]
        [Tooltip("Transform кабины — двигается только по локальной оси Y.")]
        [SerializeField] private Transform _elevatorCab;
        [Tooltip("Двери кабины (elevatorDoors_wing_A / _B внутри Elevator).")]
        [SerializeField] private ElevatorDoorPair _cabinDoors;

        [Header("Floors")]
        [Tooltip("Маркеры этажей. Индекс 0 = подвал, 1 = 1-й этаж, 2 = 2-й этаж. " +
                 "Кабина принимает Y-координату маркера.")]
        [SerializeField] private Transform[] _floorMarkers;
        [Tooltip("Коридорные двери для каждого этажа (в том же порядке, что и маркеры).")]
        [SerializeField] private ElevatorDoorPair[] _floorDoors;
        [Tooltip("Индекс этажа, на котором лифт стартует при новой игре.")]
        [SerializeField] private int _startingFloor = 1;

        [Header("Door Animation")]
        [Tooltip("Расстояние, на которое створки разъезжаются при открытии (по локальной оси Z).")]
        [SerializeField] private float _doorOpenOffset = 0.9f;
        [Tooltip("Время открытия/закрытия дверей (секунды).")]
        [SerializeField] [Min(0.1f)] private float _doorDuration = 1.5f;

        [Header("Movement")]
        [Tooltip("Кривая плавности движения кабины. 0 → начало, 1 → конец.")]
        [SerializeField] private AnimationCurve _moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Скорость движения кабины в единицах/сек. Время поездки = расстояние / скорость.")]
        [SerializeField] [Min(0.1f)] private float _moveSpeed = 3f;
        [Tooltip("Пауза между закрытием дверей и началом движения (секунды).")]
        [SerializeField] [Min(0f)] private float _pauseBeforeMove = 0.3f;
        [Tooltip("Пауза между остановкой и открытием дверей (секунды).")]
        [SerializeField] [Min(0f)] private float _pauseBeforeOpen = 0.3f;

        [Header("Audio")]
        [Tooltip("Звук открытия/закрытия дверей (one-shot).")]
        [SerializeField] private AudioClip _doorSound;
        [Tooltip("Зацикленный звук движения кабины (3D луп).")]
        [SerializeField] private AudioClip _moveSound;
        [Tooltip("Звук прибытия на этаж (one-shot).")]
        [SerializeField] private AudioClip _arriveSound;
        [Tooltip("Громкость лупа движения.")]
        [SerializeField] [Range(0f, 1f)] private float _moveVolume = 0.8f;
        [Tooltip("Минимальная дистанция 3D-звучания лупа.")]
        [SerializeField] private float _moveMinDistance = 1f;
        [Tooltip("Максимальная дистанция 3D-звучания лупа.")]
        [SerializeField] private float _moveMaxDistance = 10f;

        [Header("Ambient Loop")]
        [Tooltip("Зацикленный эмбиент-звук внутри кабины — играет пока игрок в лифте (3D луп).")]
        [SerializeField] private AudioClip _ambientClip;
        [Tooltip("Громкость эмбиент-лупа.")]
        [SerializeField] [Range(0f, 1f)] private float _ambientVolume = 0.5f;
        [Tooltip("Минимальная дистанция 3D-звучания эмбиента.")]
        [SerializeField] private float _ambientMinDistance = 1f;
        [Tooltip("Максимальная дистанция 3D-звучания эмбиента.")]
        [SerializeField] private float _ambientMaxDistance = 5f;

        [Header("Auto-Close")]
        [Tooltip("Через сколько секунд двери закроются автоматически, если игрок не в лифте.")]
        [SerializeField] [Min(1f)] private float _autoCloseDelay = 20f;

        public int CurrentFloor { get; private set; }
        public bool IsMoving { get; private set; }
        public bool AreDoorsOpen { get; private set; }

        private bool _isBusy;
        private Transform _playerTransform;
        private CharacterController _playerCharacterController;
        private FPSController _playerFPSController;
        private AudioSource _moveLoopSource;
        private AudioSource _ambientLoopSource;
        private Coroutine _autoCloseCoroutine;
        private Vector3 _cabinDoorAStart;
        private Vector3 _cabinDoorBStart;
        private Vector3[][] _floorDoorStarts;

        public string SaveId => _saveId;

        public string GetSaveData()
        {
            return JsonUtility.ToJson(new ElevatorSaveData
            {
                currentFloor = CurrentFloor,
                doorsOpen = AreDoorsOpen
            });
        }

        public void LoadSaveData(string json)
        {
            var data = JsonUtility.FromJson<ElevatorSaveData>(json);
            CurrentFloor = Mathf.Clamp(data.currentFloor, 0, _floorMarkers.Length - 1);
            AreDoorsOpen = data.doorsOpen;

            SnapToFloor(CurrentFloor);
            SetDoorsImmediate(CurrentFloor, AreDoorsOpen);
        }

        [Serializable]
        private struct ElevatorSaveData
        {
            public int currentFloor;
            public bool doorsOpen;
        }

        private void Awake()
        {
            SaveManager.Instance?.Register(this);

            CacheDoorStarts();
        }

        private void OnDestroy()
        {
            SaveManager.Instance?.Unregister(this);
            StopAmbientLoop();
            CancelAutoClose();
        }

        private void Start()
        {
            // If no save was loaded, initialize at starting floor
            if (_floorMarkers == null || _floorMarkers.Length == 0) return;

            if (CurrentFloor == 0 && !AreDoorsOpen && _startingFloor != 0)
            {
                CurrentFloor = Mathf.Clamp(_startingFloor, 0, _floorMarkers.Length - 1);
                SnapToFloor(CurrentFloor);
                SetDoorsImmediate(CurrentFloor, true);
                AreDoorsOpen = true;
            }
        }

        /// <summary>
        /// Requests the elevator to move to the given floor index.
        /// If already there with doors open, does nothing.
        /// If busy, the request is ignored (no queue to keep logic simple).
        /// </summary>
        public void MoveToFloor(int floorIndex)
        {
            if (_isBusy) return;
            if (floorIndex < 0 || floorIndex >= _floorMarkers.Length) return;
            if (floorIndex == CurrentFloor && AreDoorsOpen) return;

            StartCoroutine(MoveSequence(floorIndex));
        }

        private IEnumerator MoveSequence(int targetFloor)
        {
            _isBusy = true;
            IsMoving = false;

            // 0. Suppress gravity for the entire sequence (doors + move + doors)
            //    so the player doesn't accumulate downward velocity while waiting.
            if (_playerFPSController != null)
                _playerFPSController.SetElevatorMode(true);

            // 1. Close doors at current floor
            if (AreDoorsOpen)
            {
                yield return AnimateDoors(CurrentFloor, DOOR_OPEN, DOOR_CLOSED);
                AreDoorsOpen = false;
            }

            // 2. Pause before moving
            if (_pauseBeforeMove > 0f)
                yield return new WaitForSeconds(_pauseBeforeMove);

            // 3. Move cabin
            if (targetFloor != CurrentFloor)
            {
                IsMoving = true;
                yield return MoveCabin(CurrentFloor, targetFloor);
                IsMoving = false;
                CurrentFloor = targetFloor;

                if (_arriveSound != null)
                    AudioManager.Instance?.PlaySFX(_arriveSound);
            }

            // 4. Pause before opening
            if (_pauseBeforeOpen > 0f)
                yield return new WaitForSeconds(_pauseBeforeOpen);

            // 5. Open doors at destination
            yield return AnimateDoors(CurrentFloor, DOOR_CLOSED, DOOR_OPEN);
            AreDoorsOpen = true;

            // 6. Resume gravity
            if (_playerFPSController != null)
                _playerFPSController.SetElevatorMode(false);

            _isBusy = false;

            // 7. If player already left, start auto-close timer
            if (_playerTransform == null)
                StartAutoCloseTimer();
        }

        private IEnumerator MoveCabin(int fromFloor, int toFloor)
        {
            float fromY = _floorMarkers[fromFloor].localPosition.y;
            float toY = _floorMarkers[toFloor].localPosition.y;
            float distance = Mathf.Abs(toY - fromY);
            float duration = distance / _moveSpeed;

            Vector3 cabPos = _elevatorCab.localPosition;
            float elapsed = 0f;

            if (_moveSound != null)
            {
                _moveLoopSource = AudioManager.Instance?.Play3DLoop(
                    _moveSound, _elevatorCab, _moveVolume, _moveMinDistance, _moveMaxDistance);
            }

            // Parent player to the cab so it moves automatically with the elevator.
            // HandleGravity/HandleMovement/HandleCrouchTransition are already
            // suppressed by SetElevatorMode(true) in MoveSequence.
            bool wasParented = false;
            if (_playerTransform != null && _playerTransform.parent != _elevatorCab)
            {
                _playerTransform.SetParent(_elevatorCab, worldPositionStays: true);
                wasParented = true;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = _moveCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
                cabPos.y = Mathf.LerpUnclamped(fromY, toY, t);
                _elevatorCab.localPosition = cabPos;
                yield return null;
            }

            // Snap exactly
            cabPos.y = toY;
            _elevatorCab.localPosition = cabPos;

            // Stop movement loop
            if (_moveLoopSource != null)
            {
                AudioManager.Instance?.UnregisterLoopSource(_moveLoopSource);
                _moveLoopSource = null;
            }

            // Unparent player back to root, preserving world position
            if (wasParented && _playerTransform != null)
            {
                _playerTransform.SetParent(null, worldPositionStays: true);
            }
        }

        private IEnumerator AnimateDoors(int floor, int fromState, int toState)
        {
            if (_doorSound != null)
                AudioManager.Instance?.PlaySFX(_doorSound);

            float elapsed = 0f;

            // Cabin doors
            Vector3 cabAFrom = GetDoorPos(_cabinDoorAStart, fromState, true);
            Vector3 cabATo = GetDoorPos(_cabinDoorAStart, toState, true);
            Vector3 cabBFrom = GetDoorPos(_cabinDoorBStart, fromState, false);
            Vector3 cabBTo = GetDoorPos(_cabinDoorBStart, toState, false);

            // Floor doors
            var fd = _floorDoors[floor];
            Vector3 flAFrom = GetDoorPos(_floorDoorStarts[floor][0], fromState, true);
            Vector3 flATo = GetDoorPos(_floorDoorStarts[floor][0], toState, true);
            Vector3 flBFrom = GetDoorPos(_floorDoorStarts[floor][1], fromState, false);
            Vector3 flBTo = GetDoorPos(_floorDoorStarts[floor][1], toState, false);

            while (elapsed < _doorDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _doorDuration);
                float eased = _moveCurve.Evaluate(t);

                if (_cabinDoors.wingA != null)
                    _cabinDoors.wingA.localPosition = Vector3.Lerp(cabAFrom, cabATo, eased);
                if (_cabinDoors.wingB != null)
                    _cabinDoors.wingB.localPosition = Vector3.Lerp(cabBFrom, cabBTo, eased);

                if (fd.wingA != null)
                    fd.wingA.localPosition = Vector3.Lerp(flAFrom, flATo, eased);
                if (fd.wingB != null)
                    fd.wingB.localPosition = Vector3.Lerp(flBFrom, flBTo, eased);

                yield return null;
            }

            // Snap exactly
            if (_cabinDoors.wingA != null) _cabinDoors.wingA.localPosition = cabATo;
            if (_cabinDoors.wingB != null) _cabinDoors.wingB.localPosition = cabBTo;
            if (fd.wingA != null) fd.wingA.localPosition = flATo;
            if (fd.wingB != null) fd.wingB.localPosition = flBTo;
        }

        private Vector3 GetDoorPos(Vector3 start, int state, bool isWingA)
        {
            Vector3 pos = start;
            if (state == DOOR_OPEN)
                pos.z += isWingA ? _doorOpenOffset : -_doorOpenOffset;
            return pos;
        }

        private void CacheDoorStarts()
        {
            if (_cabinDoors.wingA != null) _cabinDoorAStart = _cabinDoors.wingA.localPosition;
            if (_cabinDoors.wingB != null) _cabinDoorBStart = _cabinDoors.wingB.localPosition;

            int count = _floorDoors != null ? _floorDoors.Length : 0;
            _floorDoorStarts = new Vector3[count][];
            for (int i = 0; i < count; i++)
            {
                _floorDoorStarts[i] = new Vector3[2];
                if (_floorDoors[i].wingA != null)
                    _floorDoorStarts[i][0] = _floorDoors[i].wingA.localPosition;
                if (_floorDoors[i].wingB != null)
                    _floorDoorStarts[i][1] = _floorDoors[i].wingB.localPosition;
            }
        }

        private void SnapToFloor(int floor)
        {
            if (_elevatorCab == null || _floorMarkers == null) return;
            if (floor < 0 || floor >= _floorMarkers.Length) return;

            var pos = _elevatorCab.localPosition;
            pos.y = _floorMarkers[floor].localPosition.y;
            _elevatorCab.localPosition = pos;
        }

        private void SetDoorsImmediate(int floor, bool open)
        {
            int state = open ? DOOR_OPEN : DOOR_CLOSED;

            if (_cabinDoors.wingA != null)
                _cabinDoors.wingA.localPosition = GetDoorPos(_cabinDoorAStart, state, true);
            if (_cabinDoors.wingB != null)
                _cabinDoors.wingB.localPosition = GetDoorPos(_cabinDoorBStart, state, false);

            if (_floorDoors != null && floor >= 0 && floor < _floorDoors.Length)
            {
                if (_floorDoors[floor].wingA != null)
                    _floorDoors[floor].wingA.localPosition =
                        GetDoorPos(_floorDoorStarts[floor][0], state, true);
                if (_floorDoors[floor].wingB != null)
                    _floorDoors[floor].wingB.localPosition =
                        GetDoorPos(_floorDoorStarts[floor][1], state, false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var fps = other.GetComponentInParent<FPSController>();
            if (fps == null) return;

            _playerTransform = fps.transform;
            _playerCharacterController = fps.GetComponent<CharacterController>();
            _playerFPSController = fps;

            // Player entered — cancel auto-close, start ambient loop
            CancelAutoClose();
            StartAmbientLoop();
        }

        private void OnTriggerExit(Collider other)
        {
            var fps = other.GetComponentInParent<FPSController>();
            if (fps == null) return;

            _playerTransform = null;
            _playerCharacterController = null;
            _playerFPSController = null;

            // Player left — stop ambient loop, start auto-close timer
            StopAmbientLoop();
            StartAutoCloseTimer();
        }

        private void StartAmbientLoop()
        {
            if (_ambientClip == null) return;
            StopAmbientLoop();
            _ambientLoopSource = AudioManager.Instance?.Play3DLoop(
                _ambientClip, _elevatorCab, _ambientVolume,
                _ambientMinDistance, _ambientMaxDistance);
        }

        private void StopAmbientLoop()
        {
            if (_ambientLoopSource != null)
            {
                AudioManager.Instance?.UnregisterLoopSource(_ambientLoopSource);
                _ambientLoopSource = null;
            }
        }

        private void StartAutoCloseTimer()
        {
            if (!AreDoorsOpen || _isBusy) return;
            CancelAutoClose();
            _autoCloseCoroutine = StartCoroutine(AutoCloseAfterDelay());
        }

        private void CancelAutoClose()
        {
            if (_autoCloseCoroutine != null)
            {
                StopCoroutine(_autoCloseCoroutine);
                _autoCloseCoroutine = null;
            }
        }

        private IEnumerator AutoCloseAfterDelay()
        {
            yield return new WaitForSeconds(_autoCloseDelay);

            _autoCloseCoroutine = null;

            if (!AreDoorsOpen || _isBusy || _playerTransform != null) yield break;

            yield return AnimateDoors(CurrentFloor, DOOR_OPEN, DOOR_CLOSED);
            AreDoorsOpen = false;
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}
