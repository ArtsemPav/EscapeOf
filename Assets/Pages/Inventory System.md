Инвентарь — это три независимых слоя: **данные** (ScriptableObjects), **логика** (`InventorySystem`) и **UI** (`InventoryUI` + слоты). Слои общаются через событие `OnInventoryChanged` — UI не знает о логике, логика не знает о UI.

Для новичков — начни с [@ id="/Pages/Private/Quick Start.md" label="Quick Start"], затем [@ id="/Pages/Private/Pickable Items.md" label="Pickable Items"].

---

## Структура файлов

```
Assets/Scripts/Inventory/
├── ItemDataSO.cs                # Класс ItemData — ScriptableObject с описанием одного предмета
├── CraftingRecipe.cs            # ScriptableObject — правило крафта
├── InventoryCondition.cs        # ScriptableObject — условие наличия предмета в инвентаре
├── InventorySystem.cs           # Singleton — вся логика инвентаря (+ руны)
├── InventoryAudio.cs            # Звук при успешном крафте (слушает OnCrafted)
├── ItemInspector.cs             # Singleton — 3D-инспекция предметов (подбор, превью, inspect-only)
├── PickableItem.cs              # Компонент на объекте в мире — подбор предмета
└── UI/
    ├── InventoryUI.cs           # Открытие/закрытие панели, создание слотов
    ├── InventorySlot.cs         # Один слот — отображение + drop-зона + ЛКМ-превью
    ├── DraggableItem.cs         # Иконка предмета — drag-and-drop поведение
    ├── InventoryHints.cs        # Панель подсказок управления внизу инвентаря
    ├── InventoryItemPreview.cs  # Встроенный 3D-превью в правой части инвентаря
    ├── InventoryBackdrop.cs     # Закрытие инвентаря кликом вне панели
    ├── IconHoverScale.cs        # Плавное масштабирование иконки при наведении + звуки
    ├── ItemTooltip.cs           # Тултип предмета при наведении на слот
    └── RunePouchUI.cs           # Иконка мешочка рун + тултип с прогрессом сбора

Assets/Scripts/Puzzle/Shared/
├── IPuzzleDropHandler.cs        # Интерфейс для пазлов, принимающих предметы из бара
├── IPuzzleDropTarget.cs         # Интерфейс 3D-целей дропа (подсказка/совместимость)
├── IPuzzleDropHoverHandler.cs   # Опциональная визуальная обратная связь при hover во время drag
├── PuzzleInventoryBar.cs        # Горизонтальный бар внизу экрана с прокруткой
└── PuzzleInventorySlot.cs       # Один слот в бара (drag + tooltip)

Assets/Scripts/Interaction/
└── PhysicsDraggable.cs    # Маркер-компонент на перетаскиваемых объектах

Assets/Scripts/Player/
├── FPSController.cs       # Контроллер персонажа
└── PhysicsGrabber.cs      # Физическое перетаскивание объектов мышью

Assets/Scripts/Puzzle/Medallion/
└── MedallionBoxUI.cs      # Пример реализации IPuzzleDropHandler (шкатулка с медальонами)

Assets/Scripts/Editor/
├── MissingScriptCleaner.cs  # Утилита: Tools → Remove Missing Scripts
└── ItemDataEditor.cs        # Кастомный Inspector для ItemData — интерактивный 3D-превью

Assets/Data/Items/         # ItemData ассеты
Assets/Data/Recipes/       # CraftingRecipe ассеты
```

---

## Данные

### `ItemData` (ScriptableObject, файл `ItemDataSO.cs`)

Создаётся через **Assets > Create > Inventory > Item Data**.


| Поле                       | Описание                                                                                                                              |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `itemName`                 | Название предмета                                                                                                                     |
| `description`              | Описание (показывается в тултипе)                                                                                                     |
| `_itemId` / `ItemId`       | Стабильный ID для сейв-системы. Если пуст — используется имя ассета. Не переименовывай ассет после сохранений                         |
| `icon`                     | Иконка для слота инвентаря                                                                                                            |
| `consumeOnUse`             | Если включено (по умолчанию) — предмет удаляется из инвентаря после использования                                                     |
| `showInDevPanel`           | Показывать предмет в списке dev-панели для быстрого тестирования                                                                      |
| `inspectionPrefab`         | Prefab для 3D-просмотра. Если пустой — предмет подбирается напрямую без инспекции. Также используется как 3D-модель в превью и лунках |
| `useCustomPreviewRotation` | Если включено — начальный ракурс превью берётся из `previewRotation` вместо глобального `initialRotation`                             |
| `previewRotation`          | Euler-углы начального поворота в 3D-превью (инспекция и инвентарь). По умолчанию `(15, -35, 0)`                                       |
| `disableIdleSpin`          | Отключить автовращение модели в превью осмотра                                                                                        |
| `lockRotationX/Y/Z`        | Запретить ручное вращение по соответствующей оси во время превью/осмотра                                                              |
| `disableShimmer`           | Предмет не будет привлекать внимание мерцанием (shimmer)                                                                              |
| `previewScale`             | Множитель масштаба модели в превью осмотра (1 = реальный размер)                                                                      |


