<!-- version: 2 -->

## Обзор

Лифт, соединяющий этажи локации (подвал → 1-й этаж → 2-й этаж). Кабина перемещается по оси Y между маркерами этажей, двери кабины и коридорные двери на каждом этаже открываются/закрываются синхронно. Лифт — потребитель питания (`IPowerConsumer`): без электричества кабина не двигается, но **кнопки остаются интерактивными** — нажатие проигрывается, а подсказка показывает «Нет электричества» (`GetInteractText()`), само перемещение блокируется внутри `MoveToFloor`.

Вызов с этажа — через кнопки вызова в коридорах (свитчи). Кнопка вызова светится (`ElevatorSwitchGlow`), когда её запрос принят, и гаснет при прибытии лифта на этаж кнопки.

Состояние (текущий этаж + открытые двери) сохраняется через [@ id="/Pages/Private/Save System.md" label="Save System"]. Скрипты: `Assets/Scripts/Interaction/ElevatorController.cs`, `Assets/Scripts/Interaction/ElevatorButton.cs` (namespace `Escape.Interaction`) и `Assets/Scripts/Puzzle/LoopPuzzle/ElevatorSwitchGlow.cs` (свечение кнопок вызова).

---

## Структура системы

```
Elevator (root, Collider isTrigger)      # триггер входа игрока и предметов
├── ElevatorController                   # контроллер кабины, дверей, сейв, питание
├── ElevatorCab                          # движущаяся кабина
│   ├── ElevatorButton (этаж 0)
│   ├── ElevatorButton (этаж 1)
│   ├── ElevatorButton (этаж 2)
│   ├── ElevatorAmbient                  # эмбиент-источник, создаётся в Awake автоматически
│   └── elevatorDoors_wing_A / _B        # створки дверей кабины
└── CorridorDoors (на каждом этаже)
    └── elevatorDoors_wing_A / _B        # коридорные створки этажа
```

---

## Компоненты

### `ElevatorController`

Файл: `Assets/Scripts/Interaction/ElevatorController.cs`. Размещается на корне лифта. `[RequireComponent(typeof(Collider))]` (триггер). Реализует `ISaveable`, `IPowerConsumer`. Управляет перемещением кабины, анимацией створок (лерп по локальной Z), звуком, переносом игрока и предметов.

**Публичные свойства:** `CurrentFloor`, `IsMoving`, `AreDoorsOpen`, `HasPower`, `IsBusy` (вся последовательность рейса: двери + ход + двери), `NoPowerHint` (текст для кнопок без питания). `MoveToFloor` возвращает `bool`: `true` — запрос принят, `false` — проигнорирован (нет питания / занят / неверный этаж / уже здесь с открытыми дверями).

**Inspector — Save**


| Поле      | Описание                                                                              |
| --------- | ------------------------------------------------------------------------------------- |
| `_saveId` | ID сохранения (default: `"elevator_controller"`); хранит `currentFloor` и `doorsOpen` |


**Inspector — Cab**


| Поле            | Описание                                                                                                                                      |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `_elevatorCab`  | Transform кабины — двигается только по локальной оси Y                                                                                        |
| `_cabinDoors`   | Пара створок дверей кабины (`ElevatorDoorPair`: `wingA` +Z, `wingB` −Z)                                                                       |
| `_cabinTrigger` | Триггер-коллайдер кабины; draggable-объекты внутри него блокируют закрытие дверей. Если пуст — берётся `GetComponent<Collider>()` в `Awake()` |


**Inspector — Floors**


| Поле             | Описание                                                                                |
| ---------------- | --------------------------------------------------------------------------------------- |
| `_floorMarkers`  | Массив маркеров этажей; индекс 0 = подвал, 1 = 1-й, 2 = 2-й. Кабина принимает Y маркера |
| `_floorDoors`    | Коридорные двери в том же порядке, что маркеры (`ElevatorDoorPair[]`)                   |
| `_startingFloor` | Этаж старта при новой игре (default: 0 — подвал, двери открыты)                         |


**Inspector — Door Animation**


| Поле                   | Описание                                                      |
| ---------------------- | ------------------------------------------------------------- |
| `_cabinDoorOpenOffset` | Разъезд створок кабины по локальной Z в метрах (default: 0.9) |
| `_floorDoorOpenOffset` | Разъезд коридорных створок (default: 0.9)                     |
| `_doorDuration`        | Время открытия/закрытия в сек (default: 1.5, min 0.1)         |


**Inspector — Movement**


| Поле               | Описание                                                                   |
| ------------------ | -------------------------------------------------------------------------- |
| `_moveCurve`       | Кривая плавности хода (default: `EaseInOut`) — используется и для дверей   |
| `_moveSpeed`       | Скорость кабины в ед/с; время поездки = расстояние / скорость (default: 3) |
| `_pauseBeforeMove` | Пауза между закрытием дверей и стартом в сек (default: 0.3)                |
| `_pauseBeforeOpen` | Пауза между остановкой и открытием дверей в сек (default: 0.3)             |


