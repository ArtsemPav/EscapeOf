Система хоррор-событий состоит из двух компонентов: `**HorrorSystem**` — singleton-координатор, и `**HorrorEvent**` — один хоррор-момент с настраиваемым триггером и эффектом. Смотри также [@ id="/Pages/Private/Code Locks.md" label="Code Locks"] — там описана интеграция с кодовым замком.

---

## Архитектура

```
HorrorSystem (GameObject)         # Singleton — слушает InventorySystem
├── Event_Mannequin (GameObject)  # HorrorEvent — один хоррор-момент
└── Event_RunPastDoor (GameObject)# Ещё один момент — добавляй сколько нужно
    └── RunDestination
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


| Поле                 | Описание                                                                        |
| -------------------- | ------------------------------------------------------------------------------- |
| **Trigger Type**     | Тип триггера — см. таблицу ниже                                                 |
| **Required Item**    | Предмет, который должен попасть в инвентарь (`OnItemPickup`)                    |
| **Player Tag**       | Тег коллайдера, активирующего зону (`OnPlayerEnterZone`). По умолчанию `Player` |
| **Activation Delay** | Задержка в секундах между срабатыванием триггера и началом эффекта              |


**Типы триггеров:**


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

### Sound — звук и музыка

Звук настраивается полями компонента — вручную подключать `AudioSource.Play()` в **On Activated** больше не нужно.


| Поле                            | По умолчанию | Описание                                                                                                                                       |
| ------------------------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| **Sound Object**                | (пусто)      | GameObject с `AudioSource`, который проиграется при активации (например дочерний `SoundSource`). `Play On Awake = false`, `Spatial Blend = 3D` |
| **Sound Fade Duration**         | `1.5`        | Длительность плавного затухания звука в `FadeOutSoundObject()`                                                                                 |
| **Music Mute Fade Duration**    | `2`          | За сколько секунд фоновая музыка глушится при активации (`0` = мгновенно)                                                                      |
| **Music Restore Fade Duration** | `3`          | За сколько секунд фоновая музыка возвращается после события                                                                                    |


### Music Ownership — автоматическое управление музыкой


| Поле                      | По умолчанию | Описание                                                                                                  |
| ------------------------- | ------------ | --------------------------------------------------------------------------------------------------------- |
| **Owns Background Music** | `true`       | Событие само глушит фоновую музыку при активации и возвращает её при завершении                           |
| **Music Mute Timeout**    | `30`         | Страховка: сколько секунд музыка остаётся заглушённой, если событие не завершилось. `0` = без ограничения |


Глушение идёт через `MusicDirector.MuteForEvent()` / `RestoreAfterEvent()` — MusicDirector считает владельцев мьюта, поэтому вложенные события безопасны (музыка вернётся, когда последний владелец отпустит).

### Interactable — интерактивный объект


| Поле                    | Описание                                                                                                                                                                                                                                   |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Interactable Object** | GameObject с `HorrorInteractable` (телефон, картина, радио). При активации события автоматически вызывается `Arm()` — игрок сможет взаимодействовать. Когда игрок ответит — `StopSoundObject()` вызовется автоматически, звук остановится. |


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
| **On Activated**   | Сразу после появления target (после задержки). Подключи: анимации, доп. эффекты и т.д. |
| **On Deactivated** | После скрытия target. Подключи: звуки исчезновения, эффекты                            |


### Публичные методы HorrorEvent

Методы для ручного вызова из кода или UnityEvent:


| Метод                         | Описание                                                                                            |
| ----------------------------- | --------------------------------------------------------------------------------------------------- |
| `Activate()` / `Deactivate()` | Запустить событие / скрыть target (`Activate` игнорирует повторные вызовы — `HasFired` защита)      |
| `StopSoundObject()`           | Остановить звук на Sound Object. Вызывается автоматически из `HorrorInteractable` при ответе игрока |
| `PlayGlobalSFX(AudioClip)`    | Проиграть клип глобально (без 3D-позиции) через `AudioManager`                                      |
| `MuteBackgroundMusic()`       | Глушить фоновую музыку (фейд `_musicMuteFadeDuration`). Пара к `RestoreBackgroundMusic()`           |
| `RestoreBackgroundMusic()`    | Вернуть фоновую музыку (фейд `_musicRestoreFadeDuration`). Безопасен без мьюта                      |
| `FadeOutSoundObject()`        | Плавно заглушить звук Sound Object за `_soundFadeDuration` секунд, затем остановить                 |


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

## Привязка звука к ивенту

Основной способ — поле **Sound Object** на `HorrorEvent`: перетащи туда GameObject с `AudioSource` из сцены (например дочерний `SoundSource`). При активации события звук проиграется автоматически, при деактивации — остановится. В Hierarchy эта связь **не видна** — только в Inspector.

### Структура в сцене

```
Env
├── HorrorTriggers              # папка для зон-триггеров
│   └── Event_DoorKnock         # зона (BoxCollider trigger) + HorrorEvent
│
└── HorrorSound                 # папка для источников звука
    └── FirstEventDoor          # AudioSource (3D, Play On Awake = false)
