## Escape Room — обзор проекта

Игра жанра Escape Room от первого лица. Игрок находится в закрытом
помещении, решает загадки, собирает предметы и использует их чтобы
открыть дверь в следующую комнату. Всего планируется 5 комнат.

---

## Технический стек

- **Unity 6000.3**, рендер **URP**
- **Input System** `com.unity.inputsystem 1.18.0` — новый пакет, не старый `Input Manager`
- **uGUI** — UI инвентаря на Canvas
- Одна сцена `SampleScene` — все комнаты существуют одновременно,
  блокировка через `Collider.enabled`, не через `SetActive`

---

## Структура сцены

```
SampleScene
├── Global Volume
├── Env
│   └── FirstRoom              # единственная готовая комната
│       ├── dors               # дверь (пока не подключена к системе)
│       ├── Walls
│       ├── flor
│       ├── props
│       │   └── Key_Pickup     # тестовый подбираемый предмет (PickableItem)
│       ├── Celing
│       └── NeonLamp x2
├── Player                     # FirstPersonController + InventoryUI
│   └── CameraRoot
│       └── Main Camera
├── GameManager                # GameManager.cs — прогресс между комнатами
├── InventorySystem            # InventorySystem.cs — хранит предметы, крафт
├── Canvas                     # UI инвентаря
│   └── InventoryPanel
│       └── SlotsContainer
└── EventSystem
```

---

## Структура скриптов

```
Assets/Scripts/
├── Core/
│   └── GameManager.cs          # синглтон, массив RoomController[], переход между комнатами
├── Room/
│   └── RoomController.cs       # Lock/Unlock — включает/выключает Collider на IInteractable объектах
├── Player/
│   ├── FirstPersonController.cs # движение, прыжок, приседание, raycast взаимодействия (клавиша E)
│   ├── IInteractable.cs         # интерфейс с методом Interact()
│   ├── PlayerInputActions.cs    # автогенерированный файл — НЕ РЕДАКТИРОВАТЬ
│   └── PlayerInputActions.inputactions  # Input Actions asset
├── Inventory/
│   ├── ItemData.cs              # ScriptableObject — данные предмета (название, иконка)
│   ├── CraftingRecipe.cs        # ScriptableObject — рецепт: A + B = C
│   ├── InventorySystem.cs       # синглтон — List<ItemData>, AddItem, TryCombine
│   ├── PickableItem.cs          # MonoBehaviour на объекте в сцене, IInteractable
│   └── UI/
│       ├── InventoryUI.cs       # открытие по Tab, RefreshSlots
│       ├── InventorySlot.cs     # один слот, IDropHandler для крафта
│       └── DraggableItem.cs     # drag-and-drop иконки
└── Other/
    └── CursorController.cs
```

---

## Как работает взаимодействие с предметами

```
Игрок нажимает E
  └── FirstPersonController.OnInteract()
        └── Physics.Raycast() на слой "Interactable Layer"
              └── hit.collider.TryGetComponent<IInteractable>()
                    └── interactable.Interact()

PickableItem.Interact()
  ├── InventorySystem.Instance.AddItem(itemData)
  │     └── OnInventoryChanged?.Invoke()
  │           └── InventoryUI.RefreshSlots()  ← перестраивает слоты
  └── Destroy(gameObject)
```

**Важно:** объект должен быть на слое `Interactable Layer`
и у `FirstPersonController` в поле `Interactable Layer` должна стоять галочка именно на этом слое.

---

## Как работает инвентарь (Tab)

```
Игрок нажимает Tab
  └── InventoryUI.OnToggleInventory()
        ├── OpenInventory()
        │     ├── inventoryPanel.SetActive(true)
        │     ├── Cursor.lockState = None
        │     └── RefreshSlots()  — создаёт SlotPrefab для каждого ItemData
        └── CloseInventory()
              ├── inventoryPanel.SetActive(false)
              └── Cursor.lockState = Locked
```

Слоты создаются динамически при каждом открытии.
Крафт: перетащи один слот на другой → `InventorySlot.OnDrop()` → `InventorySystem.TryCombine()`.

---

## Как работает система комнат

Все комнаты активны всегда. Блокировка — через `Collider.enabled`.

```
GameManager.Awake()
  └── InitializeRooms()
        ├── rooms[0].Unlock()  — включает коллайдеры IInteractable объектов
        └── rooms[1..n].Lock() — выключает коллайдеры

RoomDoor.Open()  (ещё не реализовано)
  └── GameManager.Instance.OnRoomExited()
        └── rooms[next].Unlock()
```

---

## Что сделано

- [x] `FirstPersonController` — движение, прыжок, приседание, взаимодействие (E)
- [x] `IInteractable` — интерфейс для всех интерактивных объектов
- [x] `GameManager` — каркас системы комнат
- [x] `RoomController` — Lock/Unlock взаимодействия в комнате
- [x] `ItemData` — ScriptableObject предмета
- [x] `CraftingRecipe` — ScriptableObject рецепта
- [x] `InventorySystem` — хранение предметов, крафт
- [x] `PickableItem` — подбор предмета из мира
- [x] `InventoryUI` — открытие по Tab, отображение слотов
- [x] `InventorySlot` — слот с иконкой, приём drop
- [x] `DraggableItem` — перетаскивание иконки
- [x] Тестовый предмет `Key_Pickup` в `FirstRoom`

## Что предстоит сделать

- [ ] `IPuzzle` + `PuzzleBase` — базовая система загадок
- [ ] `IDoorCondition` — условия открытия двери
- [ ] `RoomDoor` — дверь с гибкими условиями (загадка / ключ / комбо)
- [ ] Конкретные загадки: `KeypadPuzzle`, `ItemUsePuzzle` и др.
- [ ] Подключить `RoomController` к `GameManager` в инспекторе
- [ ] Создать `ItemData` ассеты для всех предметов игры
- [ ] Создать `CraftingRecipe` ассеты (батарейки + фонарик и т.д.)
- [ ] UI: подсказка "нажми E" при прицеливании на предмет
- [ ] Остальные 4 комнаты

---

## Слои (Layers)

| Слой | Назначение |
|---|---|
| `Default` | геометрия, пропсы без взаимодействия |
| `Interactable Layer` | объекты которые можно подобрать или с которыми можно взаимодействовать |
| `UI` | Canvas элементы |

---

## Префабы

| Префаб | Путь | Назначение |
|---|---|---|
| `SlotPrefab` | `Assets/Prefabs/ui/SlotPrefab.prefab` | один слот инвентаря с иконкой и drag-and-drop |

## Данные (ScriptableObjects)

| Тип | Путь | Создать через |
|---|---|---|
| `ItemData` | `Assets/Data/Items/` | `Create → Inventory → Item Data` |
| `CraftingRecipe` | `Assets/Data/Recipes/` | `Create → Inventory → Crafting Recipe` |
