Система хоррор-событий состоит из двух компонентов: `**HorrorSystem**` — singleton-координатор, и `**HorrorEvent**` — один хоррор-момент с настраиваемым триггером и эффектом. Смотри также [@ id="/Pages/Private/Code Locks.md" label="Code Locks"] — там описана интеграция с кодовым замком.

---

## Архитектура

```
HorrorSystem (GameObject)         # Singleton — слушает InventorySystem и GameManager
├── Event_Mannequin (GameObject)  # HorrorEvent — один хоррор-момент
└── Event_RunPastDoor (GameObject)# Ещё один момент — добавляй сколько нужно
    └── RunDestination
```

`HorrorSystem` подписывается на:

- `InventorySystem.OnInventoryChanged` → обрабатывает триггеры `OnItemPickup`
- `GameManager.OnRoomChanged` → обрабатывает триггеры `OnRoomEnter`

`HorrorEvent` может сам подписываться на источники событий для триггеров `OnPuzzleSolved`, `OnPowerStateChanged`, `OnZoneSwitchChanged` — без участия `HorrorSystem`.

Каждый `HorrorEvent` регистрируется в `HorrorSystem` автоматически при старте.

---

## Настройка в сцене

### Шаг 1 — HorrorSystem

1. Создай GameObject `HorrorSystem` в корне сцены (или любом удобном месте)
2. Добавь компонент `HorrorSystem`
3. Этот объект должен оставаться активным всегда

### Шаг 2 — Добавить хоррор-момент

1. Создай дочерний GameObject под `HorrorSystem`, например `Event_Mannequin`
2. Добавь компонент `HorrorEvent`
3. Настрой поля в Inspector (описание ниже)
4. Убедись, что целевой объект (`_target`) **неактивен** в сцене — `HorrorEvent` включит его сам

---

## Поля компонента HorrorEvent

### Identity


| Поле         | Описание                                                                                         |
| ------------ | ------------------------------------------------------------------------------------------------ |
| **Event Id** | Уникальный строковый ID. Нужен для ручного вызова из кода: `HorrorSystem.Instance.Trigger("id")` |


### Trigger — что запускает событие


| Поле                    | Описание                                                               |
| ----------------------- | ---------------------------------------------------------------------- |
| **Trigger Type**        | Тип триггера — см. таблицу ниже                                        |
| **Required Item**       | Предмет, который должен попасть в инвентарь (`OnItemPickup`)           |
| **Required Room Index** | Индекс комнаты при входе в которую срабатывает событие (`OnRoomEnter`) |
| **Activation Delay**    | Задержка в секундах между срабатыванием триггера и началом эффекта     |


**Типы триггеров:**


| Trigger Type          | Когда срабатывает                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------------------ |
| `OnItemPickup`        | Игрок подбирает конкретный `ItemData`                                                                  |
| `OnRoomEnter`         | `GameManager.OnRoomChanged` даёт индекс = `Required Room Index`                                        |
| `OnManual`            | Только явный вызов: `HorrorSystem.Instance.Trigger("id")` или UnityEvent                               |
| `OnPlayerEnterZone`   | Игрок входит в trigger-коллайдер на этом GameObject (нужен `BoxCollider` с `Is Trigger = true`)        |
| `OnPuzzleSolved`      | Привязка к загадке. Укажи **Puzzle To Watch** (объект с `PuzzleModeController`). Сработает при решении |
| `OnPowerStateChanged` | Привязка к электричеству. Сработает когда питание `LightingSystem` перейдёт в **Required Power State** |
| `OnZoneSwitchChanged` | Привязка к выключателю света. Укажи **Required Zone Id** и **Required Zone State**                     |


### Puzzle Trigger


| Поле                | Описание                                                                |
| ------------------- | ----------------------------------------------------------------------- |
| **Puzzle To Watch** | Объект с `PuzzleModeController`. Событие сработает когда загадка решена |


> Если загадка уже решена (восстановлена из сейва) — событие сработает сразу при старте.

### Power Trigger


| Поле                     | Описание                                                               |
| ------------------------ | ---------------------------------------------------------------------- |
| **Required Power State** | `true` = сработает когда питание включится, `false` = когда выключится |


> Если питание уже в нужном состоянии при старте — сработает сразу.

### Zone Switch Trigger


| Поле                    | Описание                                                               |
| ----------------------- | ---------------------------------------------------------------------- |
| **Required Zone Id**    | Строковый ID зоны освещения (должен совпадать с ID в `LightingSystem`) |
| **Required Zone State** | `true` = сработает когда свет включится, `false` = когда выключится    |


> Если зона уже в нужном состоянии при старте — сработает сразу.