```

Триггер и источник звука — **разные объекты**. Триггер активирует событие, источник звука — проигрывает звук. Связь между ними — поле **Sound Object**.

### Как настроить привязку в Inspector

1. Выбери объект с `HorrorEvent` (например `Event_DoorKnock`)
2. В компоненте `HorrorEvent` найди секцию **Sound** → **Sound Object**
3. Перетащи объект с `AudioSource` (например `FirstEventDoor`)

### Как проверить привязку

1. Выбери объект с `HorrorEvent` (например `Event_DoorKnock`)
2. В Inspector найди поле **Sound Object** — там должен быть указан объект с AudioSource:


| Поле         | Что должно быть                                    |
| ------------ | -------------------------------------------------- |
| Sound Object | `FirstEventDoor` (или другой объект с AudioSource) |


### Альтернатива — UnityEvent в On Activated

Для сложных цепочек (несколько звуков, анимации) используй **On Activated**:

1. В **On Activated** нажми `+`
2. В поле **Object** перетащи объект с действием
3. Выбери метод — например `AudioSource.Play()` или `HorrorEvent → PlayGlobalSFX(AudioClip)`

`PlayGlobalSFX` удобен, когда не нужна 3D-позиция звука (звук везде одинаковой громкости): перетащи **сам объект с HorrorEvent** и выбери `HorrorEvent → PlayGlobalSFX(AudioClip)`, в поле аргумента укажи аудиоклип.

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

## Deprecated — HorrorTrigger

`Assets/Scripts/Core/HorrorTrigger.cs` — устаревшая заглушка, заменён парой `HorrorEvent` + `HorrorSystem`. Не используй его в новых сценах и удаляй с существующих объектов, заменяя на `HorrorEvent`.

---

## Текущие события в сцене

> Таблица сверен с фактическим состоянием сцены `Game.unity`. Детальные описания событий — в секциях ниже.


| GameObject           | Event ID             | Триггер                                            | Эффект                          | Target                        |
| -------------------- | -------------------- | -------------------------------------------------- | ------------------------------- | ----------------------------- |
| `Event_Mannequin`    | —                    | `OnItemPickup` (FlashLight)                        | `AppearThenDisappearOnLookAway` | **null**                      |
| `Event_RunPastDoor`  | `run_past_door`      | `OnPlayerEnterZone` + FlashLight + electric puzzle | `AppearThenDisappearAfterDelay` | `MaidHolder` (горничная)      |
| `Event_VentMaid`     | `vent_maid`          | `OnManual`                                         | `AppearAndStay`                 | `/Env/Vent/Horror/MaidHolder` |
| `Event_RatScare`     | `rat_scare`          | `OnManual` ← `CrouchTrigger`                       | `AppearAndStay`                 | `/Laboratory/Puzzle/Rat/Rat`  |
| `Event_DuckHorror`   | `duck_horror`        | `OnManual` ← клики по утке                         | `AppearThenDisappearAfterDelay` | `duckHorror` (утка с глазами) |
| `Event_EntityAppear` | `entity_appear`      | `OnPlayerEnterZone`                                | `AppearThenDisappearAfterDelay` | **null** — не настроено       |
| `Event_UVPentagram`  | — (свой скрипт)      | `CanTriggerZone` (банка корма)                     | Секвенция `UVPentagramSequence` | `CatHorror` (13 котов + свет) |
| `Event_NurseryExit`  | `nursery_exit_scare` | `OnPuzzleSolved` (NurseryLock)                     | `AppearAndStay`                 | **null** — звонок телефона    |
| `Event_DoorKnock`    | —                    | (в `/Env/HorrorTriggers/`)                         | —                               | **null**                      |


> У `Event_Mannequin` Target не назначен — эффект появления не сработает пока не назначить. `Event_EntityAppear` тоже требует настройки Target и звука. Все события с `_ownsBackgroundMusic = true` при срабатывании глушат фоновую музыку (таймаут-страховка 30 с) и восстанавливают её при деактивации.

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
                 ├─ Частицы включаются на _particlesStartTime (код-дефолт 20 с)
                 └─ Эффекты обрезаются за _effectsCutoffBeforeEnd (код-дефолт 1.5 с) до конца музыки
```

