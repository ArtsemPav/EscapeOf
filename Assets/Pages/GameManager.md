## Escape Room — обзор проекта

Игра жанра Escape Room от первого лица. Игрок находится в закрытом
помещении, решает загадки, собирает предметы и использует их чтобы
открыть дверь в следующую комнату. Всего планируется 5 комнат.

---

## Технический стек

- **Unity 6000.3**, рендер **URP**
- **Input System** `com.unity.inputsystem 1.18.0`
- **uGUI** — UI инвентаря, пауза, кодовые замки, заметки на Canvas
- Одна сцена `SampleScene` — все комнаты существуют одновременно,
  блокировка через `Collider.enabled`, не через `SetActive`

---

## Структура сцены

```
SampleScene
├── Global Volume
├── Env
│   └── FirstRoom              # единственная готовая комната
│       ├── dors               # двери (DoorInteraction)
│       ├── Walls / flor / Celing
│       ├── props              # мебель, подбираемые предметы
│       └── NeonLamp x2        # NeonLightFlicker — аудиовизуальная синхронизация
├── Player                     # FPSController + FootstepController
│   └── CameraRoot
│       └── Main Camera        # CameraZoom
├── GameManager                # GameManager.cs — DontDestroyOnLoad
├── InputManager               # InputManager.cs — DontDestroyOnLoad
├── SaveManager                # SaveManager.cs — DontDestroyOnLoad
├── UIManager                  # UIManager.cs — ссылки на FPSController и GameConfig
├── InventorySystem            # InventorySystem.cs
├── HorrorSystem               # HorrorSystem.cs
├── InspectionSetup            # ItemInspector.cs + InspectionCamera
├── Canvas                     # все UI панели
│   ├── InventoryBackdrop
│   ├── InventoryPanel
│   ├── InspectionPanel
│   ├── MenuPanel
│   ├── CodeLockPanel
│   ├── NotePanel
│   └── InteractionHint
└── EventSystem
```

---

## GameManager

Синглтон (`DontDestroyOnLoad`). Хранит массив `RoomController[]` и управляет прогрессом комнат.

### Публичные API

| Член | Описание |
|---|---|
| `int CurrentRoomIndex` | Текущая активная комната |
| `int TotalRooms` | Всего комнат в массиве |
| `bool IsPaused` | Состояние паузы |
| `event Action<int> OnRoomChanged` | Стреляет при переходе в новую комнату |
| `event Action OnGameCompleted` | Стреляет когда игрок завершает последнюю комнату |
| `event Action<bool> OnPauseStateChanged` | Стреляет при изменении паузы |
| `OnRoomExited()` | Вызвать когда игрок вышел из комнаты |
| `SetPause(bool)` | Пауза / снятие паузы |
| `TogglePause()` | Переключение паузы |
| `UpdateCursorState()` | Пересчитать видимость курсора |

### Пауза

`SetPause(true)` → открывает `menuUI` через `UIManager.OpenPanel`, играет музыку меню.
`SetPause(false)` → закрывает панель, играет игровую музыку.

ESC обрабатывается через `InputManager.OnMenuPerformed`. Если уже открыта другая панель — игнорируется.

### Управление курсором

`UpdateCursorState()` — единственное место где меняется `Cursor.lockState`.
Разблокирует курсор если `IsPaused` или `UIManager.IsAnyPanelOpen`.

### Переход между комнатами

```
RoomDoor.Open()
  └── GameManager.Instance.OnRoomExited()
        ├── rooms[next].Unlock()
        ├── _currentRoomIndex = next
        ├── OnRoomChanged?.Invoke(next)
        └── SaveManager.Instance.Save()
```

Все комнаты активны всегда. `Lock()` / `Unlock()` управляют `Collider.enabled` на интерактивных объектах.

---

## UIManager

Синглтон. Управляет стеком открытых панелей. Хранит счётчик `_openPanelCount`.

### API

| Метод | Описание |
|---|---|
| `OpenPanel(GameObject)` | Активирует панель, скрывает игровой ввод, вызывает `UpdateCursorState` |
| `ClosePanel(GameObject)` | Деактивирует панель; восстанавливает ввод если счётчик = 0 |
| `CloseAll()` | Аварийный сброс: обнуляет счётчик, восстанавливает ввод и курсор |
| `bool IsAnyPanelOpen` | True если хотя бы одна панель открыта |

