using System;
using UnityEngine;

/// <summary>
/// Скрывает или показывает объект при срабатывании GameEvent.
/// Самодостаточный компонент — не требует GameEventListener на родителе.
///
/// ПРИНЦИП:
///   GameObject остаётся активным (чтобы слушать ивент).
///   Вместо SetActive(false) выключаются Renderer-ы и Collider-ы —
///   объект невидим и не взаимодействует, но продолжает работать.
///
/// ВАЖНО: скрытие происходит в Awake с ранним приоритетом выполнения
/// (DefaultExecutionOrder = -100), чтобы рендереры были отключены
/// ДО того, как RoomController.Awake() соберёт список управляемых
/// рендереров. Иначе RoomVisibilityManager переактивирует их в Start.
///
/// НАСТРОЙКА:
///   1. Добавь ObjectActivator на объект, который нужно скрыть/показать.
///   2. Укажи _activateEvent (GameEvent .asset).
///   3. При старте объект скрыт. Когда ивент срабатывает — объект появляется.
///
/// Методы Activate() / Deactivate() также доступны для вызова
/// из других UnityEvent-ов (кнопки, триггеры и т.д.).
/// </summary>
[DefaultExecutionOrder(-100)]
public class ObjectActivator : MonoBehaviour
{
    [Tooltip("Событие, при срабатывании которого объект становится видимым.")]
    [SerializeField] private GameEvent _activateEvent;

    [Tooltip("Скрывать объект при старте, пока ивент не сработал.")]
    [SerializeField] private bool _startHidden = true;

    [Tooltip("Также отключать коллайдеры вместе с рендерерами.")]
    [SerializeField] private bool _toggleColliders = true;

    private Renderer[] _renderers;
    private Collider[] _colliders;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);

        // Скрываем в Awake (до RoomController.Awake), чтобы RoomVisibilityManager
        // не собрал эти рендереры как управляемые и не переактивировал их в Start.
        if (_startHidden)
            Deactivate();
    }

    private void OnEnable()
    {
        _activateEvent?.RegisterAction(OnEventRaised);
    }

    private void OnDisable()
    {
        _activateEvent?.UnregisterAction(OnEventRaised);
    }

    /// <summary>Делает объект видимым (включает рендереры и коллайдеры).</summary>
    public void Activate()
    {
        SetVisible(true);
    }

    /// <summary>Скрывает объект (выключает рендереры и коллайдеры).</summary>
    public void Deactivate()
    {
        SetVisible(false);
    }

    private void OnEventRaised()
    {
        Activate();
    }

    private void SetVisible(bool visible)
    {
        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r != null) r.enabled = visible;
            }
        }

        if (_toggleColliders && _colliders != null)
        {
            foreach (var c in _colliders)
            {
                if (c != null) c.enabled = visible;
            }
        }
    }
}