- **Авто-подключение ссылок:** `CanTriggerZone._sequence` и `_canRigidbody` не хранятся в сериализации (компонент живёт в префабе комнаты, а цель — объект сцены; ссылка сбрасывалась при обновлении префаба). Оба резолвятся в рантайме в `Awake()`: секвенция через `FindFirstObjectByType<UVPentagramSequence>()`, банка — через `UVPentagramSequence.CatCanObject`. Если что-то не нашлось — Warning в консоль. Заполненные в инспекторе поля используются как есть.
- **Дверь комнаты:** `_doorSaveId` на `UVPentagramSequence` (по умолчанию `door_pantry_main` — дверь кладовой `/Env/1stFloor/Pantry 2/Walls/doorLab/door`). При старте музыки дверь закрывается и запирается (`CloseAndLock()`), по завершении трека — отпирается и открывается (`Open()`). Дверь ищется по `SaveId`, а не ссылкой — по той же причине: триггер и дверь в разных префабах.

- **Содержимое префаба `CatHorror`:** 13 котов (`uiiafixed`), «разрезанный» кот (`CatAnimation/uiiaCuted`), 4 световых столпа (`LightPilars/Pilar1-4`), партиклы `vfx_Hyperdrive_01`, спот-лайты.
- `_onMusicStarted` / `_onMusicFinished` — UnityEvent для доп. реакций (пост-процесс, звуки). Плюс `_timeline` — массив `TimelineEntry { time, onTime }`: UnityEvent срабатывают на заданных секундах трека для эскалации странности.
- Статическое событие `OnBeat` — стреляет на каждый бит музыки (BPM-синхронизация).
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

---

## Паучьи ивенты (SpiderRunPast / SpiderLeapScare)

> **Статус:** скрипты есть в проекте (`Assets/Scripts/Effects/`), но их триггерные зоны и объекты пауков в текущей сцене `Game.unity` не расставлены. Секция сохранена как справочник — если паучьи ивенты вернутся в сцену.

---

## Невидимая блуждающая сущность — GhostSystem (полтергейст)

Невидимый агент патрулирует дом по waypoint-графу и **активно охотится на игрока**, когда видит его.
Зрение честное — physics raycast: стены, мебель и **закрытые двери** блокируют видимость
(закрылся в комнате — спрятался), открытые дверные полотна просматриваются насквозь.
Само перемещение бесплотное (сквозь стены) — дух, но глаза у него не бесплотные.

### Состояния

```
Patrol    — дрейф по waypoint-графу; каждый кадр ищет игрока (если погоня разблокирована
            и не на кулдауне); на стоянке шалит: выключатели, двери, швыряние предметов
Chase     — увидел игрока: летит к нему на _chaseSpeed, цель = позиция игрока
Search    — потерял из виду > _loseSightTime: летит к последней видимой точке,
            висит там _searchDwellTime, потом снова Patrol
Unpowered — общий свет выключен: агент спрятан, ждёт `LightingSystem.OnPowerChanged`
```

**Гейт погони:** `_huntUnlockPuzzle` ссылается на `PuzzleModeController` часов
(`/Env/2ndFloor/Room 4/Puzzle/clock`). Пока загадка не решена (в т.ч. после загрузки сейва),
`TrySpotPlayer` не срабатывает — призрак не преследует игрока, только летает, топает и щёлкает
выключателями. После решения — охотится как обычно.

