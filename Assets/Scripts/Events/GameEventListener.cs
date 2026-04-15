using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// СЛУШАТЕЛЬ ИГРОВОГО ИВЕНТА
/// ═══════════════════════════════════════════════════════════════════════
///
/// ЧТО ЭТО:
///   Компонент, который реагирует на GameEvent.
///   Добавь его на любой GameObject — дверь, свет, звук, что угодно.
///
/// КАК НАСТРОИТЬ:
///   1. Добавь этот компонент на нужный GameObject.
///   2. В поле «Событие» перетащи .asset файл ивента
///      (например Assets/Data/Events/PowerRestore.asset).
///   3. В поле «Реакция» нажми «+» и подключи нужный метод:
///      • LightingSystem → SetPower (bool: true)
///      • DoorController → Open()
///      • AudioManager   → PlaySFX(clip)
///      • и т.д.
///
/// ОДИН ОБЪЕКТ — НЕСКОЛЬКО ИВЕНТОВ:
///   Можно добавить несколько компонентов GameEventListener на один объект,
///   каждый слушает свой ивент.
///
/// ПРИМЕР:
///   GameObject: «LightingSystem»
///     └── GameEventListener
///           Событие  = PowerOff.asset
///           Реакция  → LightingSystem.SetPower(false)
///     └── GameEventListener
///           Событие  = PowerRestore.asset
///           Реакция  → LightingSystem.SetPower(true)
///
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class GameEventListener : MonoBehaviour
{
    [Tooltip("Ивент, на который подписывается этот компонент.\n" +
             "Перетащи сюда .asset файл из Assets/Data/Events/")]
    [SerializeField] private GameEvent _event;

    [Tooltip("Что произойдёт когда ивент будет поднят.\n" +
             "Нажми «+» и подключи нужный метод через Inspector.")]
    [SerializeField] private UnityEvent _response;

    private void OnEnable()
    {
        if (_event == null)
        {
            Debug.LogWarning($"[GameEventListener] на '{gameObject.name}': поле «Событие» не заполнено.", this);
            return;
        }
        _event.RegisterListener(this);
    }

    private void OnDisable()
    {
        _event?.UnregisterListener(this);
    }

    /// <summary>Вызывается автоматически из GameEvent.Raise(). Не вызывай напрямую.</summary>
    public void OnEventRaised() => _response?.Invoke();
}
