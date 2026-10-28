## Обзор

Лифт, соединяющий этажи локации (подвал → 1-й этаж → 2-й этаж). Кабина перемещается по оси Y между маркерами этажей, двери кабины и коридорные двери на каждом этаже открываются/закрываются синхронно. Лифт — потребитель питания (`IPowerConsumer`): без электричества кабина не двигается, но **кнопки остаются интерактивными** — нажатие проигрывается, а подсказка показывает «Нет электричества» (`GetInteractText()`), само перемещение блокируется внутри `MoveToFloor`.

Состояние (текущий этаж + открытые двери) сохраняется через Save System. Вся система — два скрипта: `Assets/Scripts/Interaction/ElevatorController.cs` и `Assets/Scripts/Interaction/ElevatorButton.cs` (namespace `Escape.Interaction`).

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

**Публичные свойства:** `CurrentFloor`, `IsMoving`, `AreDoorsOpen`, `HasPower`, `NoPowerHint` (текст для кнопок без питания).

**Inspector — Save**

| Поле      | Описание                                        |
| --------- | ----------------------------------------------- |
| `_saveId` | ID сохранения (default: `"elevator_controller"`); хранит `currentFloor` и `doorsOpen` |

**Inspector — Cab**

| Поле            | Описание                                                       |
| --------------- | -------------------------------------------------------------- |
| `_elevatorCab`  | Transform кабины — двигается только по локальной оси Y          |
| `_cabinDoors`   | Пара створок дверей кабины (`ElevatorDoorPair`: `wingA` +Z, `wingB` −Z) |

**Inspector — Floors**

| Поле             | Описание                                                                       |
| ---------------- | ------------------------------------------------------------------------------ |
| `_floorMarkers`  | Массив маркеров этажей; индекс 0 = подвал, 1 = 1-й, 2 = 2-й. Кабина принимает Y маркера |
| `_floorDoors`    | Коридорные двери в том же порядке, что маркеры (`ElevatorDoorPair[]`)           |
| `_startingFloor` | Этаж старта при новой игре (default: 0 — подвал, двери открыты)                 |

**Inspector — Door Animation**

| Поле                   | Описание                                                     |
| ---------------------- | ------------------------------------------------------------ |
| `_cabinDoorOpenOffset` | Разъезд створок кабины по локальной Z в метрах (default: 0.9) |
| `_floorDoorOpenOffset` | Разъезд коридорных створок (default: 0.9)                     |
| `_doorDuration`        | Время открытия/закрытия в сек (default: 1.5, min 0.1)         |

**Inspector — Movement**

| Поле               | Описание                                                                        |
| ------------------ | ------------------------------------------------------------------------------- |
| `_moveCurve`       | Кривая плавности хода (default: `EaseInOut`) — используется и для дверей          |
| `_moveSpeed`       | Скорость кабины в ед/с; время поездки = расстояние / скорость (default: 3)       |
| `_pauseBeforeMove` | Пауза между закрытием дверей и стартом в сек (default: 0.3)                      |
| `_pauseBeforeOpen` | Пауза между остановкой и открытием дверей в сек (default: 0.3)                   |

**Inspector — Audio**

| Поле | Описание |
| ---- | -------- |
| `_doorSound` / `_arriveSound` | One-shot открытия дверей / прибытия (3D, Linear rolloff) |
| `_moveSound` | Зацикленный звук хода (3D луп, создаётся на время поездки) |
| `_sfxVolume` | Громкость one-shot (default: 1) |
| `_sfxMinDistance` / `_sfxMaxDistance` | Дистанции 3D one-shot (default: 1 / 8) |
| `_moveVolume` | Громкость лупа хода (default: 0.8) |
| `_moveMinDistance` / `_moveMaxDistance` | Дистанции лупа (default: 1 / 10) |

**Inspector — Ambient Loop**