Достижение игрока (`<= _catchDistance`) вызывает `_onPlayerReached` (UnityEvent — повесь скример/урон).
Призрак НЕ десипаунится: он сразу возвращается в Patrol и продолжает шалости, но новая погоня
блокируется кулдауном `_huntCooldownRange` (5–10 минут, `IsHuntOnCooldown`).

### Архитектура

```
GhostSystem (сцена, корень)        # GhostPatrol — координатор (сам не двигается)
├── WP_Room1_2F                   # GhostWaypoint (allowedRooms: Room 1, weight 3)
├── WP_Corridor2F                 # GhostWaypoint (Coridor2stF, CoridorFDoor, weight 3)
├── WP_Room4_2F                   # GhostWaypoint (Room 4, Room 5, weight 3)
├── WP_DoctorRoom                 # GhostWaypoint (DoctorRoom)
├── WP_Procedural                 # GhostWaypoint (Procedural)
├── WP_Pantry2                    # GhostWaypoint (Pantry 2, Pantry)
├── WP_GeneratorRoom              # GhostWaypoint — подвал, генераторная (weight 1)
└── WP_Switchgear                 # GhostWaypoint — подвал, щитовая (weight 1)

GhostAgent (сцена, корень)         # двигающийся объект: сфера + Point Light (debug-визуал),
                                   # AudioSource (3D, linear rolloff, max 35 м) + GhostFootsteps (шаги в полёте)

> **Приглушение по этажам** (GhostFootsteps): дистанция считается вручную (rolloff
> AudioSource фактически отключён, maxDistance=35). На этаже игрока: до `_fullVolumeDistance`
> (4 м) — полная громкость × `_sameFloorVolumeFactor` (0.6), плавное затухание до
> `_farVolumeFactor` (0.35) к `_fadeoutDistance` (14 м). На другом этаже (разрыв Y > 3 м):
> ровный глухой уровень × `_otherFloorVolumeFactor` (0.4) + low-pass 600 Гц по всему этажу.
```

> **Распределение по этажам:** вес точки (`_visitWeight`) — относительный шанс выбора при
> патруле (взвешенный рандом в `GhostPatrol.PickNextWaypoint`). Точки 2F имеют вес 3,
> 1F — 1, подвал — 1: призрак проводит большую часть времени на 2 этаже, но регулярно
> спускается на 1F и изредка в подвал (GeneratorRoom/Switchgear). Финальная зона
> (`CoridorFDoor/FinalPart`) waypoint'а не имеет — призрак туда не заглядывает.
> Автолинковка считается по горизонтальной (XZ) дистанции — вертикальный разлом
> этажей не разрывает граф, призрак летает сквозь перекрытия by design.

> **Важно:** waypoint'ы НЕ должны быть детьми `GhostAgent` — иначе карта точек «уезжает» вместе
> с агентом и накапливает смещение. `_agentRoot` на `GhostPatrol` указывает на `GhostAgent`.
> Для релиза `GhostAgent` можно скрыть (сфера/свет — только debug-визуал для отслеживания).

### Скрипты

- `GhostPatrol.cs` — `/Assets/Scripts/Effects/` — FSM патруль/погоня/поиск: дрейф по waypoint'ам,
честное зрение через `Physics.RaycastAll` (блокируют стены, мебель, закрытые `DoorInteraction`;
открытые двери просматриваются), погоня на `_chaseSpeed`, авта-линковка соседей
по `_autoLinkDistance` (15 м; путь сквозь стены — by design), `_onPlayerReached` при поимке,
после неё — кулдаун охоты `_huntCooldownRange` (5–10 мин) без десипауна: призрак продолжает шалить.
- `GhostFootsteps.cs` — `/Assets/Scripts/Effects/` — шаги, синхронизированные с реальным
перемещением: один шаг на `_stepLength` (0.7 м) в патруле, `_runStepLength` (1.6 м) в погоне —
длинный тяжёлый бег вместо «семенящих» шажков; быстрее погоня — шире шаг, а не чаще;
чередование лево/право (`_leftClip` / `_rightClip`, пара `S_ks_left`/`S_ks_right`);
узкий рандом питча 0.95–1.05 и громкость на каждом шаге; в покое тишина.
- `GhostWaypoint.cs` — `/Assets/Scripts/Effects/` — логика провокации: детект игрока
(`_playerDetectionRadius`), фильтр по комнате игрока (`RoomVisibilityManager.CurrentRoomName`
из `_allowedRooms`), глобальный + пер-точечный кулдаун, `_provocationChance`,
последовательность звуковых шагов (`_soundSteps`: задержка + AudioSource).