Метод `GetLiquidColor()` возвращает цвет жидкости предмета: приоритет — компонент `ChemicalPuzzle.LiquidWobble` на inspectionPrefab, затем свойство `_LiquidColor` первого материала, иначе белый.

Флаг `consumeOnUse` проверяется в `DoorInteraction.Interact()` и `CodeLock.TryUnlock()`. Примеры настройки:


| Предмет                     | `consumeOnUse` | Поведение                                  |
| --------------------------- | -------------- | ------------------------------------------ |
| Ключ от конкретной двери    | `true`         | Исчезает после открытия                    |
| Мастер-ключ / карта доступа | `false`        | Остаётся в инвентаре, работает многократно |


**Настройка ракурса превью через редакторский виджет**

При наличии `inspectionPrefab` в Inspector появляется встроенный 3D-виджет предмета:

- Drag внутри виджета — вращение по X/Y
- Shift + Drag горизонтально — вращение по Z
- При первом drag автоматически устанавливается `useCustomPreviewRotation = true` и углы сохраняются в `previewRotation`

### `CraftingRecipe` (ScriptableObject)

Создаётся через **Assets > Create > Inventory > Crafting Recipe**. Порядок ингредиентов не важен — рецепт работает в обе стороны.


| Поле                          | Описание                                                                          |
| ----------------------------- | --------------------------------------------------------------------------------- |
| `ingredientA` / `ingredientB` | Два ингредиента                                                                   |
| `result`                      | Основной результат крафта                                                         |
| `secondaryResult`             | Опциональный второй результат — кладётся в первый свободный слот рядом с основным |
| `conserveIngredientA`         | Ингредиент A не расходуется (слот сохраняется)                                    |
| `conserveIngredientB`         | Ингредиент B не расходуется (слот сохраняется)                                    |


### `InventoryCondition` (ScriptableObject)

Переиспользуемое условие «предмет в инвентаре» (меню **Create > Game > Conditions > Inventory Condition**). Поля: `requiredItem` (обязательный предмет) и `anyOfItems` (условие выполнено, если есть ЛЮБОЙ из списка). Метод `IsMet()` проверяет через `InventorySystem.HasItem`.

---

## Логика — `InventorySystem`

Singleton на GameObject в сцене. Хранит `ItemData[] _slots` — массив фиксированного размера (`maxSlots`, по умолчанию 8).

**Модель слотов:** `AddItem()` кладёт предмет в **слот 0**, сдвигая все существующие предметы вправо (`ShiftSlotsRight`). Удаления и крафт вызывают `CompactSlots()` — все непустые элементы сдвигаются влево без дырок. Поэтому перед добавлением всегда проверяй `IsFull` — у полностью забитого инвентаря правый крайний предмет был бы потерян.

### Руны

Отдельный слой поверх слотов: руны (`_runeDefinitions` — массив ItemData) не занимают слоты, а собираются в `HashSet<string> _collectedRuneIds`. Свойства `TotalRuneCount` / `CollectedRuneCount` / `RemainingRuneCount`; методы `IsRune(item)` и `HasRune(itemId)`. Добавление/удаление руны идёт через обычные `AddItem`/`RemoveItem` — они сами перенаправляют руны в набор.

### События

```csharp
public event Action OnInventoryChanged;          // любое изменение слотов или рун
public event Action OnRunesChanged;              // изменился набор собранных рун
public event Action OnCrafted;                   // рецепт успешно исполнен (слушает InventoryAudio)
public event Action<string> OnInventoryStateChanged; // снапшот "0:ItemKey,1:ItemRope, руны: a" для логов
```

UI подписывается в `Start` и отписывается в `OnDisable` (оба события).

### Публичные методы


| Метод / Свойство                                  | Описание                                                                                               |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `bool IsFull`                                     | `true` когда все слоты заняты                                                                          |
| `bool AddItem(item)`                              | Кладёт в слот 0 со сдвигом вправо; руны идут в набор. Возвращает `false` если инвентарь полон          |
| `bool RemoveItem(item)`                           | Удаляет предмет (или руну) и вызывает `CompactSlots()`                                                 |
| `ItemData GetItemAt(i)`                           | Предмет по индексу слота, или `null`                                                                   |
| `bool HasItem(item)`                              | Проверяет наличие предмета (руны — через набор)                                                        |
| `void SwapSlots(a, b)`                            | Меняет местами два слота (drag-and-drop)                                                               |
| `bool TryCombine(src, tgt, out result)`           | Синхронный крафт: результат в слот consumed-ингредиента, `CompactSlots()`, `OnCrafted`                 |
| `bool TryCombineDeferred(src, tgt, out result)`   | Отложенный крафт: ингредиенты расходуются, результат возвращает вызывающему (превью перед размещением) |
| `bool ClearSlot(slotIndex, bool reserve = false)` | Очищает слот и компактирует (`reserve` — legacy-параметр, механизм резерваций удалён)                  |
| `bool PlaceItemAt(slotIndex, item)`               | Кладёт в указанный пустой слот; при занятом — фолбэк в `AddItem`                                       |
| `bool ReplaceItem(from, to)`                      | Заменяет предмет в первом найденном слоте, позиция сохраняется (компакции нет)                         |
| `void Compact()`                                  | Компакция всех слотов + `OnInventoryChanged` + сейв (после отложенного крафта)                         |
| `void ReleaseAllReservations()`                   | No-op для обратной совместимости (резервации удалены)                                                  |
| `void ClearAll()`                                 | Очищает все слоты и руны (сброс сохранения)                                                            |
| `int MaxSlots` / `ItemData[] AllItems`            | Число слотов / база ассетов (для dev-панели)                                                           |
| `ItemData FindItemByIdPublic(id)`                 | Поиск предмета по ItemId (для dev-панели)                                                              |