### Effect — что происходит с целевым объектом


| Поле                | Описание                                                                         |
| ------------------- | -------------------------------------------------------------------------------- |
| **Effect Type**     | Тип эффекта — см. таблицу ниже                                                   |
| **Target**          | GameObject который будет показан/скрыт. Должен быть неактивен в сцене изначально |
| **Disappear Delay** | Задержка перед скрытием (только для `AppearThenDisappearAfterDelay`)             |


**Типы эффектов:**


| Effect Type                     | Поведение                                                                     |
| ------------------------------- | ----------------------------------------------------------------------------- |
| `AppearAndStay`                 | Target активируется и остаётся навсегда (до ручного скрытия)                  |
| `AppearThenDisappearOnLookAway` | Target появляется; исчезает когда игрок посмотрит на него, а затем отвернётся |
| `AppearThenDisappearAfterDelay` | Target появляется и автоматически исчезает через `Disappear Delay` секунд     |
| `DisappearOnTrigger`            | Target стартует видимым; скрывается при срабатывании триггера                 |


### Look Detection — настройка обнаружения взгляда

Используется только при эффекте `AppearThenDisappearOnLookAway`.


| Поле                    | По умолчанию  | Описание                                                                                           |
| ----------------------- | ------------- | -------------------------------------------------------------------------------------------------- |
| **Player Camera**       | `Camera.main` | Камера для проверки взгляда. Оставь пустым — назначится автоматически                              |
| **Look At Threshold**   | `0.7`         | Dot product выше которого игрок считается смотрящим на target. `0.7` ≈ в пределах 45°, `0.5` ≈ 60° |
| **Look Away Threshold** | `0.0`         | Dot product ниже которого игрок считается отвернувшимся. `0` = 90° от target                       |


**Логика обнаружения взгляда — два этапа:**

```
Фаза 1: ожидание первого взгляда
  dot >= Look At Threshold → _hasSeenTarget = true → переход в Фазу 2

Фаза 2: ожидание отворота (проверяется только после Фазы 1)
  dot < Look Away Threshold → Target.SetActive(false)
```

Это гарантирует что объект не исчезнет пока игрок не посмотрел на него хотя бы раз.

### Callbacks — дополнительные реакции


| Поле               | Когда вызывается                                                                       |
| ------------------ | -------------------------------------------------------------------------------------- |
| **On Activated**   | Сразу после появления target (после задержки). Подключи: AudioSource, Animator, и т.д. |
| **On Deactivated** | После скрытия target. Подключи: звуки исчезновения, эффекты                            |


---

## Примеры привязки к игровым системам

### Загадка решена → хоррор-момент

1. Trigger Type = `OnPuzzleSolved`
2. Puzzle To Watch → перетащи объект с `PuzzleModeController` (например, объект электрической загадки)
3. Effect Type = `AppearThenDisappearOnLookAway`
4. Target → манекен или другой объект (неактивен в сцене)

### Электричество включено → хоррор-момент

1. Trigger Type = `OnPowerStateChanged`
2. Required Power State = `true`
3. Effect Type = `AppearAndStay`
4. Target → объект который появится когда свет включится

### Выключатель света → хоррор-момент

1. Trigger Type = `OnZoneSwitchChanged`
2. Required Zone Id = ID зоны (например `"Kitchen"`)
3. Required Zone State = `true` (когда свет включается) или `false` (когда выключается)
4. Effect Type = `AppearThenDisappearAfterDelay`
5. Target → объект, Disappear Delay = 2 сек

### Универсальная привязка через GameEvent

Если нужного триггера нет в списке:

1. Trigger Type = `OnManual`
2. Добавь компонент `GameEventListener` на этот же GameObject
3. Назначь `GameEvent` (ScriptableObject .asset)
4. В Response подключи `HorrorEvent → Activate()`

Или из кода: `HorrorSystem.Instance.Trigger("event_id");`

---

## Привязка звука к ивенту (On Activated)

Связь между триггером и звуком настраивается через **UnityEvent** в поле **On Activated** на компоненте `HorrorEvent`. В Hierarchy эта связь **не видна** — только в Inspector.

### Структура в сцене

```
Env
├── HorrorTriggers              # папка для зон-триггеров
│   └── Event_DoorKnock         # зона (BoxCollider trigger) + HorrorEvent
│
└── HorrorSound                 # папка для источников звука
    └── FirstEventDoor          # AudioSource (3D, Play On Awake = false)
```

Триггер и источник звука — **разные объекты**. Триггер активирует событие, источник звука — проигрывает звук. Связь между ними — в поле **On Activated**.

### Как настроить привязку в Inspector

