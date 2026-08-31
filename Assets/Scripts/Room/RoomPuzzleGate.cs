using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Скрывает комнату до тех пор, пока не решена привязанная загадка.
/// Пока комната закрыта (gated):
///   • геометрия комнаты выключена через RoomController.SetGeometryActive(false);
///   • коллайдеры дочерних RoomTrigger отключены — RoomVisibilityManager не покажет комнату;
///   • интерактивные объекты заблокированы через RoomController.Lock();
///   • активна заглушка (placeholder), перекрывающая проход.
/// При разблокировке (через GameEvent или прямой вызов Unlock):
///   • заглушка скрывается;
///   • триггеры и интерактивные объекты включаются;
///   • видимостью геометрии снова управляет RoomVisibilityManager.
/// Состояние сохраняется через ISaveable.
/// </summary>
[RequireComponent(typeof(RoomController))]
public class RoomPuzzleGate : MonoBehaviour, ISaveable
{
    [Header("Gate")]
    [Tooltip("Событие, которое разблокирует комнату (например PuzzleChemical_Solved).")]
    [SerializeField] private GameEvent _unlockEvent;

    [Tooltip("Заглушка, перекрывающая проход, пока комната скрыта. " +
             "Должна быть ОТДЕЛЬНЫМ объектом — не дочерним к комнате, " +
             "иначе RoomController будет управлять её рендерером.")]
    [SerializeField] private GameObject _placeholder;

    [Tooltip("Комната скрыта при старте, пока загадка не решена.")]
    [SerializeField] private bool _startLocked = true;

    [Header("Save")]
    [Tooltip("Уникальный идентификатор сохранения. Never change after assigning.")]
    [SerializeField] private string _saveId = "room_gate_coridor_fdoor";

    private RoomController _roomController;
    private Collider[] _gateTriggerColliders;
    private bool _isUnlocked;

    public string SaveId => _saveId;

    /// <summary>True если комната разблокирована (загадка решена).</summary>
    public bool IsUnlocked => _isUnlocked;

    private void Awake()
    {
        _roomController = GetComponent<RoomController>();

        // Кэшируем коллайдеры дочерних RoomTrigger, чтобы отключать их пока комната закрыта.
        var triggers = GetComponentsInChildren<RoomTrigger>(includeInactive: true);
        var colliderList = new List<Collider>();
        foreach (var trigger in triggers)
        {
            if (trigger.TryGetComponent(out Collider col))
                colliderList.Add(col);
        }
        _gateTriggerColliders = colliderList.ToArray();

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // SaveManager.LoadSaveData уже вызван до Start.
        if (_isUnlocked)
        {
            ApplyUnlockedState();
        }
        else if (_startLocked)
        {
            ApplyLockedState();
        }
    }

    /// <summary>
    /// Разблокирует комнату: скрывает заглушку, включает триггеры и интерактивные объекты.
    /// Вызывается из GameEventListener или напрямую из кода.
    /// </summary>
    public void Unlock()
    {
        if (_isUnlocked) return;

        _isUnlocked = true;
        ApplyUnlockedState();
        SaveManager.Instance?.Save();
    }

    private void ApplyLockedState()
    {
        // Скрываем геометрию комнаты.
        if (_roomController != null)
        {
            _roomController.SetGeometryActive(false);
            _roomController.Lock();
        }

        // Отключаем триггеры, чтобы RoomVisibilityManager не показал комнату.
        SetTriggersEnabled(false);

        // Показываем заглушку.
        if (_placeholder != null)
            _placeholder.SetActive(true);
    }

    private void ApplyUnlockedState()
    {
        // Скрываем заглушку.
        if (_placeholder != null)
            _placeholder.SetActive(false);

        // Включаем триггеры — RoomVisibilityManager снова управляет видимостью.
        SetTriggersEnabled(true);

        // Включаем интерактивные объекты.
        if (_roomController != null)
        {
            _roomController.Unlock();
        }
    }

    private void SetTriggersEnabled(bool enabled)
    {
        if (_gateTriggerColliders == null) return;

        foreach (var col in _gateTriggerColliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isUnlocked = _isUnlocked });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isUnlocked = data.isUnlocked;
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    [Serializable]
    private struct SaveData
    {
        public bool isUnlocked;
    }
}
