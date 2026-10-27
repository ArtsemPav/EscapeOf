Система хоррор-событий состоит из двух компонентов: `**HorrorSystem**` — singleton-координатор, и `**HorrorEvent**` — один хоррор-момент с настраиваемым триггером и эффектом. Смотри также [@ id="/Pages/Private/Code Locks.md" label="Code Locks"] — там описана интеграция с кодовым замком.

---

## Архитектура

```
HorrorSystem (GameObject)         # Singleton — слушает InventorySystem
├── Event_Mannequin (GameObject)  # HorrorEvent — один хоррор-момент
└── Event_RunPastDoor (GameObject)# Ещё один момент — добавляй сколько нужно
    └── SoundSource
```

`HorrorSystem` подписывается на:

- `InventorySystem.OnInventoryChanged` → обрабатывает триггеры `OnItemPickup`

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
| **Activation Delay**    | Задержка в секундах между срабатыванием триггера и началом эффекта     |


**Типы триггеров:**

> Триггер `OnRoomEnter` упомянут в комментарии enum, но **не реализован** в `SubscribeToTriggerSource()` — использовать нельзя.


| Trigger Type          | Когда срабатывает                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------------------ |
| `OnItemPickup`        | Игрок подбирает конкретный `ItemData`                                                                  |
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

> Таблица сверен с фактическим состоянием сцены `Game.unity`. Детальные описания событий — в секциях ниже.


| GameObject             | Event ID             | Триггер                                            | Эффект                          | Target                          |
| ---------------------- | -------------------- | -------------------------------------------------- | ------------------------------- | ------------------------------- |
| `Event_RunPastDoor`    | `run_past_door`      | `OnPlayerEnterZone` + FlashLight + electric puzzle | `AppearThenDisappearAfterDelay` | `MaidHolder` (горничная)        |
| `Event_VentMaid`       | `vent_maid`          | `OnManual`                                         | `AppearAndStay`                 | `/Env/Vent/Horror/MaidHolder`   |
| `Event_RatScare`       | `rat_scare`          | `OnManual` ← `CrouchTrigger`                       | `AppearAndStay`                 | `/Laboratory/Puzzle/Rat/Rat`    |
| `Event_DuckHorror`     | `duck_horror`        | `OnManual` ← клики по утке                         | `AppearThenDisappearAfterDelay` | `duckHorror` (утка с глазами)   |
| `Event_EntityAppear`   | `entity_appear`      | `OnPlayerEnterZone`                                | `AppearThenDisappearAfterDelay` | **null** — не настроено         |
| `Event_UVPentagram`    | — (свой скрипт)      | `CanTriggerZone` (банка корма)                     | Секвенция `UVPentagramSequence` | `CatHorror` (13 котов + свет)   |
| `Event_NurseryExit`    | `nursery_exit_scare` | `OnPuzzleSolved` (NurseryLock)                     | `AppearAndStay`                 | **null** — звонок телефона      |
| `Event_DoorKnock`      | —                    | (в `/Env/HorrorTriggers/`)                         | —                               | **null**                        |


> У `Event_EntityAppear` Target не назначен — эффект появления не сработает пока не назначить. Все события с `_ownsBackgroundMusic = true` при срабатывании глушат фоновую музыку (таймаут-страховка 30 с) и восстанавливают её при деактивации.

> **Enum-значения (проверено по `HorrorEvent.cs`):** `HorrorTriggerType` — `OnItemPickup = 0`, `OnManual = 2`, `OnPlayerEnterZone = 3`, `OnPuzzleSolved = 4`, `OnPowerStateChanged = 5`, `OnZoneSwitchChanged = 6`. Значение `1` отсутствует (не используется). `HorrorEffectType` — `AppearAndStay = 0`, `AppearThenDisappearOnLookAway = 1`, `AppearThenDisappearAfterDelay = 2`, `DisappearOnTrigger = 3`.


> У `Event_NurseryExit` и `Event_DoorKnock` Target не назначен / требует настройки.

### Триггеры OnPuzzleSolved / OnPowerStateChanged / OnZoneSwitchChanged — «выстрели сразу»

Все три подписки в `SubscribeToTriggerSource()` проверяют: если условие **уже выполнено при старте** (загадка решена из сейва, питание уже включено) — `Activate()` вызывается немедленно. Событие не ждёт следующего изменения состояния.

### Триггер OnPlayerEnterZone с Puzzle To Watch — предварительное условие

Если у события с триггером `OnPlayerEnterZone` назначен **Puzzle To Watch**, зона игнорируется до решения этой загадки. Так устроен `Event_RunPastDoor`: зона активна только после решения электрощитка.

