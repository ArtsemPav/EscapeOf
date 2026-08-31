using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ИГРОВОЙ ИВЕНТ (ScriptableObject-канал)
/// ═══════════════════════════════════════════════════════════════════════
///
/// ЧТО ЭТО:
///   Один файл .asset = одно событие в игре.
///   Например: «электричество выключено», «дверь лаборатории открылась».
///
/// КАК СОЗДАТЬ ИВЕНТ:
///   ПКМ в окне Project → Create → Game Events → Game Event
///   Назови файл понятно: PowerOff, DoorLab_Open, PuzzleElectric_Solved и т.д.
///   Храни в папке: Assets/Data/Events/
///
/// КАК ПОДНЯТЬ (ВЫЗВАТЬ) ИВЕНТ ИЗ КОДА:
///   [SerializeField] private GameEvent _onPuzzleSolved;
///   ...
///   _onPuzzleSolved.Raise();
///
/// КАК ПОДНЯТЬ БЕЗ КОДА:
///   Добавь компонент GameEventRaiser на нужный объект.
///
/// КАК ПОДПИСАТЬСЯ НА ИВЕНТ:
///   Добавь компонент GameEventListener на объект-получатель.
///   Укажи этот .asset в поле «Событие».
///   Подключи нужный метод в поле «Реакция».
///
/// ПРИМЕР ЦЕПОЧКИ:
///   Пазл решён → PuzzleSolved.Raise()
///     └── GameEventListener на LightingSystem → SetPower(true)
///     └── GameEventListener на Door           → Open()
///     └── GameEventListener на AudioManager   → PlaySFX(powerOnClip)
///
/// ПОДПИСКА ИЗ КОДА (без GameEventListener):
///   _event.RegisterAction(MyHandler);
///   _event.UnregisterAction(MyHandler);
///   Удобно для самодостаточных компонентов вроде ObjectActivator.
///
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
[CreateAssetMenu(menuName = "Game Events/Game Event", fileName = "NewGameEvent")]
public class GameEvent : ScriptableObject
{
    private readonly List<GameEventListener> _listeners = new();
    private readonly List<Action> _actionListeners = new();

    /// <summary>
    /// Поднимает ивент — уведомляет всех активных слушателей.
    /// Вызывай из кода: myEvent.Raise();
    /// </summary>
    public void Raise()
    {
        // Итерируем с конца на случай если слушатель отписывается во время реакции.
        for (int i = _listeners.Count - 1; i >= 0; i--)
            _listeners[i].OnEventRaised();

        for (int i = _actionListeners.Count - 1; i >= 0; i--)
            _actionListeners[i]?.Invoke();
    }

    /// <summary>Регистрирует слушателя. Вызывается автоматически из GameEventListener.OnEnable.</summary>
    public void RegisterListener(GameEventListener listener)
    {
        if (!_listeners.Contains(listener))
            _listeners.Add(listener);
    }

    /// <summary>Снимает слушателя. Вызывается автоматически из GameEventListener.OnDisable.</summary>
    public void UnregisterListener(GameEventListener listener) => _listeners.Remove(listener);

    /// <summary>Регистрирует Action-колбек. Для подписки из кода без GameEventListener.</summary>
    public void RegisterAction(Action action)
    {
        if (action != null && !_actionListeners.Contains(action))
            _actionListeners.Add(action);
    }

    /// <summary>Снимает Action-колбек.</summary>
    public void UnregisterAction(Action action) => _actionListeners.Remove(action);
}