### Защита от переполнения

`AddItem()` возвращает `bool`. Все пути подбора (`PickableItem.Interact` → `ItemInspector.BeginInspection`, `ItemInspector.ConfirmPickup`) проверяют результат перед уничтожением мирового объекта — предмет остаётся в мире, если места нет. При `onPickup`-коллбэке ответственность за добавление несёт коллбэк.

`MedallionBoxUI.TryRetrieveFromHole()` (`Assets/Scripts/Puzzle/Medallion/MedallionBoxUI.cs`) проверяет `IsFull` **до** вызова `hole.Retrieve()` — медальон не пропадёт при попытке вернуть его в заполненный инвентарь.

### Инспектор


| Поле               | Описание                                                                             |
| ------------------ | ------------------------------------------------------------------------------------ |
| `Max Slots`        | Максимальное число слотов (по умолчанию 8)                                           |
| `Rune Definitions` | `_runeDefinitions` — все rune-предметы; они маршрутизируются в мешочек, а не в слоты |
| `_allItems`        | Скрыт (`[HideInInspector]`). Заполняется автоматически — см. `InventoryAutoPopulate` |
| `recipes`          | Скрыт (`[HideInInspector]`). Заполняется автоматически — см. `InventoryAutoPopulate` |


---

## Editor — `InventoryAutoPopulate`

`Assets/Scripts/Editor/InventoryAutoPopulate.cs`

Автоматически сканирует папки `Assets/Data/Items` (тип `ItemData`) и `Assets/Data/Recipes` (тип `CraftingRecipe`) и записывает найденные ассеты в скрытые поля `_allItems` и `recipes` компонента `InventorySystem` в открытых сценах. Устраняет необходимость вручную поддерживать эти списки.

**Когда запускается:**

- При каждом domain reload (старт редактора, компиляция) — через `[InitializeOnLoad]`
- При добавлении, удалении или переименовании любого файла в `Assets/Data/Items` или `Assets/Data/Recipes` — через `AssetPostprocessor`
- Вручную: **Tools → Inventory → Refresh Items and Recipes**

**Чтобы добавить новый предмет:** положи `ItemData` ассет в `Assets/Data/Items` — список обновится автоматически.

---

## UI — `InventoryUI`

Singleton (`Instance`). Управляет панелью инвентаря и связанными объектами. Создаёт слоты один раз в `Start` (`CreateSlots`), потом только обновляет их содержимое (`RefreshSlots`).

**Открытие/закрытие** — событие `InputManager.OnInventoryPerformed` (Input System). При открытии `OpenInventory()`:

- Активирует `inventoryBackdrop` (прозрачный экран за панелью)
- Открывает панель через `UIManager.Instance.OpenPanel(inventoryPanel)`
- Обновляет слоты (`RefreshSlots`)
- Автоматически показывает первый предмет инвентаря в 3D-превью (`InventoryItemPreview.Show`)

Закрытие `CloseInventory()` (публичный, безопасно игнорирует повторный вызов): скрывает тултип (`ItemTooltip.Hide`), очищает превью (`InventoryItemPreview.Clear`), деактивирует backdrop, вызывает `ItemInspector.CancelPreviewIfActive()` (иначе счётчик открытых панелей UIManager рассинхронизируется) и закрывает панель через `UIManager.ClosePanel`.

### Инспектор


| Поле                 | Описание                                             |
| -------------------- | ---------------------------------------------------- |
| `Inventory Panel`    | GameObject панели инвентаря                          |
| `Inventory Backdrop` | GameObject полноэкранного фона (`InventoryBackdrop`) |
| `Slot Prefab`        | Префаб одного слота (`InventorySlot`)                |
| `Slots Container`    | Transform сетки слотов                               |


---

## UI — `InventoryBackdrop`

Полноэкранный прозрачный overlay, размещённый в Canvas **перед** `InventoryPanel` в иерархии (рендерится позади неё). Реализует `IPointerClickHandler`.

Клик ЛКМ на backdrop (т.е. вне панели инвентаря) → вызывает `InventoryUI.Instance.CloseInventory()`.

### Сцена