### Триггеры OnItemPickup и OnManual — отличие в повторном срабатывании

- `OnItemPickup` обрабатывает `HorrorSystem.OnInventoryChanged()` — срабатывает когда нужный `ItemData` появляется в инвентаре.
- `OnManual` — только явный вызов `HorrorSystem.Instance.Trigger("event_id")` из кода (`CrouchTrigger`, `DuckHorrorInteractable` и т.д.).
- Оба события активируются один раз (`HasFired` защита) и сохраняются через сейв.

### Триггер OnRoomEnter — не реализован в коде

Значение присутствует в enum-комментарии (`OnRoomEnter` — тип 1) и в таблицах выше, но **case для него в `SubscribeToTriggerSource()` отсутствует** — событие с таким типом никогда не сработает. Если нужен триггер по входу в комнату — используйте `OnPlayerEnterZone` с RoomTrigger-коллайдером.

---

## Горничная (CharacterRunPast)

Скрипт `Assets/Scripts/Core/CharacterRunPast.cs` — аналог `RatRunAway` для персонажа-горничной. Метод `StartRun()` запускает движение по forward к целевой точке, по прибытии стреляет `UnityEvent _onReached`.

**Два события с горничной в сцене:**

### Event_RunPastDoor (`run_past_door`) — горничная ползёт по коридору

```
HorrorSystem/Event_RunPastDoor              # зона-триггер (BoxCollider trigger) + HorrorEvent
└── SoundSource                             # джампскейр-звук (2D, Play On Awake = off)

Env/2ndFloor/Corridor2stF/Horror
├── MaidHolder                              # Target — горничная (CharacterRunPast, скорость 5.5, анимация ×1.6, Crawl)
└── RunPosition                             # точка в конце коридора, куда ползёт
```

Цепочка при срабатывании (игрок вошёл в зону **и** у него есть фонарик **и** электрическая загадка решена):

1. `HorrorEvent.Activate()` — глушит фоновую музыку (`_ownsBackgroundMusic`, таймаут-страховка 30 сек)
2. `MaidHolder` активируется, играет джампскейр-звук
3. **On Activated**:
   - `MaidHolder.CharacterRunPast.StartRun()` — горничная ползёт в `RunPosition`
   - `doors_A/Doormesh.DoorInteraction.Open()` — дверь детской распахивается перед ней (игрок потом проходит свободно)
4. Доползла → `CharacterRunPast._onReached` → `HorrorEvent.Deactivate()` — горничная скрывается, музыка возвращается

> Сохраняется `HasFired` через сейв (`horror_run_past_door`) — не повторяется после загрузки.

### Event_VentMaid (`vent_maid`) — горничная в вентиляции

- **Триггер:** `OnManual` — вызывается из кода, `_activationDelay = 0.5` с
- **Target:** `/Env/Vent/Horror/MaidHolder` (CharacterRunPast), эффект `AppearAndStay`
- **Звук:** `/HorrorSystem/Event_VentMaid/SoundSource` (3D)
- **On Activated:** `CharacterRunPast.StartRun()` + `HorrorPostProcess.Activate()` + `ActivateLensMove()`
- **On Deactivated:** стоп звука + `FlashlightController.RestoreAfterForcedOff()` (фонарик принудительно гасился во время события) + `HorrorPostProcess.Deactivate()`

---

## Крыса — Event_RatScare (`rat_scare`)

Скример с крысой в лаборатории (1-й этаж). Игрок должен **присесть**, чтобы заглянуть под лабораторный стол — тогда крыса бежит на него.

**Цепочка:**

```
Игрок приседает и входит в ratTrigger (зона под столом)
  └─ CrouchTrigger (/Env/1stFloor/Laboratory/Puzzle/Rat/ratTrigger)
       └─ HorrorSystem.Instance.Trigger("rat_scare") — один раз, затем self-disable
            └─ HorrorEvent.Activate()
                 ├─ Target: /Env/1stFloor/Laboratory/Puzzle/Rat/Rat (модель PRF_Rat)
                 ├─ Звук: /HorrorSystem/Event_RatScare/SoundSource
                 └─ Фоновая музыка глушится (_ownsBackgroundMusic)
```

- **Триггер:** `OnManual` — срабатывает только через вызов из `CrouchTrigger` (`Assets/Scripts/Core/CrouchTrigger.cs`). Приседание обязательно: стоя игрок в триггер-зону не попадает. Сам триггер отключается после срабатывания.
- **Эффект:** `AppearAndStay` — крысу деактивирует её собственный скрипт `RatRunAway` (`_speed = 5`, `_lifetime = 1.5`) по прибытии или по таймеру.
- Сохраняется `HasFired` через сейв — не повторяется после загрузки.

