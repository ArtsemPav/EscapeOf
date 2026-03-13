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
    ├── InventorySlot.cs   # Один слот — отображение + drop-зона
    └── DraggableItem.cs   # Иконка предмета — drag-and-drop поведение

Assets/Scripts/Editor/
└── MissingScriptCleaner.cs  # Утилита: Tools → Remove Missing Scripts

Assets/Data/Items/         # ItemData ассеты
Assets/Data/Recipes/       # CraftingRecipe ассеты
```

---

## Данные

### `ItemData` (ScriptableObject)

Создаётся через **Assets > Create > Inventory > Item Data**.

Поле `inspectionPrefab` должно быть заполнено для показа 3D-превью. Если пустое — предмет подбирается напрямую без инспекции.

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

## Подбор предметов — `PickableItem`

Компонент на любом GameObject в мире. Требует `Collider`. Реализует `IInteractable`.

При взаимодействии игрока открывает панель инспекции (`ItemInspector.BeginInspection`). Если `ItemInspector` недоступен — добавляет предмет напрямую и уничтожает себя.

---

## Инспекция предметов — `ItemInspector`

Singleton на GameObject `InspectionSetup` в сцене. Показывает 3D-превью предмета перед добавлением в инвентарь. Использует выделенную камеру, рендерящую в `RenderTexture`, которая отображается через `RawImage` в UI.

### Сцена

```
InspectionSetup            # GameObject с компонентом ItemInspector
├── InspectionCamera       # Orthographic camera → RenderTexture
Canvas/
└── InspectionPanel        # UI панель инспекции
    ├── HintText           # Подсказки управления
    ├── PreviewImage       # RawImage отображающий RenderTexture
    ├── InfoPanel
    │   ├── ItemNameText
    │   └── DescriptionText
    ├── TakeButton         # → ItemInspector.ConfirmPickup()
    └── CancelButton       # → ItemInspector.CancelInspection()
```

### Как работает

1. `PickableItem.Interact()` → `ItemInspector.BeginInspection(item, worldObject)`
2. Инстанциируется `item.inspectionPrefab` в точке `InspectionOrigin` (y = -1000) — вне видимости основной камеры
3. По bounds всех `Renderer` вычисляется геометрический центр модели
4. Создаётся `InspectionPivot` в центре bounds; модель парентится к нему
5. К пивоту применяется `initialRotation` — начальный ракурс 3/4
6. Камера настраивается orthographic, `orthographicSize = maxSize * framingMultiplier * 0.5`
7. Запускается idle spin — модель плавно поворачивается с ease-out и останавливается
8. `E` — подтвердить подбор, `Escape` — отмена; ЛКМ + drag — ручное вращение

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