1. Выбери объект с `HorrorEvent` (например `Event_DoorKnock`)
2. В компоненте `HorrorEvent` найди секцию **Callbacks** → **On Activated**
3. Нажми `+`
4. В поле **Object** перетащи объект с `AudioSource` (например `FirstEventDoor`)
5. В выпадающем меню выбери `AudioSource → Play()`

```
On Activated
  ┌──────────────────────────────────────────┐
  │  ☑ Runtime Only                          │
  │  FirstEventDoor           ← объект       │
  │  AudioSource.Play()       ← метод        │
  └──────────────────────────────────────────┘
```

### Как проверить привязку

1. Выбери объект с `HorrorEvent` (например `Event_DoorKnock`)
2. В Inspector найди **On Activated**
3. Там должен быть указан целевой объект и метод:


| Поле     | Что должно быть                                    |
| -------- | -------------------------------------------------- |
| Object   | `FirstEventDoor` (или другой объект с AudioSource) |
| Function | `AudioSource.Play`                                 |


### Альтернатива — PlayGlobalSFX (глобальный звук)

Если не нужна 3D-позиция звука (звук везде одинаковой громкости):

1. В **On Activated** нажми `+`
2. В поле **Object** перетащи **сам объект с HorrorEvent** (например `Event_DoorKnock`)
3. Выбери `HorrorEvent → PlayGlobalSFX(AudioClip)`
4. В поле аргумента перетащи аудиоклип (например `knock.aif`)

### Можно добавить несколько действий

В **On Activated** можно нажать `+` несколько раз и подключить разные действия:

```
On Activated
  ├── FirstEventDoor → AudioSource.Play()           # стук в дверь
  ├── Event_Phone → HorrorInteractable.Arm()         # телефон зазвонит
  └── AudioManager → PlaySFX(creakClip)              # скрип половицы
```

Все действия выполняются одновременно при срабатывании триггера.

---

## Интеграция с CodeLock

`CodeLock` поддерживает два события:


| Событие             | Когда                                                 |
| ------------------- | ----------------------------------------------------- |
| **On Panel Opened** | Игрок открывает панель ввода кода (жмёт `E` на замке) |
| **On Unlocked**     | Игрок вводит правильный код                           |


Типичная схема для первой комнаты:

```
CodeLock._onPanelOpened
  └─ HorrorEvent.Deactivate()     ← манекен исчезает когда игрок подходит к замку

CodeLock._onUnlocked
  └─ DoorInteraction.UnlockAndOpen()  ← дверь открывается
```

Как подключить в Inspector:

1. Выбери объект с `CodeLock`
2. В секции **On Panel Opened** нажми `+`
3. Перетащи GameObject с `HorrorEvent`
4. Выбери `HorrorEvent → Deactivate()`

---

## Ручное управление из кода

```csharp
// Активировать событие по ID
HorrorSystem.Instance.Trigger("my_event_id");

// Активировать или деактивировать конкретный HorrorEvent напрямую
horrorEvent.Activate();
horrorEvent.Deactivate();

// Проверить состояние
bool alreadyFired = horrorEvent.HasFired;
```

> `Activate()` срабатывает только один раз — повторный вызов игнорируется (`HasFired` защита).

---

## Как добавить новый хоррор-момент

1. Создай дочерний GameObject под `HorrorSystem`, дай понятное имя (`Event_LightFlicker`, `Event_Shadow`, и т.д.)
2. Добавь компонент `HorrorEvent`
3. Настрой **Trigger Type** — что запускает событие
4. Настрой **Effect Type** — что происходит
5. Назначь **Target** — объект который появится/исчезнет
6. При необходимости подключи **On Activated** / **On Deactivated** UnityEvents для звука или анимации
7. Убедись что **Target** неактивен (отключён) в сцене изначально

---

## Текущие события в сцене


| GameObject          | Триггер                     | Эффект                          | Target   |
| ------------------- | --------------------------- | ------------------------------- | -------- |
| `Event_Mannequin`   | `OnItemPickup` (FlashLight) | `AppearThenDisappearOnLookAway` | **null** |
| `Event_RunPastDoor` | `OnPlayerEnterZone`         | `AppearThenDisappearAfterDelay` | **null** |
| `Event_DoorKnock`   | (в `/Env/HorrorTriggers/`)  | —                               | **null** |
| `Event_NurseryExit` | (в `/HorrorSystem/`)        | —                               | **null** |


> У всех событий Target не назначен — эффект появления/исчезновения не сработает пока не назначить. `Event_DoorKnock` и `Event_NurseryExit` добавлены позже и требуют настройки триггера и эффекта.