**Inspector — Audio**


| Поле                                    | Описание                                                   |
| --------------------------------------- | ---------------------------------------------------------- |
| `_doorSound` / `_arriveSound`           | One-shot открытия дверей / прибытия (3D, Linear rolloff)   |
| `_moveSound`                            | Зацикленный звук хода (3D луп, создаётся на время поездки) |
| `_sfxVolume`                            | Громкость one-shot (default: 1)                            |
| `_sfxMinDistance` / `_sfxMaxDistance`   | Дистанции 3D one-shot (default: 1 / 8)                     |
| `_moveVolume`                           | Громкость лупа хода (default: 0.8)                         |
| `_moveMinDistance` / `_moveMaxDistance` | Дистанции лупа (default: 1 / 10)                           |


**Inspector — Ambient Loop**


| Поле                                          | Описание                                                                                               |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `_ambientClip` / `_ambientVolume`             | Эмбиент в кабине, играет всегда (default: 0.5); при потере питания громкость → 0                       |
| `_ambientMinDistance` / `_ambientMaxDistance` | Дистанции эмбиента (default: 1 / 3)                                                                    |
| `_musicFadeDuration`                          | Время «приглушения» игровой музыки при входе/выходе (`MusicDirector.DuckActiveTrack`) (default: 2 сек) |


**Inspector — Auto-Close / Power**


| Поле              | Описание                                                                     |
| ----------------- | ---------------------------------------------------------------------------- |
| `_autoCloseDelay` | Секунды до авто-закрытия дверей, если игрока нет внутри (default: 20, min 1) |
| `_noPowerHint`    | Подсказка кнопок при отключённом питании (default: `"Нет электричества"`)    |


**Логика поездки (`MoveToFloor` → `MoveSequence`):**

```
MoveToFloor(index) → bool:
  ✗ нет питания (HasPower == false)        → false  ← блокировка без электричества
  ✗ _isBusy (поездка уже идёт, без очереди) → false
  ✗ индекс вне диапазона                    → false
  ✗ уже там и двери открыты                 → false
  → StartCoroutine(MoveSequence); → true

MoveSequence:
  0. FPSController.SetElevatorMode(true)   # гравитация игрока подавлена на всю поездку
  1. Ждать, пока HasDraggablesInside() (overlap-запрос по _cabinTrigger —
     блокируют и занесённые игроком, и лежащие с уровня/сейва предметы),
     максимум _dragWaitTimeout (10 сек) → если предмет всё ещё внутри —
     РЕЙС ОТМЕНЯЕТСЯ: лог «Ride cancelled», SetElevatorMode(false),
     _isBusy = false, двери не трогаются
  2. Закрыть двери текущего этажа (кабина + коридор одновременно)
  3. Пауза _pauseBeforeMove
  4. MoveCabin: игрок парентится к кабине, лерп Y по _moveCurve,
     луп звука хода, снап точно в Y маркера → unparent
  5. Пауза _pauseBeforeOpen
  6. Открыть двери этажа назначения
  7. SetElevatorMode(false)
  8. Игрока нет внутри → таймер авто-закрытия
```

**Триггер и питание:**

- `OnTriggerEnter/Exit(FPSController)`: отмена/запуск авто-закрытия, `MusicDirector.DuckActiveTrack` (музыка приглушается в лифте).
- `HasDraggablesInside()`: физический `Physics.OverlapBoxNonAlloc` по объёму `_cabinTrigger` (для `BoxCollider` — точный ориентированный бокс; иначе — мировые bounds), ищет `PhysicsDraggable` в родителях найденных коллайдеров. Предмет в кабине (любым способом) блокирует закрытие дверей; после 10 с ожидания рейс отменяется.
- `OnPowerStateChanged(isPowered)`: `HasPower = isPowered`; эмбиент заглушается/возвращается. Регистрация в `LightingSystem.RegisterConsumer` — состояние приходит сразу при регистрации.
- Авто-закрытие: через `_autoCloseDelay`, не срабатывает при игроке внутри или идущей поездке.

### `ElevatorButton`

Файл: `Assets/Scripts/Interaction/ElevatorButton.cs`. Кнопка внутри кабины **и на этажах** (свитчи вызова в `PRF_CorridorBasement` / `PRF_Corridor1stFloor` / `PRF_Corridor2ndFloor`), `IInteractable`. `[RequireComponent(typeof(Collider))]`. По клику вызывает `ElevatorController.MoveToFloor` со своим индексом этажа и запоминает результат в `LastCallAccepted`.

**Inspector**


| Поле          | Описание                                                                                                                             |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `_floorIndex` | Индекс этажа: 0 = подвал, 1 = 1-й этаж, 2 = 2-й этаж                                                                                 |
| `_controller` | `ElevatorController`; если пусто — ищется на родителе в `Awake()` (для свитчей в комнатах проставляется ссылкой на контроллер сцены) |
| `_hintText`   | Подсказка при наличии питания (default: `"Нажать кнопку"`)                                                                           |