### Inspector

| Поле | Описание |
|---|---|
| `FPSController` | Ссылка на контроллер игрока |
| `GameConfig` | `ScriptableObject` с текстами и цветами UI |

---

## InputManager

Синглтон (`DontDestroyOnLoad`). Оборачивает `PlayerInputActions` (новый Input System).

### Публикуемые события

| Событие | Когда |
|---|---|
| `OnInteractPerformed` | Клавиша E |
| `OnJumpPerformed` | Пробел |
| `OnSprintToggled(bool)` | ЛШифт зажат / отпущен |
| `OnCrouchToggled(bool)` | CTRL зажат / отпущен |
| `OnMenuPerformed` | ESC |
| `OnInventoryPerformed` | Tab / I |

`SetPlayerInputEnabled(bool)` — отключает `Player` action map и обнуляет `MoveInput`/`LookInput`. Используется `UIManager` при открытии панелей.

---

## GameConfig (ScriptableObject)

Создаётся через **Create → Game → Game Config**. Назначается в `UIManager → Config`.

| Поле | Описание |
|---|---|
| `pickUpPrefix` | Приставка перед именем предмета: «Взять Ключ» |
| `codeLockSuccessText` | Текст при верном коде |
| `codeLockWrongText` | Текст при неверном коде |
| `successColor` | Цвет успеха |
| `errorColor` | Цвет ошибки |
| `normalColor` | Нейтральный цвет |

---

## RoomController

Компонент на корневом объекте каждой комнаты.

| Метод | Описание |
|---|---|
| `Unlock()` | Включает коллайдеры всех `IInteractable` объектов в комнате |
| `Lock()` | Выключает коллайдеры |
| `LocalVolume` | Ссылка на локальный `Volume` постобработки комнаты |

---

## Слои (Layers)

| Слой | Назначение |
|---|---|
| `Default` | Геометрия, пропсы без взаимодействия |
| `Interactable Layer` | Объекты, доступные через `E` или ЛКМ |
| `Inspection` | Модели в окне инспекции предметов |
| `Draggable` | Физически перетаскиваемые объекты (`PhysicsGrabber`) |
| `UI` | Canvas элементы |

---

## Что сделано

- [x] `FPSController` — движение, прыжок, приседание, бег, взаимодействие (E + ЛКМ)
- [x] `FootstepController` — шаги синхронизированы с head-bob
- [x] `InputManager` — обёртка нового Input System, DontDestroyOnLoad
- [x] `UIManager` — стек панелей, курсор, ввод
- [x] `GameManager` — комнаты, пауза, переходы, ISaveable
- [x] `RoomController` — Lock/Unlock взаимодействий
- [x] `GameConfig` — ScriptableObject с текстами/цветами
- [x] `SaveManager` — JSON + дебаунс + бэкапы
- [x] `InventorySystem` — слоты, крафт, события
- [x] `ItemInspector` — 3D-превью при подборе предметов
- [x] `InventoryUI` — Tab открытие, встроенный 3D-превью
- [x] `AudioManager` — BGM, 3D-петли, fade при паузе
- [x] `HorrorSystem` + `HorrorEvent` — хоррор-события с триггерами и сохранением
- [x] `FlashlightController` — режимы линз, `HiddenWallSign`
- [x] `DoorInteraction` + `DrawerDrag` — физическое перетаскивание
- [x] `CodeLock` + `CodeLockUI` — кодовый замок с рандомным/фиксированным кодом
- [x] `PressurePuzzle` — загадка с рычагами
- [x] `PuzzleManager` / `PuzzleElement` — слайдер-пазл (пятнашки)
- [x] `PopupMessageSystem` — всплывающие уведомления
- [x] `NoteInteraction` / `NoteUI` — заметки с текстом
- [x] `PhysicsGrabber` — захват физических объектов

## Что предстоит сделать

- [ ] Комнаты 2–5: дизайн, ассеты, загадки
- [ ] Keypad и combination загадки
- [ ] Interaction Tooltips — подсказка «Нажми E»
- [ ] Main Menu — начальный экран