| Поле | Описание |
| ---- | -------- |
| `_ambientClip` / `_ambientVolume` | Эмбиент в кабине, играет всегда (default: 0.5); при потере питания громкость → 0 |
| `_ambientMinDistance` / `_ambientMaxDistance` | Дистанции эмбиента (default: 1 / 3) |
| `_musicFadeDuration` | Время «приглушения» игровой музыки при входе/выходе (`MusicDirector.DuckActiveTrack`) (default: 2 сек) |

**Inspector — Auto-Close / Power**

| Поле | Описание |
| ---- | -------- |
| `_autoCloseDelay` | Секунды до авто-закрытия дверей, если игрока нет внутри (default: 20, min 1) |
| `_noPowerHint` | Подсказка кнопок при отключённом питании (default: `"Нет электричества"`) |

**Логика поездки (`MoveToFloor` → `MoveSequence`):**

```
MoveToFloor(index):
  ✗ нет питания (HasPower == false)        → выход  ← блокировка без электричества
  ✗ _isBusy (поездка уже идёт, без очереди) → выход
  ✗ индекс вне диапазона                    → выход
  ✗ уже там и двери открыты                 → выход
  → StartCoroutine(MoveSequence)

MoveSequence:
  0. FPSController.SetElevatorMode(true)   # гравитация игрока подавлена на всю поездку
  1. Ждать, пока внутри PhysicsDraggable   # предметы не дают дверям закрыться
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
- `OnTriggerEnter/Exit(PhysicsDraggable)`: учёт предметов внутри — двери не закроются, пока предмет не вынесен.
- `OnPowerStateChanged(isPowered)`: `HasPower = isPowered`; эмбиент заглушается/возвращается. Регистрация в `LightingSystem.RegisterConsumer` — состояние приходит сразу при регистрации.
- Авто-закрытие: через `_autoCloseDelay`, откладывается, если внутри предметы; не срабатывает при игроке внутри или идущей поездке.

### `ElevatorButton`

Файл: `Assets/Scripts/Interaction/ElevatorButton.cs`. Кнопка внутри кабины, `IInteractable`. `[RequireComponent(typeof(Collider))]`. По клику вызывает `ElevatorController.MoveToFloor` со своим индексом этажа.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_floorIndex` | Индекс этажа: 0 = подвал, 1 = 1-й этаж, 2 = 2-й этаж |
| `_controller` | `ElevatorController`; если пусто — ищется на родителе в `Awake()` |
| `_hintText` | Подсказка при наличии питания (default: `"Нажать кнопку"`) |

**Поведение при отсутствии питания**

- `CanInteract()` **не блокирует** взаимодействие — кнопка остаётся интерактивной без электричества.
- `Interact()` всегда вызывает `_controller.MoveToFloor(_floorIndex)` — нажатие «происходит», но `MoveToFloor` сам выходит по `!HasPower`, поэтому кабина не двигается (анимация нажатия со стороны `FPSController` проигрывается — игрок понимает, что кнопка есть, но не работает).
- `GetInteractText()`: если `_controller.HasPower == false` — возвращает `_controller.NoPowerHint` («Нет электричества»), иначе `_hintText`.
- `IsPickable() => false`, `UseLMBClick => true`, `GetCrosshairMode() => CrosshairMode.Hand`.

---

## Взаимодействие с другими системами

| Система | Связь |
| ------- | ----- |
| Lighting System | `IPowerConsumer`: `HasPower` управляет движением и эмбиентом; без питания кнопки показывают «Нет электричества» |
| Save System | `ISaveable`: сохраняет этаж и состояние дверей; при загрузке — мгновенный снап (`SnapToFloor` + `SetDoorsImmediate`) |
| `FPSController` | `SetElevatorMode(true/false)` подавляет гравитацию игрока; игрок парентится к кабине на время хода |
| `AudioManager` / `MusicDirector` | 3D one-shot и лупы регистрируются для `MuteBackground`; музыка приглушается при входе в кабину |