### Логика

1. `GhostPatrol` стартует в случайной точке, каждые 4–12 с выбирает следующую (из Connected)
  и двигает `_agentRoot` (= `GhostAgent`) к ней.
2. По прибытии вызывает `GhostWaypoint.OnGhostArrived()`.
3. Точка проверяет: игрок в радиусе? в разрешённой комнате? прошёл кулдаун? прошёл шанс?
4. Если да — проигрывает `_soundSteps` (отложенные `AudioSource.Play()` в 3D).

### Настройка (текущая)


| Параметр         | GhostPatrol                               | GhostWaypoint                    |
| ---------------- | ----------------------------------------- | -------------------------------- |
| Скорость патруля | `_speed` 1.0 м/с                          | `_playerDetectionRadius` 12 м    |
| Скорость погони  | `_chaseSpeed` 4.5 м/с (чуть медленнее шага игрока 5) | —                                |
| Дальность зрения | `_viewDistance` 15 м (raycast, честный)   | —                                |
| Потеря из виду   | `_loseSightTime` 4 с → Search             | —                                |
| Поиск на месте   | `_searchDwellTime` 3 с                    | —                                |
| Поимка           | `_catchDistance` 1 м → `_onPlayerReached` | —                                |
| Исчезновение     | после поимки `GhostAgent` деактивируется  | —                                |
| Кулдаун охоты    | после поимки `_huntCooldownRange` 300–600 с (5–10 мин); призрак не десипаунится — продолжает патруль и шалости, но не начинает погоню (`IsHuntOnCooldown`) | — |
| Вариация скорости| `_patrolSpeedMultiplierRange` 0.6–1.6, переролл каждые 5–12 с, бленд 2 с | — |
| Звук поимки      | `_onPlayerReached` → `AudioSource.Play()` на `/GhostSystem` (whisper whoosh) | — |
| Щелчки выключателей | шанс 50% каждые 8 с во время стоянки, радиус 10 м, кулдаун 30 с; 70% гасит / 30% зажигает; щелчок — 3D one-shot на позиции свитча (max 15 м) | — |
| Полтергейст-швыряние | шанс 40% каждые 10 с во время стоянки, радиус 6 м, кулдаун 45 с; бросок 4–7 м/с горизонталь + 1.5–3.5 м/с вверх, предметы тяжелее 15 кг не трогает; предмет в руках игрока неуязвим (`PhysicsGrabber.CurrentlyGrabbed`); звуки — сам `PhysicsDraggable` | — |
| Шалости с дверями | шанс 50% каждые 12 с во время стоянки, радиус 8 м, кулдаун 60 с; 70% — медленно (4–7 с, долгий скрип), 30% — хлопок (0.15–0.3 с); только незапертые двери; `DoorInteraction.GhostToggle(duration)` — та же анимация, что у обычного клика (скрипы, защёлка, occlusion); решает по `_openFraction`, а не `_isOpen`; защёлка — 3D one-shot у двери (max 15 м), не глобальный SFX | — |
| Гейт погони       | `_huntUnlockPuzzle` → `PuzzleModeController` часов (`/Env/2ndFloor/Room 4/Puzzle/clock`): не решены — только Patrol/топот/выключатели | — |
| Пауза в точке    | 180–300 с (3–5 минут)                     | —                                |
| Кулдаун          | —                                         | глобальный 45 с, пер-точка 180 с |
| Шанс провокации  | —                                         | 0.6                              |


### Отладка

- `GhostPatrol._debugLogging` (вкл. по умолчанию) пишет в консоль `[GhostPatrol] Start at ...`,
`Arrived at ...`, `State -> Chase/Search/Patrol`, `Reached the player`,
`Caught the player — hunt re-arms in ...` и логи шалостей (двери/предметы/выключатели).
- Выдели `GhostSystem` в Scene View — `OnDrawGizmosSelected` рисует циановые линии
авта-линковки между точками и красную сферу дальности зрения.
- Тестовый визуал агента: `GhostAgent` (зелёная сфера + Point Light) — **отключён**
  (`MeshRenderer.enabled = false`, `Light.enabled = false`); призрак в игре невидим, шаги и звук работают.

