# Inventory System

## Обзор

Инвентарь — три независимых слоя: **данные** (ScriptableObjects), **логика** (`InventorySystem`) и **UI** (`InventoryUI` + слоты). Слои общаются через событие `OnInventoryChanged` — UI не знает о логике, логика не знает о UI.

---

## Структура файлов

```
Assets/Scripts/Inventory/
├── ItemDataSO.cs          # ScriptableObject — описание одного предмета
├── CraftingRecipe.cs      # ScriptableObject — правило крафта
├── InventorySystem.cs     # Singleton — вся логика инвентаря
├── PickableItem.cs        # Компонент на объекте в мире — подбор предмета
└── UI/
    ├── InventoryUI.cs     # Открытие/закрытие панели, создание слотов
    ├── InventorySlot.cs   # Один слот — отображение + drop-зона + ПКМ-превью
    ├── DraggableItem.cs   # Иконка предмета — drag-and-drop поведение
    └── InventoryHints.cs  # Панель подсказок управления внизу инвентаря

Assets/Data/Items/         # ItemData ассеты
Assets/Data/Recipes/       # CraftingRecipe ассеты
Assets/Pages/Architecture/ # Документация
```

---

## Данные

### `ItemData` (ScriptableObject)
Создаётся через **Assets > Create > Inventory > Item Data**.

| Поле | Тип | Назначение |
|---|---|---|
| `itemName` | `string` | Отображаемое имя |
| `description` | `string` | Описание предмета |
| `icon` | `Sprite` | Иконка в инвентаре |

### `CraftingRecipe` (ScriptableObject)
Создаётся через **Assets > Create > Inventory > Crafting Recipe**.

| Поле | Тип | Назначение |
|---|---|---|
| `ingredientA` | `ItemData` | Первый ингредиент |
| `ingredientB` | `ItemData` | Второй ингредиент |
| `result` | `ItemData` | Результат крафта |

Порядок ингредиентов не важен — рецепт работает в обе стороны.

---

## Логика — `InventorySystem`

Singleton на GameObject в сцене. Хранит `ItemData[] _slots` — массив фиксированного размера. Позиция предмета в массиве = его позиция в инвентаре.

### Публичные методы

| Метод | Что делает |
|---|---|
| `AddItem(ItemData)` | Кладёт предмет в первый свободный слот |
| `RemoveItem(ItemData)` | Удаляет предмет по ссылке, возвращает `bool` |
| `HasItem(ItemData)` | Проверяет наличие предмета |
| `GetItemAt(int)` | Возвращает `ItemData` по индексу слота |
| `SwapSlots(int, int)` | Меняет предметы двух слотов местами (работает с пустыми) |
| `TryCombine(int src, int tgt, out ItemData)` | Ищет рецепт, кладёт результат в `tgt`, очищает `src` |

### Событие

```csharp
public event Action OnInventoryChanged;
```

Стреляет после каждого изменения `_slots`. UI подписывается в `Start`, отписывается в `OnDisable`.

### Инспектор

| Поле | Назначение |
|---|---|
| `Max Slots` | Размер массива (по умолчанию 8) |
| `Recipes` | Массив всех `CraftingRecipe` |

---

## UI — `InventoryUI`

Управляет панелью инвентаря. Создаёт слоты один раз в `Start`, затем только обновляет содержимое через `RefreshSlots()`.

**Открытие/закрытие** — кнопка `Player.Inventory` из Input System. При открытии снимает блокировку курсора и отключает ввод игрока (`FPSController.SetPlayerInputEnabled(false)`).

При закрытии инвентаря автоматически завершает активный 3D-превью через `ItemInspector.CancelPreviewIfActive()` — чтобы 3D-объект не оставался в сцене.

### Инспектор

| Поле | Назначение |
|---|---|
| `Inventory Panel` | Корневой объект панели |
| `Slot Prefab` | Префаб слота |
| `Slots Container` | Transform-контейнер для слотов |

---

## UI — `InventorySlot`

Один слот в сетке. Иконка скрывается через `Image.enabled = false` — GameObject остаётся активным, чтобы `DraggableItem` работал в любом состоянии.

| Свойство | Что хранит |
|---|---|
| `SlotIndex` | Индекс в `_slots` |
| `Item` | Текущий `ItemData` (`null` = пусто) |
| `IsEmpty` | `Item == null` |

### Логика `OnDrop`

```
Предмет брошен на слот
├── Оба слота заняты → TryCombine(source, target)
│   ├── Рецепт найден → результат в target, source очищается
│   └── Рецепта нет  → SwapSlots (предметы меняются местами)
└── Один слот пуст  → SwapSlots (предмет переезжает)
```

`OnPointerClick (ПКМ)` — открывает 3D-превью предмета через `ItemInspector.BeginPreview(item)`. Работает только если у предмета есть `inspectionPrefab`.

---

## UI — `InventoryHints`

Компонент на объекте `HintsBar` внутри `InventoryPanel`. Отображает подсказки управления внизу инвентаря.

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

## UI — `DraggableItem`

Компонент на дочернем объекте `Icon` внутри слота.

| Этап | Действие |
|---|---|
| `OnBeginDrag` | Обновляет `SourceSlot`, отменяет drag если слот пуст, перепривязывает `Icon` к корневому `Canvas`, отключает `blocksRaycasts` |
| `OnDrag` | Двигает `transform.position` за курсором |
| `OnEndDrag` | Возвращает `Icon` к исходному родителю, сбрасывает позицию и `CanvasGroup` |

---

## Подбор предметов — `PickableItem`

Компонент на любом GameObject в мире. Требует `Collider`. Реализует `IInteractable`.

При взаимодействии вызывает `ItemInspector.BeginInspection(item, worldObject)`. Если `ItemInspector` недоступен — добавляет предмет напрямую и уничтожает себя.

---

## Инспекция предметов — `ItemInspector`

Singleton на `InspectionSetup`. Поддерживает два режима:

- **Режим подбора** — `BeginInspection(item, worldObject)`. Открывается из `PickableItem`. Показывает название и описание. Закрытие кладёт предмет в инвентарь.
- **Режим превью** — `BeginPreview(item)`. Открывается по ПКМ на слоте инвентаря. Название и описание скрыты. Закрытие не меняет инвентарь.

### Публичные методы

| Метод | Описание |
|---|---|
| `BeginInspection(item, worldObject)` | Режим подбора |
| `BeginPreview(item)` | Режим превью из инвентаря |
| `ConfirmPickup()` | Добавляет предмет в инвентарь, закрывает панель |
| `CancelPreviewIfActive()` | Закрывает превью без изменения инвентаря |

### Управление

| Режим | Действие | Результат |
|---|---|---|
| Подбор | ЛКМ + drag | Вращение модели |
| Подбор | E / Escape | Подобрать предмет |
| Превью | ЛКМ + drag | Вращение модели |
| Превью | ПКМ / E / Escape | Закрыть превью |

---

## Как добавить новый предмет

1. **Assets > Create > Inventory > Item Data** — заполнить поля, назначить иконку
2. Создать GameObject в сцене, добавить `PickableItem`, назначить `ItemData`

## Как добавить рецепт крафта

1. **Assets > Create > Inventory > Crafting Recipe** — указать два ингредиента и результат
2. Добавить ассет в массив `Recipes` на компоненте `InventorySystem` в сцене