```
Canvas
├── InventoryBackdrop     # Image alpha=0.4, raycastTarget=true; InventoryBackdrop.cs
│                         # RectTransform: anchors (0,0)→(1,1) — полный экран
└── InventoryPanel        # блокирует raycast на себя — клики внутри до backdrop не доходят
```

`InventoryUI` активирует backdrop при открытии и деактивирует при закрытии.

---

## UI — `InventorySlot`

Один слот в сетке. Всегда видим как фон. Иконка (`Image`) включается/выключается через `Image.enabled` — сам GameObject остаётся активным, чтобы `DraggableItem` работал в любом состоянии. Хранит `SlotIndex` и `Item`; при наведении масштабирует иконку через `IconHoverScale` и проигрывает звуки hover/press.

`OnDrop` — точка входа для drag-and-drop:

```
Предмет брошен на слот
├── Оба слота заняты → TryCombine(source, target)
│   ├── Рецепт найден → результат в слот consumed-ингредиента, CompactSlots()
│   │                   InventoryItemPreview.Show(craftedItem) — превью обновляется сразу
│   └── Рецепта нет  → SwapSlots (предметы меняются местами)
└── Один слот пуст  → SwapSlots (предмет переезжает)
```

`OnPointerClick (ЛКМ)` — показывает встроенный 3D-превью предмета через `InventoryItemPreview.Show(item)`.

---

## UI — `InventoryItemPreview`

Встроенный 3D-превью в правой части инвентаря. Всегда виден пока инвентарь открыт. Компонент крепится на `RawImage` (`PreviewImage`), который рендерит `RenderTexture` от выделенной камеры. Singleton (`Instance`).

### Как работает

1. В `Awake` создаётся `RenderTexture 512×512` (квадратная; камера `aspect = 1.0`)
2. `InventoryPreviewCamera` — `Orthographic`, culling mask = слой `Inspection`, при старте отключена
3. `Show(item)` — инстанциирует `item.inspectionPrefab` в точке `(500, -1000, 0)`, вычисляет bounds, создаёт пивот, применяет `previewRotation` (или глобальный `initialRotation`), включает камеру
4. Idle spin — если у предмета не стоят `disableIdleSpin`/`lockRotationY`; drag по `RawImage` — ручное вращение
5. Трёхточечный свет: key (2.0), fill (1.0), rim (1.5); отключается флагом `useThreePointLighting`
6. `Clear()` — уничтожает пивот и свет, отключает камеру, чистит текст

### Сцена

```
Canvas/InventoryPanel/
└── RightPanel
    ├── PreviewImage          # RawImage + AspectRatioFitter(FitInParent, 1:1) + InventoryItemPreview
    ├── ItemNameText          # TextMeshProUGUI — название предмета
    └── DescriptionText       # TextMeshProUGUI — описание предмета

InspectionSetup/
└── InventoryPreviewCamera    # Camera, изначально отключена; управляется скриптом
```

`AspectRatioFitter` на `PreviewImage` обязателен — без него квадратный `RenderTexture` растянется по форме панели.

### Параметры Inspector


| Поле                           | По умолчанию                                 | Описание                                                                                          |
| ------------------------------ | -------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Preview Camera`               | — (ищет `"InventoryPreviewCamera"` по имени) | Ссылка на камеру превью                                                                           |
| `Item Name Text`               | —                                            | `TextMeshProUGUI` для названия                                                                    |
| `Description Text`             | —                                            | `TextMeshProUGUI` для описания                                                                    |
| `Idle Spin Speed`              | `30`                                         | Скорость автовращения (°/сек)                                                                     |
| `Drag Rotation Speed`          | `0.4`                                        | Чувствительность ручного вращения                                                                 |
| `Initial Rotation`             | `(15, -35, 0)`                               | Глобальный начальный поворот. Применяется если у `ItemData` не включён `useCustomPreviewRotation` |
| `Inspection Layer Name`        | `Inspection`                                 | Слой рендера превью                                                                               |
| `Framing Multiplier`           | `2.2`                                        | Масштаб кадрирования. Больше — модель меньше                                                      |
| `Key/Fill/Rim Light Intensity` | `2 / 1 / 1.5`                                | Интенсивности трёхточечного света                                                                 |
| `Use Three Point Lighting`     | `true`                                       | Отключить — рендер только с key-светом (старый вид)                                               |


### Управление


| Действие             | Результат                      |
| -------------------- | ------------------------------ |
| ЛКМ на слоте         | Показать этот предмет в превью |
| ЛКМ + drag по превью | Ручное вращение модели         |


---

## UI — `DraggableItem`

Компонент на дочернем объекте `Icon` внутри слота. Реализует `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`. Требует `CanvasGroup`.

`OnBeginDrag`:

1. Обновляет `SourceSlot` через `GetComponentInParent<InventorySlot>()`
2. Отменяет drag если слот пуст (`eventData.pointerDrag = null`)
3. Перепривязывает `Icon` к корневому `Canvas` (чтобы иконка рисовалась поверх всего)
4. `CanvasGroup.blocksRaycasts = false` — пропускает raycast на слот под курсором

`OnDrag` двигает `transform.position` за курсором.

`OnEndDrag` возвращает `Icon` к `_originalParent`, сбрасывает позицию и `CanvasGroup`.

---

## UI — `InventoryHints`

Компонент на объекте `HintsBar` внутри `InventoryPanel`. Отображает строку подсказок управления внизу инвентаря.

### Сцена

```
Canvas/InventoryPanel/
├── LeftPanel
│   ├── TitleText           # Заголовок инвентаря
│   └── SlotsContainer      # GridLayoutGroup — сетка слотов
├── RightPanel
│   ├── PreviewImage        # Встроенный 3D-превью (InventoryItemPreview)
│   ├── ItemNameText        # Название выбранного предмета
│   └── DescriptionText     # Описание выбранного предмета
└── HintsBar                # Полная ширина, внизу панели; компонент InventoryHints
    └── HintsText           # TextMeshProUGUI — итоговый текст подсказок