---

## Резиновая утка — Event_DuckHorror (`duck_horror`)

Хоррор-событие в ванной 2-го этажа. Резиновая утка в туалете: изначально невидима, появляется при включении питания. Игрок кликает на неё — утка ныряет под воду и выныривает с плавучестью. После нескольких кликов — финальное погружение и скример.

**Цепочка:**

```
Питание включено → утка появляется
Игрок кликает утку N раз (DuckHorrorInteractable, IInteractable)
  └─ Ныряние: _diveDepth=0.12, _diveDuration=0.5, всплытие через _underwaterDuration=1.4
  └─ После MaxDives кликов — финальное погружение _finalUnderwaterDuration=2.5
       └─ HorrorSystem.Instance.Trigger("duck_horror")
            └─ HorrorEvent.Activate()
                 ├─ Target: /Env/2ndFloor/Bathroom2ndFloor/Puzzle/DuckHorror/duckHorror
                 │           (модель с глазами aye1/aye2 — «злой» вариант утки)
                 ├─ Звук: сам объект duckHorror (3D)
                 └─ Disappear Delay = 1 с — утка исчезает через секунду после появления
```

- **Триггер:** `OnManual` — срабатывает только из `DuckHorrorInteractable` (`Assets/Scripts/Interaction/DuckHorrorInteractable.cs`).
- **Эффект:** `AppearThenDisappearAfterDelay` (1 с).
- `DuckHorrorInteractable` также `ISaveable` — прогресс ныряний сохраняется.

---

## Пентаграмма — Event_UVPentagram (`uv_pentagram_sequence`)

Многофазная секвенция в кладовой (Pantry 2, 1-й этаж). Не обычный `HorrorEvent` — отдельный скрипт `UVPentagramSequence` (`Assets/Scripts/Effects/UVPentagramSequence.cs`, ISaveable, SaveId `uv_pentagram_sequence`).

**Цепочка:**

```
Игрок приносит банку кошачьего корма (/Env/MovibleObjects/catcan) в зону CanTrigger
  └─ CanTriggerZone (BoxCollider trigger на /Env/1stFloor/Pantry 2/Puzzle/CatHorror/CanTrigger)
       ├─ Ждёт _triggerDelay после входа банки в зону (грейс _startGracePeriod на старте сцены)
       └─ UVPentagramSequence.BeginFromCanTrigger()
            └─ Фазы: Idle → Waiting → Shrinking → Playing → Finished
                 ├─ Shrinking: пентаграмма сжимается (_shrinkDuration=0.35, EaseInOut)
                 ├─ Playing: музыка с BPM-синхронизацией (_bpm=128), вращение (_spinSpeed=60°/с по часовой)
                 ├─ Частицы включаются на _particlesStartTime=32 с
                 └─ Эффекты обрезаются за _effectsCutoffBeforeEnd=0.5 с до конца музыки
```

- **Содержимое префаба `CatHorror`:** 13 котов (`uiiafixed`), «разрезанный» кот (`CatAnimation/uiiaCuted`), 4 световых столпа (`LightPilars/Pilar1-4`), партиклы `vfx_Hyperdrive_01`, спот-лайты.
- `_catHorrorInstance` → `/Env/1stFloor/Pantry 2/Puzzle/CatHorror`, `_catCanObject` → `/Env/MovibleObjects/catcan`.
- `_onMusicStarted` / `_onMusicFinished` — UnityEvent для доп. реакций (пост-процесс, звуки).
- `HasFired` сохраняется через сейв — секвенция проигрывается один раз за прохождение.
- Editor-дебаг: `_enableEditorDebugHotkey` + клавиша `O` (`DebugPlay()`).
- Статические свойства для внешних проверок: `MusicPlaying`, `ClimaxStarted`, `InstanceCatTransform`, `InstanceMusicSource`.

---

## Сущность — Event_EntityAppear (`entity_appear`)

Появление сущности в зоне. **Настроено не полностью.**

- **Триггер:** `OnPlayerEnterZone` — BoxCollider на самом `Event_EntityAppear` (позиция `/HorrorSystem/Event_EntityAppear`).
- **Эффект:** `AppearThenDisappearAfterDelay` (2.5 с), но **Target = null** — нужно назначить объект сущности.
- **Звук:** не назначен.
- **_ownsBackgroundMusic = true** — при срабатывании глушит фоновую музыку (таймаут-страховка 30 с).
- Сохраняется `HasFired` через сейв.