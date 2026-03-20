# Inventory System

## Обзор

Инвентарь — это три независимых слоя: **данные** (ScriptableObjects), **логика** (`InventorySystem`) и **UI** (`InventoryUI` + слоты). Слои общаются через событие `OnInventoryChanged` — UI не знает о логике, логика не знает о UI.

---

## Структура файлов

```
Assets/Scripts/Inventory/
├── ItemDataSO.cs          # ScriptableObject — описание одного предмета
├── CraftingRecipe.cs      # ScriptableObject — правило крафта
├── InventorySystem.cs     # Singleton — вся логика инвентаря
├── PickableItem.cs        # Компонент на объекте в мире — подбор предмета
├── ItemInspector.cs       # Singleton — управление инспекцией предмета
└── UI/
    ├── InventoryUI.cs     # Открытие/закрытие панели, создание слотов
    ├── InventorySlot.cs   # Один слот — отображение + drop-зона + ПКМ-превью
    ├── DraggableItem.cs   # Иконка предмета — drag-and-drop поведение
    └── InventoryHints.cs  # Панель подсказок управления внизу инвентаря

Assets/Scripts/Editor/
└── MissingScriptCleaner.cs  # Утилита: Tools → Remove Missing Scripts

Assets/Data/Items/         # ItemData ассеты
Assets/Data/Recipes/       # CraftingRecipe ассеты
```

---

## Данные

### `ItemData` (ScriptableObject)

Создаётся через **Assets > Create > Inventory > Item Data**.

| Поле | Описание |
|---|---|
| `itemName` | Название предмета |
| `description` | Описание (показывается в тултипе) |
| `icon` | Иконка для слота инвентаря |
| `inspectionPrefab` | Prefab для 3D-просмотра. Если пустое — предмет подбирается напрямую без инспекции |
| `consumeOnUse` | Если включено — предмет удаляется из инвентаря после использования (дверь, замок). По умолчанию `true` |

Флаг `consumeOnUse` проверяется в `DoorInteraction.Interact()` и `CodeLock.TryUnlock()`. Примеры настройки:

| Предмет | `consumeOnUse` | Поведение |
|---|---|---|
| Ключ от конкретной двери | `true` | Исчезает после открытия |
| Мастер-ключ / карта доступа | `false` | Остаётся в инвентаре, работает многократно |

### `CraftingRecipe` (ScriptableObject)

Создаётся через **Assets > Create > Inventory > Crafting Recipe**.

Порядок ингредиентов не важен — рецепт работает в обе стороны.

---

## Логика — `InventorySystem`

Singleton на GameObject в сцене. Хранит `ItemData[] _slots` — массив фиксированного размера. Позиция предмета в массиве = его позиция в инвентаре.

### Событие

```csharp
public event Action OnInventoryChanged;
```

Стреляет после каждого изменения массива `_slots`. UI подписывается в `Start`, отписывается в `OnDisable`.

---

## UI — `InventoryUI`

Управляет панелью инвентаря. Создаёт слоты один раз в `Start`, потом только обновляет их содержимое. Количество слотов берётся из `InventorySystem.Instance.MaxSlots`.

**Открытие/закрытие** — кнопка из Input System (`Player.Inventory`). При открытии:

- Показывает `inventoryPanel`
- Снимает блокировку курсора
- Отключает ввод игрока (`FPSController.SetPlayerInputEnabled(false)`)

При закрытии инвентаря автоматически завершает активный 3D-превью через `ItemInspector.CancelPreviewIfActive()` — чтобы 3D-объект не оставался в сцене.

`RefreshSlots()` — вызывается при `OnInventoryChanged`. Проходит по всем слотам и вызывает `slot.Setup(GetItemAt(i))`.

---

## UI — `InventorySlot`

Один слот в сетке. Всегда видим как фон. Иконка (`Image`) включается/выключается через `Image.enabled` — сам GameObject остаётся активным, чтобы `DraggableItem` работал в любом состоянии.

`OnDrop` — точка входа для drag-and-drop:

```
Предмет брошен на слот
├── Оба слота заняты → TryCombine(source, target)
│   ├── Рецепт найден → результат в target, source очищается
│   └── Рецепта нет  → SwapSlots (предметы меняются местами)
└── Один слот пуст  → SwapSlots (предмет переезжает)
```

`OnPointerClick (ПКМ)` — открывает 3D-превью предмета через `ItemInspector.BeginPreview(item)`. Работает только если у предмета есть `inspectionPrefab`.

---

## UI — `DraggableItem`

Компонент на дочернем объекте `Icon` внутри слота. Реализует `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`.

`OnBeginDrag`:
1. Обновляет `SourceSlot` через `GetComponentInParent<InventorySlot>()`
2. Отменяет drag если слот пуст (`eventData.pointerDrag = null`)
3. Перепривязывает `Icon` к корневому `Canvas` (чтобы иконка рисовалась поверх всего)
4. `CanvasGroup.blocksRaycasts = false` — пропускает raycast на слот под курсором

`OnDrag`: двигает `transform.position` за курсором.

`OnEndDrag`: возвращает `Icon` к `_originalParent`, сбрасывает позицию и `CanvasGroup`.

---

## UI — `InventoryHints`

Компонент на объекте `HintsBar` внутри `InventoryPanel`. Отображает строку подсказок управления внизу инвентаря.

### Сцена

```
InventoryPanel
├── SlotsContainer      # Сетка слотов
└── HintsBar            # Компонент InventoryHints
    └── HintsText       # TextMeshProUGUI — итоговый текст подсказок
```