```

### Параметры Inspector


| Поле            | Описание                                                                                    |
| --------------- | ------------------------------------------------------------------------------------------- |
| `Hints Label`   | Ссылка на `TextMeshProUGUI` объекта `HintsText`                                             |
| `Hints`         | Массив подсказок (`key` + `action`); дефолт: перемещение, 3D-осмотр, крафт, Tab/I — закрыть |
| `Hints Per Row` | Сколько подсказок на одну строку (по умолчанию 2)                                           |
| `Separator`     | Строка между клавишей и действием (по умолчанию `  —  `)                                    |
| `Column Gap`    | Строка-отступ между подсказками в одной строке                                              |


`BuildHintsText()` вызывается в `Start` и строит финальный текст с `<b>` тегами. Можно вызвать вручную если подсказки меняются в рантайме.

---

## UI — `RunePouchUI`

Singleton на иконке мешочка в панели инвентаря. Появляется, когда собрана хотя бы одна руна (подписка на `OnRunesChanged`). При наведении показывает через `ItemTooltip` счётчик «собрано / осталось». Когда собраны все руны — иконка меняется на мастер-эмблему и тултип читает «Мастер рун».

---

## Подбор предметов — `PickableItem`

Компонент на любом GameObject в мире. Требует `Collider`. Реализует `IInteractable` и `ISaveable`.

При взаимодействии игрока открывает панель инспекции (`ItemInspector.BeginInspection`). Если `ItemInspector` недоступен — добавляет предмет напрямую и уничтожает себя. Подробнее — [@ id="/Pages/Private/Pickable Items.md" label="Pickable Items"].

---

## Инспекция предметов — `ItemInspector`

Singleton на GameObject `InspectionSetup` в сцене. Показывает 3D-превью предмета через выделенную orthographic-камеру и `RenderTexture`. Поддерживает три режима:

- **Режим подбора** — открывается при взаимодействии с `PickableItem`. Показывает название и описание. Закрытие (`ConfirmPickup`) кладёт предмет в инвентарь (или вызывает `onPickup`-коллбэк) и уничтожает мировой объект. Отмены нет: `CancelInspection()` теперь просто вызывает `ConfirmPickup()` — предмет всегда забирается.
- **Режим превью** — открывается по ЛКМ на слоте инвентаря (`BeginPreview`). Название и описание скрыты. Закрытие не влияет на инвентарь.
- **Inspect-only режим** — `BeginWorldPreview(item, worldObject)`: читаемый просмотр мирового объекта без подбора. Показывает имя и описание, мировой объект остаётся в сцене, в инвентарь ничего не попадает.

Свойства: `IsInspecting` (панель открыта) и `HoldsDeferredResult` (панель держит отложенный результат крафта, существующий только здесь: `_onPickup != null && _worldObject == null` — закрытие обязано подтвердить, а пазл не должен выходить).

### Сцена

```
InspectionSetup            # GameObject с компонентом ItemInspector
├── InspectionCamera       # Orthographic camera → RenderTexture
Canvas/
└── InspectionPanel        # UI панель инспекции
    ├── PreviewFrame       # RectTransform рамки превью
    ├── PreviewImage       # RawImage + AspectRatioFitter(FitInParent, 1.5) → RenderTexture
    ├── InfoPanel
    │   ├── ItemNameText     # Скрыт в режиме превью
    │   └── DescriptionText  # Скрыт в режиме превью
    ├── TakeButton         # → ItemInspector.ConfirmPickup()
    └── CancelButton       # → ItemInspector.CancelInspection() (= ConfirmPickup)
