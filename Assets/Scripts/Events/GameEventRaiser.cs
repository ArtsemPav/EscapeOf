using UnityEngine;

/// <summary>
/// ПОДНИМАЛЬЩИК ИГРОВОГО ИВЕНТА (без кода)
/// ═══════════════════════════════════════════════════════════════════════
///
/// ЧТО ЭТО:
///   Позволяет поднять GameEvent из UnityEvent другого компонента,
///   не написав ни строчки кода.
///
/// КОГДА ИСПОЛЬЗОВАТЬ:
///   Когда источник ивента — это кнопка, триггер-коллайдер, анимация
///   или любой компонент с UnityEvent, но ты не хочешь трогать его код.
///
/// КАК НАСТРОИТЬ:
///   1. Добавь этот компонент на объект-источник (кнопка, триггер и т.д.).
///   2. В поле «Ивент» перетащи нужный .asset.
///   3. Из чужого UnityEvent вызови: GameEventRaiser → RaiseEvent()
///
/// ПРИМЕР — кнопка UI поднимает ивент:
///   Button.OnClick → GameEventRaiser.RaiseEvent()
///
/// ПРИМЕР — физический триггер поднимает ивент:
///   Добавь отдельный скрипт-триггер, который вызывает RaiseEvent()
///   из OnTriggerEnter, либо используй готовый компонент ниже.
///
/// КАК ПОДНЯТЬ ИЗ КОДА (рекомендуется):
///   Вместо этого компонента — держи ссылку на GameEvent в своём скрипте:
///   [SerializeField] private GameEvent _onSolved;
///   ...
///   _onSolved?.Raise();
///
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class GameEventRaiser : MonoBehaviour
{
    [Tooltip("Ивент, который будет поднят при вызове RaiseEvent().\n" +
             "Перетащи .asset файл из Assets/Data/Events/")]
    [SerializeField] private GameEvent _event;

    /// <summary>
    /// Поднимает ивент. Подключи этот метод из UnityEvent другого компонента.
    /// </summary>
    public void RaiseEvent()
    {
        if (_event == null)
        {
            Debug.LogWarning($"[GameEventRaiser] на '{gameObject.name}': поле «Ивент» не заполнено.", this);
            return;
        }
        _event.Raise();
    }
}