### Параметры Inspector

| Поле | Описание |
|---|---|
| `Hints Label` | Ссылка на `TextMeshProUGUI` объекта `HintsText` |
| `Hints` | Массив подсказок: каждая запись содержит `key` и `action` |
| `Hints Per Row` | Сколько подсказок на одну строку (по умолчанию 2) |
| `Separator` | Строка между клавишей и действием |
| `Column Gap` | Отступ между подсказками в одной строке |

---

## Подбор предметов — `PickableItem`

Компонент на любом GameObject в мире. Требует `Collider`. Реализует `IInteractable`.

При взаимодействии игрока открывает панель инспекции (`ItemInspector.BeginInspection`). Если `ItemInspector` недоступен — добавляет предмет напрямую и уничтожает себя.

---

## Инспекция предметов — `ItemInspector`

Singleton на GameObject `InspectionSetup` в сцене. Показывает 3D-превью предмета. Поддерживает два режима:

- **Режим подбора** — открывается при взаимодействии с `PickableItem` в мире. Показывает название и описание. Закрытие кладёт предмет в инвентарь и уничтожает мировой объект.
- **Режим превью** — открывается по ПКМ на слоте инвентаря. Название и описание скрыты. Закрытие не влияет на инвентарь.

### Сцена

```
InspectionSetup            # GameObject с компонентом ItemInspector
├── InspectionCamera       # Orthographic camera → RenderTexture
Canvas/
└── InspectionPanel        # UI панель инспекции
    ├── HintText           # Подсказки управления
    ├── PreviewImage       # RawImage отображающий RenderTexture
    ├── InfoPanel
    │   ├── ItemNameText   # Скрыт в режиме превью
    │   └── DescriptionText  # Скрыт в режиме превью
    ├── TakeButton         # → ItemInspector.ConfirmPickup()
    └── CancelButton       # → ItemInspector.CancelInspection()
```

### Публичные методы

| Метод | Описание |
|---|---|
| `BeginInspection(item, worldObject)` | Режим подбора. Вызывается из `PickableItem` |
| `BeginPreview(item)` | Режим превью из инвентаря. ПКМ на слоте |
| `ConfirmPickup()` | Добавляет предмет в инвентарь и закрывает панель |
| `CancelPreviewIfActive()` | Закрывает превью без изменения инвентаря. Вызывается из `InventoryUI.CloseInventory` |

### Управление в режиме подбора

| Действие | Результат |
|---|---|
| ЛКМ + drag | Ручное вращение модели |
| E / Escape | Подобрать предмет |

### Управление в режиме превью (из инвентаря)

| Действие | Результат |
|---|---|
| ЛКМ + drag | Ручное вращение модели |
| ПКМ / E / Escape | Закрыть превью |

### Как работает

1. `PickableItem.Interact()` → `ItemInspector.BeginInspection(item, worldObject)`
2. Инстанциируется `item.inspectionPrefab` в точке `InspectionOrigin` (y = -1000) — вне видимости основной камеры
3. По bounds всех `Renderer` вычисляется геометрический центр модели
4. Создаётся `InspectionPivot` в центре bounds; модель парентится к нему
5. К пивоту применяется `initialRotation` — начальный ракурс 3/4
6. Камера настраивается orthographic, `orthographicSize = maxSize * framingMultiplier * 0.5`
7. Запускается idle spin — модель плавно поворачивается с ease-out и останавливается

### Параметры Inspector

| Поле | По умолчанию | Описание |
|---|---|---|
| `inspectionCamera` | — | Ссылка на камеру инспекции |
| `framingMultiplier` | `2.2` | Чем больше — тем меньше модель в кадре |
| `rotationSpeed` | `180` | Скорость ручного вращения (градус/сек) |
| `initialRotation` | `(15, -35, 0)` | Начальный поворот при открытии, Euler |
| `idleSpinDuration` | `1.8` | Длительность вступительной анимации (сек) |
| `idleSpinSpeed` | `80` | Пиковая скорость idle spin (градус/сек) |

### Технические детали

- Камера — `Orthographic`, без HDR, `ClearFlags = SolidColor`, прозрачный фон `(0,0,0,0)`
- `RenderTexture` создаётся в `Awake` под размер экрана (`Screen.width × Screen.height`)
- Culling mask камеры ограничен слоем `"Inspection"` — модель невидима основной камерой
- Idle spin использует `Mathf.Cos(t * π/2)` для ease-out затухания
- `worldPositionStays: true` при парентинге → `initialRotation` применяется после `SetParent`
- При закрытии `_inspectionPivot` уничтожается вместе с дочерним instance
- `ItemNameText` и `DescriptionText` восстанавливаются через `SetActive(true)` при закрытии

---

## Утилита — `MissingScriptCleaner`

Editor-only скрипт. Меню: **Tools → Remove Missing Scripts**.

Рекурсивно проходит по всей иерархии загруженных сцен, удаляет компоненты с отсутствующим скриптом через `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` и сохраняет сцену.

---

## Как добавить новый предмет

1. **Assets > Create > Inventory > Item Data** — заполнить поля, назначить иконку и `inspectionPrefab`
2. Создать GameObject в сцене, добавить `PickableItem`, назначить новый `ItemData`

## Как добавить рецепт крафта

1. **Assets > Create > Inventory > Crafting Recipe** — указать два ингредиента и результат
2. Добавить ассет в массив `Recipes` на компоненте `InventorySystem` в сцене