```

### Публичные методы


| Метод                                                 | Описание                                                                                          |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `BeginInspection(item, worldObject, onPickup = null)` | Режим подбора. `onPickup` — опциональный коллбэк, заменяющий дефолтный `InventorySystem.AddItem`  |
| `BeginPreview(item)`                                  | Режим превью из инвентаря (ЛКМ на слоте)                                                          |
| `BeginWorldPreview(item, worldObject)`                | Inspect-only просмотр мирового объекта без подбора                                                |
| `ConfirmPickup()`                                     | Закрывает панель, добавляет предмет (или вызывает коллбэк), уничтожает мировой объект             |
| `CancelInspection()`                                  | Эквивалентен `ConfirmPickup()` — отмена больше не поддерживается                                  |
| `CancelPreviewIfActive()`                             | Закрывает превью/inspect-only без изменения инвентаря. Вызывается из `InventoryUI.CloseInventory` |


### Как работает

1. `PickableItem.Interact()` → `ItemInspector.BeginInspection(item, worldObject)`
2. Если `inspectionPrefab` пуст — предмет добавляется в инвентарь сразу, без панели
3. Инстанциируется prefab в точке `InspectionOrigin` `(0, -1000, 0)` — вне видимости основной камеры
4. По bounds всех `Renderer` вычисляется геометрический центр модели
5. Создаётся `InspectionPivot` в центре bounds; модель парентится к нему; применяется начальный поворот
6. Камера orthographic, `orthographicSize = maxSize * framingMultiplier * 0.5`
7. Scale-in анимация (`scaleInDuration`, локскейл от нуля) + idle spin

### Технические детали

- `RenderTexture` фиксированный — `1536×1024` (формат 3:2), камера `aspect = 1.5`; на `PreviewImage` обязателен `AspectRatioFitter` (FitInParent, ratio 1.5)
- Камера — `Orthographic`, без HDR, `ClearFlags = SolidColor`, прозрачный фон `(0,0,0,0)`
- Culling mask камеры ограничен слоем `Inspection` (`inspectionLayerName`) — модель невидима основной камере
- Idle spin — постоянная скорость `idleSpinSpeed` (°/сек), сбрасывается при ручном drag
- Drag-порог `25 px` отличает клик от вращения; при открытии во время зажатой ЛКМ ввод игнорируется до отпускания кнопки (`_waitForMouseRelease`)
- Панель открывается через `UIManager.OpenPanel(inspectionPanel, CursorLockMode.Confined)`
- При закрытии текстовые поля восстанавливаются через `SetActive(true)`, пивот уничтожается вместе с инстансом
- Звук подбора: `pickupClip` через `AudioManager.PlaySFX`

### Параметры Inspector


| Поле                                  | По умолчанию   | Описание                                                                                          |
| ------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------- |
| `Inspection Camera`                   | —              | Ссылка на камеру инспекции                                                                        |
| `Inspection Panel`                    | —              | Панель инспекции (открывается через UIManager)                                                    |
| `Preview Frame`                       | —              | RectTransform рамки превью                                                                        |
| `Preview Image`                       | —              | RawImage с AspectRatioFitter (1.5)                                                                |
| `Item Name Text` / `Description Text` | —              | Текстовые поля (скрываются в режиме превью)                                                       |
| `Rotation Speed`                      | `180`          | Скорость ручного вращения (градус/сек)                                                            |
| `Initial Rotation`                    | `(15, -35, 0)` | Глобальный начальный поворот. Применяется если у `ItemData` не включён `useCustomPreviewRotation` |
| `Scale In Duration`                   | `0.45`         | Длительность scale-in анимации открытия (сек)                                                     |
| `Idle Spin Speed`                     | `40`           | Скорость автовращения (°/сек)                                                                     |
| `Inspection Layer Name`               | `Inspection`   | Слой рендера                                                                                      |
| `Framing Multiplier`                  | `2.2`          | Чем больше — тем меньше модель в кадре                                                            |
| `Pickup Clip`                         | —              | Звук подбора                                                                                      |


### Управление в режиме подбора


| Действие                    | Результат              |
| --------------------------- | ---------------------- |
| ЛКМ + drag                  | Ручное вращение модели |
| Кнопка Take / подтверждение | Подобрать предмет      |


### Управление в режиме превью (из инвентаря)


| Действие         | Результат              |
| ---------------- | ---------------------- |
| ЛКМ + drag       | Ручное вращение модели |
| ПКМ / E / Escape | Закрыть превью         |


### Требования к `ItemData`

Поле `inspectionPrefab` должно быть заполнено. Если оно пустое — предмет подбирается без показа инспекции.

---

## Физическое перетаскивание — `PhysicsDraggable` + `PhysicsGrabber`

Система позволяет игроку тянуть объекты в мире через физику — удерживая ЛКМ и двигая мышью. Масса объекта определяет ощущение веса. Полное описание — [@ id="/Pages/Private/Drag Interaction System.md" label="Drag Interaction System"].

### Настройка объекта

Каждый перетаскиваемый объект требует:

1. Компонент `PhysicsDraggable` — маркер, настройки поведения и drag/impact-аудио
2. Компонент `Rigidbody` — масса влияет на скорость реакции и замедление игрока
3. Слой **Draggable** — иначе SphereCast не попадёт
4. `Static` флаг — **снять**, иначе физика не работает
5. Для `MeshCollider` — включить **Convex**

### Ключевые параметры

`PhysicsDraggable`: `Drag Hint` (по умолчанию `«Тянуть»`), `Prevent Tipping` (заморозка вращения X/Z при drag), `Max Drag Distance` и `Hold Distance` (персональные дистанции захвата/удержания, 0 = глобальные), плюс блоки drag-аудио (scrape-луп) и impact-аудио (звуки столкновений).

`PhysicsGrabber` (на `Player`): `Camera Transform`, `Grab Distance 3`, `Detection Radius 0.08`, `Hold Distance 2`, `Spring Strength 200`, `Spring Damping 20`, `Angular Damping Factor 8`, `Max Velocity 8`, `Grab Linear Drag 5`, `Reference Heavy Mass 20`, `Min Speed Multiplier 0.4`, `Acceptable Gap 0.6`, `Max Gap 1.5`.

### Как работает скорость игрока

```
итоговый_множитель = massMultiplier × gapFactor
```

- `massMultiplier` — постоянный, задаётся при захвате: `Lerp(1.0, minSpeedMultiplier, mass / referenceHeavyMass)`
- `gapFactor` — динамический: падает до `0`, когда разрыв между объектом и hold point достигает `maxGap`

При отпускании скорость немедленно возвращается к `1.0`.

### Ограничения и правила

- Бег (`Sprint`) блокирует захват; начавшийся бег отпускает объект
- Захват блокируется при открытом инвентаре (`Cursor.lockState == None`)
- CCD включается на время drag'а и закрепляется за последней перенесённой вещью (см. Drag Interaction System)

---

## Утилита — `MissingScriptCleaner`

Editor-only скрипт. Меню: **Tools → Remove Missing Scripts**.

Рекурсивно проходит по всей иерархии загруженных сцен, удаляет компоненты с отсутствующим скриптом через `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` и сохраняет сцену.

---

## Панель инвентаря для пазлов — `PuzzleInventoryBar`

Горизонтальный бар, появляющийся внизу экрана при входе в режим пазла. Показывает весь инвентарь игрока в виде прокручиваемой ленты. Предметы из бара можно перетаскивать на объекты пазла — при этом пазл сам решает, принять ли предмет.

### Ключевой принцип — CanvasGroup вместо SetActive

GameObject `PuzzleInventoryBar` **всегда остаётся активным** в иерархии. Видимость управляется через `CanvasGroup` (`[RequireComponent(typeof(CanvasGroup))]`):

```csharp
// SetBarVisible(true)  → alpha=1, interactable=true, blocksRaycasts=true
// SetBarVisible(false) → alpha=0, interactable=false, blocksRaycasts=false
```

Это гарантирует, что `Awake()` и `Start()` всегда выполняются при загрузке сцены и `PuzzleInventoryBar.Instance` всегда доступен для пазлов. Если использовать `SetActive(false)` в `Awake`, `Start()` не запустится и синглтон останется `null`.

### Статическое состояние drag

```csharp
public static bool IsDragging { get; }         // игрок тянет предмет из бара
public static ItemData DraggedItem { get; }    // текущий перетаскиваемый предмет
public static int DragSourceSlotIndex { get; } // слот-источник (-1 когда drag не идёт)
```

`DragSourceSlotIndex` читают пазлы (например `ChemicalSynthesisController`), чтобы вернуть результат в нужный слот.

### Отображение слотов

Бар всегда показывает ровно `MaxSlots` ячеек (или `visibleSlotCount` без прокрутки). Пустые ячейки видны как фон без иконки. Иконка включается/выключается через `Image.enabled`.

Внутри используются два счётчика:

- `_activeSlotCount` — всегда равен `inv.MaxSlots`, определяет сколько слотов создано
- `_filledSlotCount` — число ячеек с предметами, управляет кнопками прокрутки

Кнопки прокрутки активны только когда `_filledSlotCount > visibleSlotCount`.

### `IPuzzleDropHandler`, `IPuzzleDropTarget`, `IPuzzleDropHoverHandler`

```csharp
public interface IPuzzleDropHandler
{
    /// <summary>
    /// Попытка разместить перетаскиваемый предмет в позиции курсора.
    /// Бар сам управляет инвентарём — реализациям НЕЛЬЗЯ вызывать InventorySystem напрямую.
    /// </summary>
    /// <param name="replacement">
    /// Когда не null — бар заменяет перетаскиваемый предмет этим вместо удаления.
    /// Используется, чтобы вернуть пустую тару (например пустую колбу), когда устройство забрало содержимое.
    /// </param>
    /// <returns>true — предмет принят; false — возвращается в бар.</returns>
    bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement);
}
```

`IPuzzleDropTarget` — для 3D-целей дропа: `GetDropHint()` (текст подсказки при hover) и `CanAccept(item)` (совместимость для превью). `IPuzzleDropHoverHandler` — опциональная подсветка: `SetDragHover(bool isHovering)` вызывается при начале/окончании hover совместимым предметом (реализуется на том же компоненте, что и `IPuzzleDropTarget`).

### Жизненный цикл

```
Пазл открывается
  └─ PuzzleInventoryBar.Instance.Show(this)
       ├─ Регистрирует пазл как текущий IPuzzleDropHandler
       ├─ Заполняет слоты из InventorySystem (все MaxSlots ячеек)
       └─ SetBarVisible(true) → бар становится видимым

