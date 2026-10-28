## Обзор

Событийная шина на ScriptableObject: один `.asset`-файл — одно событие игры (например «электричество выключено», «загадка решена»). Издатели и слушатели не знают друг о друге — они ссылаются только на общий ассет события. Это развязывает системы: пазл поднимает `PuzzleSolved`, а свет, двери и звук реагируют каждый своим `GameEventListener`-ом.

Скрипты: `Assets/Scripts/Events/`.

---

## Компоненты

### `GameEvent`

Файл: `Assets/Scripts/Events/GameEvent.cs`. ScriptableObject-канал события. Хранит два списка слушателей: компоненты `GameEventListener` и чистые `Action`-колбеки (подписка из кода).

**Создание ассета:** ПКМ в Project → `Create → Game Events → Game Event`. Называйте файл понятно: `PowerOff`, `DoorLab_Open`, `PuzzleElectric_Solved` и т.д. Рекомендуемая папка: `Assets/Data/Events/`.

**Публичные методы**

| Метод | Описание |
| ----- | -------- |
| `Raise()` | Поднимает событие: уведомляет всех слушателей. Итерация с конца — безопасно, если слушатель отписывается во время реакции |
| `RegisterListener(listener)` / `UnregisterListener(listener)` | Подписка `GameEventListener` — вызывается автоматически из его `OnEnable`/`OnDisable` |
| `RegisterAction(Action)` / `UnregisterAction(Action)` | Подписка из кода без компонента (дубликаты не добавляются) |

**Вызов из кода:**

```csharp
[SerializeField] private GameEvent _onPuzzleSolved;
// ...
_onPuzzleSolved?.Raise();
```

### `GameEventListener`

Файл: `Assets/Scripts/Events/GameEventListener.cs`. `MonoBehaviour`-слушатель. Добавляется на любой объект-реагент (дверь, свет, звук). Подписывается в `OnEnable`, отписывается в `OnDisable`; при пустом `_event` пишет `Debug.LogWarning`.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_event` | Ассет `GameEvent`, на который подписывается компонент |
| `_response` | `UnityEvent` — реакция; подключайте методы через Inspector (`+`) |

**Пример — один объект слушает два события:**

```
GameObject: «LightingSystem»
├── GameEventListener
│     Событие = PowerOff.asset
│     Реакция → LightingSystem.SetPower(false)
└── GameEventListener
      Событие = PowerRestore.asset
      Реакция → LightingSystem.SetPower(true)
```

Метод `OnEventRaised()` вызывается только из `GameEvent.Raise()` — напрямую не вызывать.

### `GameEventRaiser`

Файл: `Assets/Scripts/Events/GameEventRaiser.cs`. Поднимальщик события без кода: из любого чужого `UnityEvent` (кнопка UI, триггер, анимация) вызывается `RaiseEvent()`.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_event` | Ассет `GameEvent`, который поднимется при `RaiseEvent()` |

**Подключение:** `Button.OnClick → GameEventRaiser.RaiseEvent()`.

Рекомендация из кода скрипта: когда источник — ваш скрипт, лучше держать ссылку на `GameEvent` напрямую и вызывать `Raise()` — компонент `GameEventRaiser` нужен только для чужих `UnityEvent` без правки кода.

### `ObjectActivator`

Файл: `Assets/Scripts/Events/ObjectActivator.cs`. Самодостаточный компонент: показывает объект при срабатывании события — не требует `GameEventListener` на родителе, подписывается через `RegisterAction` напрямую.

**Принцип:** GameObject **не** выключается через `SetActive` — вместо этого отключаются `Renderer`-ы и (опционально) `Collider`-ы. Объект невидим и невзаимодействуем, но продолжает слушать события.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_activateEvent` | Событие, при срабатывании которого объект становится видимым |
| `_startHidden` | Скрывать объект при старте до срабатывания события (default: `true`) |
| `_toggleColliders` | Отключать коллайдеры вместе с рендерерами (default: `true`) |

**Публичные методы:** `Activate()` / `Deactivate()` — также доступны для вызова из других `UnityEvent` (кнопки, триггеры).

**Важная деталь порядка выполнения:** `[DefaultExecutionOrder(-100)]` — скрытие происходит в `Awake()` **раньше** `RoomController.Awake()`, чтобы RoomVisibilityManager не собрал эти рендереры как управляемые и не переактивировал их в `Start()`.

```
Awake  (-100): кэш Renderer/Collider детей, Deactivate() если _startHidden
OnEnable:      _activateEvent.RegisterAction(OnEventRaised)
OnDisable:     UnregisterAction
Событие:       OnEventRaised → Activate() (включить рендереры и коллайдеры)
```

---

## Типовой сценарий (цепочка)

```
Пазл решён → PuzzleSolved.Raise()
  ├── GameEventListener на LightingSystem → SetPower(true)
  ├── GameEventListener на Door           → Open()
  └── GameEventListener на AudioManager   → PlaySFX(powerOnClip)
```

### Реальный пример: `RoomPuzzleGate`

Файл: `Assets/Scripts/Room/RoomPuzzleGate.cs`. Скрывает комнату, пока не решена привязанная загадка. Разблокировка приходит **через GameEvent** — `GameEventListener` на объекте гейта подключает реакцию → `RoomPuzzleGate.Unlock()` (метод публичный, «вызывается из GameEventListener или напрямую из кода»).

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_unlockEvent` | Событие, разблокирующее комнату (например `PuzzleChemical_Solved`) |
| `_placeholder` | Заглушка, перекрывающая проход. Должна быть **отдельным** объектом (не дочерним к комнате), иначе `RoomController` будет управлять её рендерером |
| `_startLocked` | Комната скрыта при старте (default: `true`) |
| `_occlusionPortal` | Портал окклюжн-кулинга на дверном проёме; автопоиск в детях, открывается при разблокировке |
| `_saveId` | ID сохранения (default: `"room_gate_coridor_fdoor"`) — состояние `isUnlocked` сохраняется |

**Логика:**

```
Заблокировано:                              Unlock():
  SetGeometryActive(false) + Lock()           скрыть заглушку
  SetOcclusionCulling(true)                   включить триггеры RoomTrigger
  коллайдеры триггеров off                    Unlock() + SetGeometryActive(true)
  (RoomVisibilityManager не покажет комнату)  SetOcclusionCulling(false)
  заглушка Active                             открыть OcclusionPortal
  OcclusionPortal.open = false                SaveManager.Save()
```

---

## Как пользоваться (памятка)

1. **Создать событие:** `Create → Game Events → Game Event`, назвать по смыслу, положить в `Assets/Data/Events/`.
2. **Поднять:** поле `GameEvent` в своём скрипте → `Raise()`, либо компонент `GameEventRaiser` из чужого `UnityEvent`.
3. **Отреагировать:** `GameEventListener` на объекте-реагенте — событие в `_event`, метод в `_response`.
4. **Показать/скрыть объект:** `ObjectActivator` — скрыт до события, появляется по нему.
5. **Подписка из кода без компонента:** `_event.RegisterAction(handler)` / `UnregisterAction(handler)`.

## Взаимодействие с другими системами

| Система | Связь |
| ------- | ----- |
| Room Visibility System | `ObjectActivator` согласовывает скрытие с `RoomController` через порядок выполнения; `RoomPuzzleGate` отключает триггеры комнат |
| Lighting System | Типовой подписчик: включение/выключение питания по событиям |
| Save System | `RoomPuzzleGate` (ISaveable) сохраняет факт разблокировки |
