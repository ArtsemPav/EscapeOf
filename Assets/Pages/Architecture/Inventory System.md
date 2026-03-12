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
    ├── InventorySlot.cs   # Один слот — отображение + drop-зона
    └── DraggableItem.cs   # Иконка предмета — drag-and-drop поведение

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
При взаимодействии вызывает `InventorySystem.Instance.AddItem(itemData)` и уничтожает себя.

---

## Как добавить новый предмет

1. **Assets > Create > Inventory > Item Data** — заполнить поля, назначить иконку
2. Создать GameObject в сцене, добавить `PickableItem`, назначить `ItemData`

## Как добавить рецепт крафта

1. **Assets > Create > Inventory > Crafting Recipe** — указать два ингредиента и результат
2. Добавить ассет в массив `Recipes` на компоненте `InventorySystem` в сцене