Игрок перетаскивает предмет
  ├─ Бросил на слот бара → PuzzleInventorySlot.OnDrop → bar.OnSlotDropReceived(...)
  │    (только ЗАПИСЫВАЕТ намерение — swap/craft откладывается до OnEndDrag,
  │     чтобы дроп на 3D-устройство имел приоритет, если курсор и над ним)
  └─ OnEndDrag (PuzzleInventoryBar)
       ├─ Панель инспекции открыта → дроп блокируется, визуал восстанавливается
       ├─ 1. HandleDrop(item, pos, out replacement) — приоритет у устройства
       │      ├─ true + replacement != null → InventorySystem.ReplaceItem(dragged, replacement)
       │      ├─ true + replacement == null → InventorySystem.ClearSlot(sourceIndex) + компакция
       │      └─ false → шаг 2
       ├─ 2. Был дроп на слот бара → ExecutePendingSlotDrop()
       │      ├─ Оба заняты + рецепт найден → TryCombineDeferred
       │      │    → ItemInspector.BeginInspection(result, null, onPickup)
       │      │      onPickup → PlaceItemAt(originSlot) + Compact()
       │      └─ Рецепта нет → SwapSlots (в т.ч. в пустой слот)
       └─ 3. Ничего не приняло → визуал восстанавливается, звук _wrongItemSound