**Публичные свойства:** `FloorIndex` (этаж кнопки), `LastCallAccepted` (результат последнего `MoveToFloor` — читается глоу-скриптом).

**Поведение при отсутствии питания**

- `CanInteract()` **не блокирует** взаимодействие — кнопка остаётся интерактивной без электричества.
- `Interact()` всегда вызывает `_controller.MoveToFloor(_floorIndex)` — нажатие «происходит», но `MoveToFloor` сам выходит по `!HasPower`, поэтому кабина не двигается (анимация нажатия со стороны `FPSController` проигрывается — игрок понимает, что кнопка есть, но не работает).
- `GetInteractText()`: если `_controller.HasPower == false` — возвращает `_controller.NoPowerHint` («Нет электричества»), иначе `_hintText`.
- `IsPickable() => false`, `UseLMBClick => true`, `GetCrosshairMode() => CrosshairMode.Hand`.

### `ElevatorSwitchGlow`

Файл: `Assets/Scripts/Puzzle/LoopPuzzle/ElevatorSwitchGlow.cs`. Свечение кнопки вызова на этаже. Вешается на объект свитча (рядом с `ElevatorButton` + `ButtonPressAnimation`) — настроен на всех трёх этажах (`props/elevator_switch (1)` в трёх коридорных префабах).

**Inspector**


| Поле                                   | Описание                                                              |
| -------------------------------------- | --------------------------------------------------------------------- |
| `_glowRenderer`                        | `Renderer` светящейся части (ребёнок `elevator_switch.001`)           |
| `_callButton`                          | Соседний `ElevatorButton` — его `FloorIndex` задаёт этаж свитча       |
| `_controller`                          | `ElevatorController`; если пуст — `FindFirstObjectByType` в `Awake()` |
| `_button`                              | `ButtonPressAnimation`; на её `OnPressed` включается свечение         |
| `_glowOffMaterial` / `_glowOnMaterial` | Материалы без/с эмиссией (`elevatorMat` / `elevatorMat_emit`)         |
| `_busyFlashFallbackDuration`           | Длительность вспышки, если `_button` не задан (default: 0.2 сек)      |


**Логика (`OnPressed` → `GlowRoutine`):**

- Без питания свечение не включается (`HasPower == false` → выход).
- `requestIgnored = !_callButton.LastCallAccepted` — запрос проигнорирован контроллером (лифт занят или уже стоит на этаже с открытыми дверями) → короткая вспышка на длительность анимации нажатия (`PressDuration + ReturnDuration` ≈ 0.18 сек), затем гаснет.
- Запрос принят → горит, пока рейс идёт (`IsBusy == true`) и лифт не прибыл на этаж кнопки (`CurrentFloor == FloorIndex && AreDoorsOpen`); гаснет при открытии дверей, отмене рейса (`IsBusy → false` без прибытия) или по страховочному таймауту 60 сек.
- Переключение — подменой `Renderer.sharedMaterial` между `_glowOffMaterial` и `_glowOnMaterial` (материал как ассет не изменяется).
- `Start()` выключает свечение (не в `Awake` — в prefab-контексте обращение к рендереру в `Awake`/`OnEnable` кидает `InvalidOperationException`).

---

## Взаимодействие с другими системами


| Система                                                                            | Связь                                                                                                                |
| ---------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| [@ id="/Pages/Private/Lighting System.md" label="Lighting System"]         | `IPowerConsumer`: `HasPower` управляет движением и эмбиентом; без питания кнопки показывают «Нет электричества»      |
| [@ id="/Pages/Private/Save System.md" label="Save System"]             | `ISaveable`: сохраняет этаж и состояние дверей; при загрузке — мгновенный снап (`SnapToFloor` + `SetDoorsImmediate`) |
| `FPSController`                                                                    | `SetElevatorMode(true/false)` подавляет гравитацию игрока; игрок парентится к кабине на время хода                   |
| `AudioManager` / `MusicDirector`                                                   | 3D one-shot и лупы регистрируются для `MuteBackground`; музыка приглушается при входе в кабину                       |
| [@ id="/Pages/Private/Drag Interaction System.md" label="Drag Interaction System"] | `PhysicsDraggable` внутри `_cabinTrigger` блокирует закрытие дверей; после 10 с рейс отменяется                      |


## Известные особенности

- `MoveSequence` выполняется без очереди: вызов во время активного рейса просто игнорируется (`MoveToFloor → false`).
- Двери — кинематический лерп: если предмет «прорвался» в зону закрытия, створки пройдут сквозь него.
- При отмене рейса по таймауту двери остаются в исходном состоянии, гравитация игрока возвращается, `_isBusy` сбрасывается — повторный вызов возможен сразу.
- Свечение свитча — подмена `sharedMaterial`; эмиссию `elevatorMat_emit` смотреть при выключенном пост-процессинге не обязательно — она видна и в базовом виде.
