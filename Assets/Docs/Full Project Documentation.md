# Escape — Полная документация проекта

> Этот документ описывает **все системы** проекта: как они устроены, как связаны между собой и как добавлять новый контент. Написано так, чтобы было понятно даже если ты только что открыл проект.

---

## Содержание

1. [Общая архитектура](#общая-архитектура)
2. [Как добавить новый предмет](#как-добавить-новый-предмет)
3. [Как добавить новую комнату](#как-добавить-новую-комнату)
4. [Как добавить хоррор-событие](#как-добавить-хоррор-событие)
5. [Ядро — UIManager](#ядро--uimanager)
6. [Ядро — GameManager](#ядро--gamemanager)
7. [Ядро — GameConfig](#ядро--gameconfig)
8. [Игрок — FPSController](#игрок--fpscontroller)
9. [Игрок — PhysicsGrabber](#игрок--physicsgrabber)
10. [Инвентарь](#инвентарь)
11. [Инспекция предметов — ItemInspector](#инспекция-предметов--iteminspector)
12. [Интерфейс инвентаря](#интерфейс-инвентаря)
13. [Взаимодействие — IInteractable](#взаимодействие--iinteractable)
14. [Двери — DoorInteraction](#двери--doorinteraction)
15. [Ящики — DrawerDrag](#ящики--drawerdrag)
16. [Замки — CodeLock](#замки--codelock)
17. [Записки — NoteInteraction](#записки--noteinteraction)
18. [Фонарик — FlashlightController](#фонарик--flashlightcontroller)
19. [Хоррор-система](#хоррор-система)
20. [UI-системы](#ui-системы)
21. [Комнаты — RoomController](#комнаты--roomcontroller)
22. [Структура файлов](#структура-файлов)

---

## Общая архитектура

Проект построен на **одностороннем потоке данных**: каждая система знает только о тех, что стоят ниже по иерархии. Никто напрямую не общается «вбок».

```
GameManager          — прогресс по комнатам
UIManager            — все UI-панели, курсор, блокировка ввода
  └─ FPSController   — движение, взгляд, взаимодействие
       └─ PhysicsGrabber  — физическое перетаскивание объектов

InventorySystem      — данные инвентаря (события → всем подписчикам)
  ├─ InventoryUI     — отображение слотов
  ├─ FlashlightController — реагирует на наличие батареек
  └─ HorrorSystem    — реагирует на подбор предметов

HorrorSystem         — координирует хоррор-события
  └─ HorrorEvent     — один момент ужаса (N штук в сцене)
```

**Все ключевые системы — Singleton.** Доступ: `SystemName.Instance`.

---

## Как добавить новый предмет

> Самая частая задача. Шаги описаны максимально подробно.

### Шаг 1 — Создать данные предмета

Правая кнопка в Project → **Create → Inventory → Item Data**.

Откроется ScriptableObject. Заполни поля:

| Поле | Что указать |
|---|---|
| `Item Name` | Название (например, "Ключ от подвала") |
| `Description` | Описание для тултипа в инвентаре |
| `Icon` | Спрайт иконки для слота инвентаря |
| `Inspection Prefab` | Prefab 3D-модели для просмотра (можно оставить пустым) |
| `Consume On Use` | Снять галочку если предмет должен оставаться в инвентаре после использования |

### Шаг 2 — Создать 3D-объект в сцене

1. Добавь GameObject с мешем предмета в сцену
2. Добавь компонент `Collider` (любой)
3. Добавь компонент `PickableItem`
4. В поле `Item Data` укажи ScriptableObject из шага 1
5. Выставь слой объекта: **Interactable Layer**

### Шаг 3 — Prefab для инспекции (опционально)

Если хочешь чтобы при подборе/ПКМ показывался 3D-вид предмета:

1. Создай отдельный Prefab с моделью предмета
2. Укажи его в поле `Inspection Prefab` у `ItemData`
3. Prefab будет автоматически помещён на слой `Inspection` — основная камера его не видит

### Шаг 4 — Рецепт крафта (опционально)

Если предмет получается из двух других:

1. Правая кнопка → **Create → Inventory → Crafting Recipe**
2. Укажи два ингредиента и результат
3. Добавь ассет в массив `Recipes` компонента `InventorySystem` в сцене

---

## Как добавить новую комнату

1. Создай GameObject для комнаты, добавь компонент `RoomController`
2. Разложи внутри неё интерактивные объекты (двери, замки, предметы)
3. В Inspector компонента `GameManager` добавь новый `RoomController` в массив `rooms`
4. Комнаты в массиве идут по порядку: Room[0] — первая, Room[1] — вторая и т.д.
5. Room[0] автоматически разблокируется при старте, остальные заблокированы

Переход между комнатами: `GameManager.Instance.OnRoomExited()` — вызывается когда игрок прошёл дверь последней комнаты.

---

## Как добавить хоррор-событие

1. Создай пустой GameObject в сцене, добавь компонент `HorrorEvent`
2. Заполни `Event Id` — уникальная строка
3. Выбери **Trigger Type**:
   - `OnItemPickup` — игрок подбирает предмет (укажи `Required Item`)
   - `OnRoomEnter` — игрок входит в комнату (укажи `Required Room Index`)
   - `OnManual` — вызов кодом: `HorrorSystem.Instance.Trigger("id")`
   - `OnPlayerEnterZone` — игрок входит в коллайдер-триггер на этом объекте
4. Выбери **Effect Type** (что происходит):
   - `AppearAndStay` — объект появляется и остаётся
   - `AppearThenDisappearOnLookAway` — появляется, исчезает когда игрок посмотрел и отвернулся
   - `AppearThenDisappearAfterDelay` — появляется и исчезает через `Disappear Delay` секунд
   - `DisappearOnTrigger` — объект уже виден, исчезает при событии
5. В поле `Target` укажи GameObject который нужно показать/скрыть
6. В `On Activated` / `On Deactivated` подключи звуки или анимации

---

## Ядро — UIManager

**Файл:** `Assets/Scripts/Core/UIManager.cs`

Центральный менеджер всех UI-панелей. Все панели открываются и закрываются **только через него** — это гарантирует правильное состояние курсора и ввода в любой момент.

### Почему это важно

Когда открыта любая панель (инвентарь, записка, замок, инспекция) — игрок не должен двигаться и крутить камеру. `UIManager` автоматически это обеспечивает через счётчик `_openPanelCount`. Курсор разблокируется только когда **все** панели закрыты.

### Публичные методы

```csharp
// Открыть панель. cursorMode — как показывать курсор (по умолчанию свободный)
UIManager.Instance.OpenPanel(GameObject panel, CursorLockMode cursorMode = None);

// Закрыть панель. Ввод и курсор вернутся только когда все панели закрыты
UIManager.Instance.ClosePanel(GameObject panel);

// Принудительно закрыть всё (например при перезагрузке сцены)
UIManager.Instance.CloseAll();
```

### Свойства

```csharp
bool IsAnyPanelOpen  // true если хоть одна панель открыта
GameConfig Config    // доступ к GameConfig
FPSController PlayerController
```

### Настройка в Inspector

| Поле | Что назначить |
|---|---|
| `Player Controller` | GameObject игрока (с компонентом FPSController) |
| `Config` | Ассет GameConfig (Create → Game → Game Config) |

---

## Ядро — GameManager

**Файл:** `Assets/Scripts/Core/GameManager.cs`

Отслеживает в какой комнате находится игрок и разблокирует следующую при выходе.

### Как работает

- При старте Room[0] разблокируется, все остальные заблокированы
- Блокировка = отключение коллайдеров на всех `IInteractable` объектах внутри комнаты (геометрия остаётся видимой)
- `OnRoomExited()` вызывается дверью последней комнаты → разблокируется следующая

### События

```csharp
event Action<int> OnRoomChanged;   // передаёт индекс новой комнаты
event Action OnGameCompleted;       // все комнаты пройдены
```

HorrorSystem автоматически подписывается на `OnRoomChanged`.

### Настройка в Inspector

Массив `Rooms` — перетащи `RoomController`-объекты в нужном порядке.

---

## Ядро — GameConfig

**Файл:** `Assets/Scripts/Core/GameConfig.cs`

ScriptableObject с текстами и цветами используемыми по всему проекту. Менять в одном месте — работает везде.

Создать: **правая кнопка → Create → Game → Game Config**.

| Поле | Назначение |
|---|---|
| `Pick Up Prefix` | Слово перед названием предмета: "Взять Ключ" |
| `Code Lock Success Text` | Текст при правильном коде: "Доступ открыт" |
| `Code Lock Wrong Text` | Текст при неверном коде: "Неверный код" |
| `Success Color` | Зелёный — для успешных сообщений |
| `Error Color` | Красный — для ошибок |
| `Normal Color` | Белый — стандартный |

---

## Игрок — FPSController

**Файл:** `Assets/Scripts/Player/FPSController.cs`

Управляет всем что связано с персонажем: движение, прыжок, приседание, взгляд, хэд-боб, обнаружение объектов и взаимодействие с ними.

### Движение

| Параметр | По умолчанию | Описание |
|---|---|---|
| `Walk Speed` | 5 | Скорость ходьбы (м/с) |
| `Run Speed` | 8 | Скорость бега |
| `Crouch Speed` | 2 | Скорость в приседе |
| `Acceleration Time` | 0.12 | Время разгона (сек) |
| `Deceleration Time` | 0.07 | Время торможения — быстрее для чёткости |
| `Jump Force` | 7 | Начальная вертикальная скорость при прыжке |
| `Gravity` | -12 | Сила притяжения |

### Взгляд и камера

| Параметр | По умолчанию | Описание |
|---|---|---|
| `Mouse Sensitivity` | 0.2 | Чувствительность мыши |
| `Pitch Smooth Time` | 0.03 | Сглаживание вертикального взгляда |
| `Strafe Tilt Angle` | 2 | Наклон камеры при стрейфе (градусы) |
| `Drag Camera Sensitivity Multiplier` | 0.25 | Множитель чувствительности во время перетаскивания |

### Приседание

Зажать — присесть, отпустить — встать. Если над головой препятствие — персонаж ждёт пока место освободится.

### Хэд-боб

Плавное покачивание камеры при ходьбе. Амплитуда масштабируется от скорости. При остановке плавно возвращается в нейтраль.

| Параметр | Описание |
|---|---|
| `Bob Frequency` | Частота покачивания |
| `Bob Amplitude Y/X` | Амплитуда вертикальная/горизонтальная |
| `Bob Return Speed` | Скорость возврата в нейтраль |

### Взаимодействие

Каждый кадр кастуется луч от камеры вперёд на расстояние `Interact Distance`. Если луч попадает в объект на слое `Interactable Layer`, у которого есть компонент `IInteractable` — показывается подсказка `InteractionUI`.

Приоритет: `IDraggable` > `IInteractable` (чтобы ящики перекрывали старые компоненты дверей).

### Публичные методы

```csharp
// Включить/выключить весь ввод игрока. Вызывается UIManager при открытии панелей.
SetPlayerInputEnabled(bool enabled);

// Замедлить персонажа. 1.0 = обычная скорость. Используется PhysicsGrabber.
SetSpeedMultiplier(float multiplier);

// Включить режим захвата физического объекта (снижает чувствительность камеры).
SetPhysicsGrabActive(bool active);

// Заморозить XZ-позицию на duration секунд (защита от выталкивания анимациями).
LockPositionFor(float duration);

// Сбросить кеш обнаружения — следующий кадр заново определит объект под прицелом.
ResetInteractionCache();
```

### Управление игрока

| Клавиша | Действие |
|---|---|
| WASD | Движение |
| Мышь | Взгляд |
| Shift | Бег |
| Ctrl / C | Присесть |
| Пробел | Прыжок |
| E | Взаимодействие |
| ЛКМ | Взаимодействие (записки, предметы) / Перетаскивание (двери, ящики) |
| F | Фонарик |
| I / Tab | Инвентарь |
| Escape | Меню паузы |

---

## Игрок — PhysicsGrabber

**Файл:** `Assets/Scripts/Player/PhysicsGrabber.cs`

Позволяет игроку тянуть физические объекты в мире — удерживая ЛКМ. Объект притягивается к точке перед камерой через пружину.

### Как настроить объект для перетаскивания

1. Добавь компонент `PhysicsDraggable` — маркер-компонент с настройками
2. Добавь компонент `Rigidbody` — масса влияет на ощущение веса
3. Назначь слой **Draggable**
4. Сними флаг `Static`
5. Для `MeshCollider` — включи **Convex**

### Параметры PhysicsDraggable

| Поле | Описание |
|---|---|
| `Drag Hint` | Текст подсказки при наведении |
| `Prevent Tipping` | Запрещает объекту опрокидываться (блокирует X/Z вращение) |

### Параметры PhysicsGrabber

| Поле | По умолчанию | Описание |
|---|---|---|
| `Grab Distance` | 3 | Дистанция захвата (м) |
| `Detection Radius` | 0.08 | Радиус SphereCast — больше = стабильнее подсветка |
| `Hold Distance` | 2 | Расстояние перед камерой куда тянется объект |
| `Spring Strength` | 200 | Сила пружины (больше = быстрее) |
| `Spring Damping` | 20 | Затухание (больше = меньше раскачки) |
| `Max Velocity` | 8 | Ограничение скорости объекта (м/с) |
| `Grab Linear Drag` | 5 | Drag во время удержания |
| `Reference Heavy Mass` | 20 | Масса при которой игрок движется с min скоростью |
| `Min Speed Multiplier` | 0.4 | Минимум скорости игрока при тяжёлом объекте |
| `Acceptable Gap` | 0.6 | Зазор до hold point без дополнительного замедления |
| `Max Gap` | 1.5 | Зазор при котором игрок полностью стоит |

### Ограничения

- Бег отменяет захват
- Захват блокируется при открытом инвентаре/меню

---

## Инвентарь

### InventorySystem

**Файл:** `Assets/Scripts/Inventory/InventorySystem.cs`

Singleton. Хранит массив `ItemData[]` фиксированного размера. Индекс в массиве = позиция в инвентаре.

### Публичные методы

```csharp
bool AddItem(ItemData item)              // добавить предмет в первый свободный слот
bool RemoveItem(ItemData item)           // удалить первый найденный экземпляр
bool HasItem(ItemData item)              // есть ли предмет в инвентаре
ItemData GetItemAt(int slotIndex)        // получить предмет в конкретном слоте
void SwapSlots(int a, int b)             // поменять местами два слота
bool TryCombine(int a, int b, out ItemData result)  // попробовать скрафтить
```

### Событие

```csharp
event Action OnInventoryChanged;
```

Стреляет после любого изменения. Подписчики: `InventoryUI`, `FlashlightController`, `HorrorSystem`.

### Настройка в Inspector

| Поле | Описание |
|---|---|
| `Slot Count` | Сколько слотов в инвентаре |
| `Recipes` | Массив ассетов CraftingRecipe |

### ItemData (ScriptableObject)

Создать: **правая кнопка → Create → Inventory → Item Data**.

| Поле | Описание |
|---|---|
| `Item Name` | Название предмета |
| `Description` | Описание (показывается в тултипе) |
| `Icon` | Иконка для слота инвентаря |
| `Inspection Prefab` | Prefab для 3D-просмотра |
| `Consume On Use` | Удалить предмет из инвентаря после использования. По умолчанию `true` |

Флаг `Consume On Use` проверяется в `DoorInteraction.Interact()` и `CodeLock.TryUnlock()` — если выключен, предмет остаётся в инвентаре и может быть использован повторно (карта доступа, мастер-ключ).

### CraftingRecipe (ScriptableObject)

Создать: **правая кнопка → Create → Inventory → Crafting Recipe**.

Порядок ингредиентов не важен. Результат помещается в целевой слот.

---

## Инспекция предметов — ItemInspector

**Файл:** `Assets/Scripts/Inventory/ItemInspector.cs`

Singleton. Показывает 3D-модель предмета через отдельную камеру → `RenderTexture` → `RawImage` в UI.

### Два режима

**Режим подбора** — открывается когда игрок нажимает E/ЛКМ на предмете в мире:
- Показывает название и описание
- Закрытие = предмет в инвентарь + мировой объект уничтожается

**Режим превью** — открывается по ПКМ на слоте инвентаря:
- Название и описание скрыты
- Закрытие = только закрывается панель, инвентарь не меняется
- Закрывается: ПКМ / E / Escape

### Публичные методы

```csharp
// Режим подбора. Вызывается из PickableItem.
BeginInspection(ItemData item, GameObject worldObject);

// Режим превью. Вызывается по ПКМ из InventorySlot.
BeginPreview(ItemData item);

// Подобрать и закрыть. Привязан к кнопке Take в UI.
ConfirmPickup();

// Закрыть превью без изменения инвентаря. Вызывается из InventoryUI.CloseInventory.
CancelPreviewIfActive();
```

### Параметры Inspector

| Поле | По умолчанию | Описание |
|---|---|---|
| `Inspection Camera` | — | Orthographic камера для рендера модели |
| `Framing Multiplier` | 2.2 | Чем больше — тем меньше модель в кадре |
| `Rotation Speed` | 180 | Скорость ручного вращения (градусы/сек) |
| `Initial Rotation` | (15, -35, 0) | Начальный угол при открытии |
| `Idle Spin Duration` | 1.8 | Длительность вступительного spin (сек) |
| `Idle Spin Speed` | 80 | Скорость idle spin (градусы/сек) |

### Как работает технически

1. Prefab инстанциируется в точке Y = -1000 (вне видимости основной камеры)
2. По bounds всех Renderer вычисляется геометрический центр модели
3. Создаётся `InspectionPivot` в этом центре — вращение без смещения модели
4. Камера настраивается Orthographic, размер = `maxSize * framingMultiplier * 0.5`
5. Idle spin плавно затухает через `Mathf.Cos`
6. При закрытии Pivot уничтожается вместе с дочерним объектом

### Настройка сцены

```
InspectionSetup         (компонент ItemInspector)
└── InspectionCamera    (Orthographic, слой Inspection, → RenderTexture)

Canvas/InspectionPanel
├── HintText            (подсказки управления)
├── PreviewImage        (RawImage ← RenderTexture)
├── InfoPanel
│   ├── ItemNameText    (скрыт в режиме превью)
│   └── DescriptionText (скрыт в режиме превью)
├── TakeButton          (→ ConfirmPickup)
└── CancelButton        (→ CancelInspection)
```

---

## Интерфейс инвентаря

### InventoryUI

**Файл:** `Assets/Scripts/Inventory/UI/InventoryUI.cs`

Управляет панелью инвентаря. Слоты создаются один раз в `Start`, затем только обновляются.

Открывается: **I** или **Tab**. При открытии — курсор свободен, ввод игрока отключён.
При закрытии: сначала гасится активный 3D-превью (`CancelPreviewIfActive`), затем закрывается панель.

### InventorySlot

**Файл:** `Assets/Scripts/Inventory/UI/InventorySlot.cs`

Один слот в сетке. Иконка скрывается через `Image.enabled = false` — GameObject остаётся активным чтобы drag-and-drop работал всегда.

**ЛКМ + тащить** → переместить предмет или скрафтить (если оба слота заняты).
**ПКМ** → открыть 3D-превью через `ItemInspector.BeginPreview`.

### DraggableItem

**Файл:** `Assets/Scripts/Inventory/UI/DraggableItem.cs`

Компонент на дочернем объекте `Icon`. При начале drag — иконка перепривязывается к корневому Canvas чтобы рисоваться поверх всего.

### ItemTooltip

**Файл:** `Assets/Scripts/Inventory/UI/ItemTooltip.cs`

Тултип с названием и описанием. Появляется при наведении на заполненный слот, исчезает при уходе курсора.

### InventoryHints

**Файл:** `Assets/Scripts/Inventory/UI/InventoryHints.cs`

Панель подсказок внизу инвентаря. Строит текст из массива подсказок в Inspector.

```
InventoryPanel
├── SlotsContainer      (сетка слотов)
└── HintsBar            (компонент InventoryHints)
    └── HintsText       (TextMeshProUGUI)
```

| Параметр | Описание |
|---|---|
| `Hints Per Row` | Сколько подсказок на строку (по умолчанию 2) |
| `Hints` | Массив пар: клавиша + действие |

---

## Взаимодействие — IInteractable

**Файл:** `Assets/Scripts/Interaction/IInteractable.cs`

Интерфейс для всех интерактивных объектов. `FPSController` кастует луч и вызывает методы у любого объекта реализующего этот интерфейс.

```csharp
void Interact();                  // вызывается при нажатии E
string GetInteractText();         // текст подсказки ("Открыть дверь")
bool IsPickable();                // нужна ли иконка "рука" в прицеле
bool UseLMBClick { get; }         // true = ЛКМ тоже вызывает Interact (записки, предметы)
CrosshairMode GetCrosshairMode(); // какую иконку прицела показывать
string GetBlockedHint();          // объяснение почему взаимодействие заблокировано
```

### IDraggable

Расширение `IInteractable` для объектов управляемых мышью (двери, ящики).

```csharp
void OnDragStart(Vector3 hitPoint); // ЛКМ нажата, hitPoint = точка попадания луча
void OnDrag(Vector2 mouseDelta);    // каждый кадр пока ЛКМ удерживается
void OnDragEnd();                   // ЛКМ отпущена
```

### CrosshairMode

| Значение | Иконка | Когда |
|---|---|---|
| `Default` | Точка | Ничего рядом |
| `Hand` | Рука | Можно подобрать |
| `Grab` | Захват | Можно перетащить |
| `Read` | Книга | Можно прочитать |
| `Locked` | Замок закрытый | Заперто, ключа нет |
| `Unlocked` | Замок открытый | Заперто, но ключ есть |

---

## Двери — DoorInteraction

**Файл:** `Assets/Scripts/Interaction/DoorInteraction.cs`

Физическая дверь. Игрок удерживает ЛКМ и тянет мышью — дверь следует за курсором как в Phasmophobia. После отпускания — инерция.

### Настройка

1. Создай иерархию: `DoorFrame → DoorPivot → DoorMesh`
2. Добавь `DoorInteraction` на объект с коллайдером (обычно `DoorMesh`)
3. В поле `Pivot` укажи `DoorPivot` — он будет вращаться
4. Назначь слой **Interactable Layer**

### Параметры

| Поле | Описание |
|---|---|
| `Is Locked` | Дверь заперта при старте |
| `Required Key` | ItemData ключа (необязательно) |
| `Max Open Angle` | Угол полного открытия (отрицательный = другая сторона) |
| `Drag Sensitivity` | Чувствительность перетаскивания |
| `Friction` | Трение — как быстро останавливается |
| `Max Velocity` | Максимальная угловая скорость |
| `Locked Jiggle Fraction` | Насколько поддаётся запертая дверь |

### Звуки

Назначь `AudioSource` на тот же объект и заполни `Open Clip`, `Close Clip`, `Locked Clip`, `Unlock Clip`.

### Программное управление

```csharp
door.Unlock();         // снять блокировку
door.UnlockAndOpen();  // снять блокировку и слегка приоткрыть (используй в OnUnlocked у CodeLock)
```

---

## Ящики — DrawerDrag

**Файл:** `Assets/Scripts/Interaction/DrawerDrag.cs`

Выдвижной ящик. Игрок тянет ЛКМ — ящик выдвигается. При отпускании — защёлкивается в крайнее положение.

### Настройка

1. Добавь компонент на Transform ящика
2. `Open Direction` — локальная ось выдвижения (например `(0,0,1)` для вперёд)
3. `Open Distance` — расстояние от закрытого до открытого (метры)
4. `Snap Threshold` — 0.5 означает: если открыт больше половины — защёлкнется открытым
5. Слой **Interactable Layer**

**Важно:** `DrawerDrag` автоматически отключает `Animator` на родительском объекте во время перетаскивания, чтобы анимация не мешала физике.

---

## Замки — CodeLock

**Файл:** `Assets/Scripts/Interaction/CodeLock.cs`

Электронный замок с цифровой клавиатурой. Генерирует случайный код при каждом запуске (или использует фиксированный).

### Настройка

1. Добавь `CodeLock` на объект с коллайдером
2. Назначь `Code Lock UI` — ссылка на компонент `CodeLockUI` (уже готов в Canvas)
3. Подключи `On Unlocked → door.UnlockAndOpen()`
4. Слой **Interactable Layer**

### Параметры

| Поле | Описание |
|---|---|
| `Randomize On Start` | Каждую сессию новый код |
| `Code Length` | Длина случайного кода |
| `Secret Code` | Фиксированный код (если не рандом) |
| `Required Item` | Предмет нужный чтобы открыть панель (потребляется при успехе) |
| `Missing Item Hint` | Сообщение если нужного предмета нет |

### Как показать код игроку в сцене

Добавь компонент `CodeHintDisplay` на любой объект (например, на записку или плакат) и укажи ссылку на `CodeLock` — он автоматически отобразит код.

---

## Записки — NoteInteraction

**Файл:** `Assets/Scripts/Interaction/NoteInteraction.cs`

Записка в мире. Открывает панель чтения. Объект остаётся в сцене — записка не подбирается в инвентарь.

### Настройка

1. Добавь `NoteInteraction` на объект с коллайдером
2. Создай `NoteData`: правая кнопка → **Create → Data → Note Data** (или аналогичный путь)
3. Укажи `Note Data` в компоненте
4. Слой **Interactable Layer**

### NoteData (ScriptableObject)

Содержит: заголовок, текст, опциональное изображение.

---

## Фонарик — FlashlightController

**Файл:** `Assets/Scripts/Flashlight/FlashlightController.cs`

Управляет фонариком через конфиг-ассет. Клавиша **F**. Работает только если условие в `FlashlightConfig` выполнено (например, батарейки в инвентаре).

### Настройка

1. Добавь `FlashlightController` на GameObject с компонентом `Light` (тип Spot)
2. Создай `FlashlightConfig`: **Create → Flashlight → Flashlight Config**
3. В конфиге настрой `On State` / `Off State` (интенсивность, угол, цвет) и `Operating Condition`
4. `Operating Condition` — ссылка на `InventoryCondition` (ScriptableObject проверяющий наличие предмета)

### Поведение

- При отсутствии батареек — фонарик не включается
- Если батарейки исчезают из инвентаря пока фонарик включён — выключается автоматически
- Интенсивность меняется плавно (`transitionSpeed`), остальные параметры — мгновенно

---

## Хоррор-система

### HorrorSystem

**Файл:** `Assets/Scripts/Core/HorrorSystem.cs`

Singleton-координатор. Получает события от `InventorySystem` и `GameManager`, находит подходящие `HorrorEvent` и запускает их.

Ручной запуск из любого скрипта:
```csharp
HorrorSystem.Instance.Trigger("my_event_id");
```

### HorrorEvent

**Файл:** `Assets/Scripts/Core/HorrorEvent.cs`

Один хоррор-момент. Компонент размещается на любом активном GameObject. Регистрируется в `HorrorSystem` автоматически при старте.

После срабатывания (`HasFired = true`) — больше не повторяется.

### HorrorTrigger

**Файл:** `Assets/Scripts/Core/HorrorTrigger.cs`

Вспомогательный компонент. Позволяет вызвать `HorrorSystem.Trigger` из коллайдера-триггера без написания кода.

---

## UI-системы

### InteractionUI

**Файл:** `Assets/Scripts/UI/InteractionUI.cs`

Панель подсказки взаимодействия и управление прицелом. Обновляется каждый кадр через `FPSController`.

```csharp
InteractionUI.Instance.SetHint(bool visible, string text, bool isPickable, CrosshairMode mode);
InteractionUI.Instance.SetCrosshair(CrosshairMode mode);
```

### PopupMessageSystem

**Файл:** `Assets/Scripts/UI/PopupMessageSystem.cs`

Всплывающие сообщения в углу экрана. Поддерживает очередь и ограничение количества одновременных сообщений.

```csharp
// Простое сообщение
PopupMessageSystem.Instance.Show("Нужен ключ от этой двери");

// С типом и длительностью
PopupMessageSystem.Instance.Show("Дверь открыта!", PopupMessageType.Event, 4f);
```

Типы: `Hint`, `Warning`, `Event` — влияют на визуальный стиль.

### MenuScript

**Файл:** `Assets/Scripts/UI/MenuScript.cs`

Меню паузы. Открывается клавишей **Escape**.

Важно: `MenuScript` не управляет курсором напрямую. Всё делается через `UIManager.OpenPanel` / `ClosePanel`. Это исключает конфликты с инвентарём и другими панелями.

Если в момент нажатия Escape открыта другая панель — меню паузы не открывается.

### NoteUI

**Файл:** `Assets/Scripts/UI/NoteUI.cs`

Панель для чтения записок. Открывается из `NoteInteraction.Interact()`.

### CodeLockUI

**Файл:** `Assets/Scripts/UI/CodeLockUI.cs`

Панель цифровой клавиатуры. Открывается из `CodeLock.Interact()`.

---

## Комнаты — RoomController

**Файл:** `Assets/Scripts/Room/RoomController.cs`

Компонент на корневом объекте комнаты. При блокировке отключает коллайдеры у всех `IInteractable` дочерних объектов — геометрия остаётся видимой, но взаимодействие невозможно.

```csharp
room.Unlock(); // разрешить взаимодействие
room.Lock();   // запретить взаимодействие
bool IsUnlocked; // текущее состояние
```

---

## Структура файлов

```
Assets/
├── Data/
│   ├── Items/           # ItemData ScriptableObjects
│   ├── Recipes/         # CraftingRecipe ScriptableObjects
│   ├── Notes/           # NoteData ScriptableObjects
│   └── Conditions/      # InventoryCondition ScriptableObjects
│
├── Prefabs/
│   ├── ui/              # UI-префабы (PopupMessageEntry и др.)
│   └── object/          # Игровые объекты
│
├── Scripts/
│   ├── Core/
│   │   ├── UIManager.cs            # Управление всеми UI-панелями
│   │   ├── GameManager.cs          # Прогресс по комнатам
│   │   ├── GameConfig.cs           # Тексты и цвета
│   │   ├── HorrorSystem.cs         # Координатор хоррор-событий
│   │   ├── HorrorEvent.cs          # Один хоррор-момент
│   │   ├── HorrorTrigger.cs        # Триггер из коллайдера
│   │   └── NeonLightFlicker.cs     # Мигание неоновых ламп
│   │
│   ├── Player/
│   │   ├── FPSController.cs        # Весь контроль персонажа
│   │   ├── PhysicsGrabber.cs       # Физическое перетаскивание
│   │   └── PlayerInputActions.cs   # Авто-генерация Input System
│   │
│   ├── Inventory/
│   │   ├── InventorySystem.cs      # Логика инвентаря
│   │   ├── ItemDataSO.cs           # ScriptableObject предмета
│   │   ├── CraftingRecipe.cs       # ScriptableObject рецепта
│   │   ├── PickableItem.cs         # Подбираемый объект в мире
│   │   ├── ItemInspector.cs        # 3D-просмотр предметов
│   │   ├── InventoryCondition.cs   # Условие наличия предмета
│   │   └── UI/
│   │       ├── InventoryUI.cs      # Открытие/закрытие панели
│   │       ├── InventorySlot.cs    # Один слот
│   │       ├── DraggableItem.cs    # Drag-and-drop иконки
│   │       ├── ItemTooltip.cs      # Тултип при наведении
│   │       └── InventoryHints.cs   # Подсказки внизу панели
│   │
│   ├── Interaction/
│   │   ├── IInteractable.cs        # Интерфейс взаимодействия + IDraggable
│   │   ├── DoorInteraction.cs      # Физическая дверь
│   │   ├── DrawerDrag.cs           # Выдвижной ящик
│   │   ├── CodeLock.cs             # Кодовый замок
│   │   ├── NoteInteraction.cs      # Записка
│   │   ├── DoorAnimator.cs         # Дверь через Animator
│   │   └── PhysicsDraggable.cs     # Маркер физического объекта
│   │
│   ├── Flashlight/
│   │   ├── FlashlightController.cs # Управление фонариком
│   │   ├── FlashlightConfig.cs     # ScriptableObject конфига
│   │   └── FlashlightLagFollow.cs  # Запаздывающее следование фонарика
│   │
│   ├── UI/
│   │   ├── InteractionUI.cs        # Подсказка взаимодействия + прицел
│   │   ├── MenuScript.cs           # Меню паузы (Escape)
│   │   ├── NoteUI.cs               # Панель чтения записок
│   │   ├── CodeLockUI.cs           # Панель кодового замка
│   │   ├── CodeHintDisplay.cs      # Отображение кода замка в сцене
│   │   ├── PopupMessageSystem.cs   # Всплывающие сообщения
│   │   ├── PopupMessageEntry.cs    # Один попап
│   │   └── PopupMessage.cs         # Данные одного сообщения
│   │
│   ├── Room/
│   │   └── RoomController.cs       # Блокировка/разблокировка комнаты
│   │
│   ├── Buttons/
│   │   ├── BaseButton.cs           # Базовый класс кнопки
│   │   ├── CloseBtn.cs             # Кнопка закрытия панели
│   │   └── ContinueButton.cs       # Кнопка продолжения
│   │
│   └── Editor/
│       └── MissingScriptCleaner.cs # Tools → Remove Missing Scripts
│
├── Docs/                # Документация (этот файл)
├── Scenes/              # Сцены проекта
├── Materials/           # Материалы
├── Prefabs/             # Префабы
└── Sprites/ui/          # UI спрайты, иконки предметов
```