### Что нужно сделать вручную

1. **Звуки:** создать дочерние `AudioSource`-объекты у waypoint'ов (3D spatial, Play On Awake = false),
  назначить клипы шагов/стуков в `_soundSteps`. Сейчас список пуст — провокации ничего не играют.
2. **Звук поимки:** настроен — `_onPlayerReached` вызывает `AudioSource.Play()` на `/GhostSystem`
  (клип `DSGNWhsh_Ghostly Whisper Whoosh High 02`). Поменять клип — в поле **Clip** этого
  AudioSource; другие варианты — `Assets/sound/ghost/` (Low 01/02/04, High 01/02).
3. **Имена комнат:** `_allowedRooms` должны совпадать с именами GameObject'ов комнат
  (так их выдаёт `RoomVisibilityManager.CurrentRoomName`). Сверь со своими `RoomController`-объектами.
4. **Позиции точек:** сейчас 6 точек на 2 этажах по расчётным координатам — проверь в Scene View,
  что каждая лежит внутри нужной комнаты (Y: 2F ≈ 10.2–10.3, 1F ≈ 4.2).
5. **Расширение:** добавить точки в пропущенные комнаты (Room 2/3/6/7, Bathroom, Nursery, подвал) —
  просто создать дочерний объект под `GhostSystem` с `GhostWaypoint`.
6. **Броски Rigidbody / другие эффекты** — вторая итерация (по договорённости: сначала только звук).

> Детект намеренно по радиусу + фильтр комнаты, не сквозь-стенной raycast: точка стоит внутри комнаты,
> а фильтр `_allowedRooms` не даёт сработать, когда игрок в соседней.

Три события с пауком (`/Assets/Models/SpiderInfestation/spider.fbx`, Animator Controller `SpiderController.controller`):


| Объект     | Скрипт            | Поведение                                      | Точка в сцене                   |
| ---------- | ----------------- | ---------------------------------------------- | ------------------------------- |
| `Spider_1` | `SpiderRunPast`   | Ползёт по своему forward (`rig                 | walk`), исчезает по` _lifetime` |
| `Spider_2` | `SpiderRunPast`   | То же, в обратную сторону (face −X), speed 3.5 | x=5.6, z=-4.0                   |
| `Spider_3` | `SpiderLeapScare` | Прыжок на игрока (`rig                         | leap`), затем убегание (`rig    |


Все три — неактивны изначально, висят под `HorrorSystem`, эффект `AppearAndStay` (паук сам себя деактивирует). Триггерные зоны `Event_SpiderPoint1/2/3` — BoxCollider 3×2×3 с Is Trigger.

`SpiderRunPast` — аналог `RatRunAway` с настраиваемым `_crawlStateName` (у паука состояния с префиксом `rig|`). `SpiderLeapScare` — три фазы: прыжок (`rig|leap`) к цели (`_leapTarget`, иначе камера игрока), поедание на месте (`rig|eating`, `_eatDuration` сек), затем ползёт к `_fleePoint` (`rig|walk`) и исчезает по прибытии (`_fleeArriveDistance`) или по таймеру `_fleeLifetime`.

**Звук и пост-процесс:**


| Объект                           | Настройка                                                                   |
| -------------------------------- | --------------------------------------------------------------------------- |
| `Event_SpiderPoint1/SoundSource` | `S_HorrorVent.aif`, 3D, volume 0.8                                          |
| `Event_SpiderPoint2/SoundSource` | `S_HorrorVent.aif`, 3D, volume 0.8                                          |
| `Event_SpiderPoint3/SoundSource` | `S_HorrorJumpScare.mp3`, 3D, volume 1.0                                     |
| `HorrorPostProcess_Spider`       | копия манекенового пост-процесса: focus на `Spider_3`, fadeIn 0.3с, BPM 120 |


Звук подключён через поле **Sound Object** каждого `HorrorEvent`. У точки 3 эффект переключён на `AppearThenDisappearAfterDelay` (2.5 с ≈ прыжок 0.6 + бег 1.5), потому что `AppearAndStay` не вызывает `Deactivate()`, а паук деактивирует себя сам — пост-процесс не погас бы. `On Activated` → `HorrorPostProcess_Spider.Activate()`, `On Deactivated` → `Deactivate()`.

&nbsp;