Пазл закрывается
  └─ PuzzleInventoryBar.Instance.Hide()
       └─ SetBarVisible(false) → бар скрыт, handler сброшен
```

### Как интегрировать пазл с баром

1. Реализовать `IPuzzleDropHandler` на контроллере пазла:

```csharp
public class MyPuzzleController : MonoBehaviour, IInteractable, IPuzzleDropHandler
{
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        if (!IsAccepted(item)) return false;
        // Raycast или другая логика размещения
        return true;
    }
}
```

2. Вызвать `Show` / `Hide` при открытии и закрытии пазла:

```csharp
private void Open()  => PuzzleInventoryBar.Instance?.Show(this);
private void Close() => PuzzleInventoryBar.Instance?.Hide();
```

### Параметры Inspector — `PuzzleInventoryBar`


| Поле                       | Описание                                                             |
| -------------------------- | -------------------------------------------------------------------- |
| `Slot Content`             | Transform контейнера слотов внутри вьюпорта (RectMask2D на вьюпорте) |
| `Slot Prefab`              | Префаб одного слота (`PuzzleInventorySlot`)                          |
| `Scroll Left/Right Button` | Кнопки прокрутки. Неактивны когда предметов ≤ `visibleSlotCount`     |
| `Visible Slot Count`       | Сколько слотов видно без прокрутки (по умолчанию 5)                  |
| `Wrong Item Sound`         | Звук при отказе устройства или неудачном swap                        |
| `Slot Size`                | Размер ячейки в пикселях (80)                                        |
| `Slot Spacing`             | Отступ между ячейками (8)                                            |
| `Ghost Size`               | Размер иконки-призрака при перетаскивании (64)                       |
| `Bar Vertical Padding`     | Высота бара = `slotSize + barVerticalPadding` (30)                   |
| `Icon Padding`             | Отступ иконки от края ячейки (4; 0 = заполнить полностью)            |


### Фильтрация предметов

Каждый пазл сам отвечает за фильтрацию в `HandleDrop`. Стандартный паттерн — проверка предмета против массива разрешённых `ItemData`:

```csharp
[SerializeField] private ItemData[] _acceptedItems;

public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
{
    replacement = null;
    if (System.Array.IndexOf(_acceptedItems, item) < 0) return false;
    // ...
    return true;
}
```

Примеры реализаций:

- `MedallionBoxUI` (`Assets/Scripts/Puzzle/Medallion/MedallionBoxUI.cs`) — принимает только медальоны, определяет лунку через raycast. При возврате медальона проверяет `IsFull` перед `hole.Retrieve()`.
- `ElectricPuzzleController` — принимает предохранитель, определяет зону вставки через raycast на `_fuseAnchorCollider`.

---

## Как добавить новый предмет

1. **Assets > Create > Inventory > Item Data** — заполнить `itemName`, `icon`, `inspectionPrefab`
2. Положить ассет в `Assets/Data/Items/` — `InventoryAutoPopulate` подхватит его автоматически
3. Открыть созданный `ItemData` в Inspector — появится 3D-виджет. Drag внутри виджета, чтобы повернуть модель к читаемому ракурсу; углы запишутся автоматически
4. Создать GameObject в сцене, добавить `PickableItem`, назначить новый `ItemData`

## Как добавить рецепт крафта

1. **Assets > Create > Inventory > Crafting Recipe** — указать два ингредиента и результат (опционально `secondaryResult`, `conserveIngredientA/B`)
2. Положить ассет в `Assets/Data/Recipes/` — `InventoryAutoPopulate` подхватит его автоматически
