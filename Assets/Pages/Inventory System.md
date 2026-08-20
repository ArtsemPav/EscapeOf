Инвентарь — это три независимых слоя: **данные** (ScriptableObjects), **логика** (`InventorySystem`) и **UI** (`InventoryUI` + слоты). Слои общаются через событие `OnInventoryChanged` — UI не знает о логике, логика не знает о UI.

Для новичков — начни с [@ id="/Pages/Private/Quick Start.md" label="Quick Start"], затем [@ id="/Pages/Private/Pickable Items.md" label="Pickable Items"].

---

## Структура файлов

```
Assets/Scripts/Inventory/
├── ItemDataSO.cs              # ScriptableObject — описание одного предмета
├── CraftingRecipe.cs          # ScriptableObject — правило крафта
├── InventoryCondition.cs      # ScriptableObject — условие наличия предмета в инвентаре
├── InventorySystem.cs         # Singleton — вся логика инвентаря
├── InventoryAudio.cs          # Звуковые эффекты инвентаря (открытие/закрытие/крафт)
├── ItemInspector.cs           # Singleton — 3D-инспекция предметов (подбор и превью)
├── PickableItem.cs            # Компонент на объекте в мире — подбор предмета
└── UI/
    ├── InventoryUI.cs         # Открытие/закрытие панели, создание слотов
    ├── InventorySlot.cs       # Один слот — отображение + drop-зона + ЛКМ-превью
    ├── DraggableItem.cs       # Иконка предмета — drag-and-drop поведение
    ├── InventoryHints.cs      # Панель подсказок управления внизу инвентаря
    ├── InventoryItemPreview.cs # Встроенный 3D-превью в правой части инвентаря
    ├── InventoryBackdrop.cs   # Закрытие инвентаря кликом вне панели
    └── ItemTooltip.cs         # Тултип предмета при наведении на слот

Assets/Scripts/Puzzle/Shared/
├── IPuzzleDropHandler.cs      # Интерфейс для пазлов, принимающих предметы из бара
├── PuzzleInventoryBar.cs      # Горизонтальный бар внизу экрана с прокруткой
└── PuzzleInventorySlot.cs     # Один слот в баре (drag + tooltip)

Assets/Scripts/Interaction/
└── PhysicsDraggable.cs    # Маркер-компонент на перетаскиваемых объектах

Assets/Scripts/Player/
├── FPSController.cs       # Контроллер персонажа
└── PhysicsGrabber.cs      # Физическое перетаскивание объектов мышью

Assets/Scripts/Editor/
├── MissingScriptCleaner.cs  # Утилита: Tools → Remove Missing Scripts
└── ItemDataEditor.cs        # Кастомный Inspector для ItemData — интерактивный 3D-превью

Assets/Data/Items/         # ItemData ассеты
Assets/Data/Recipes/       # CraftingRecipe ассеты
```

---

## Данные

### `ItemData` (ScriptableObject)

Создаётся через **Assets > Create > Inventory > Item Data**.


| Поле                       | Описание                                                                                                                                              |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `itemName`                 | Название предмета                                                                                                                                     |
| `description`              | Описание (показывается в тултипе)                                                                                                                     |
| `icon`                     | Иконка для слота инвентаря                                                                                                                            |
| `inspectionPrefab`         | Prefab для 3D-просмотра. Если пустое — предмет подбирается напрямую без инспекции. Также используется как 3D-модель монеты в лунках шкатулки          |
| `consumeOnUse`             | Если включено — предмет удаляется из инвентаря после использования. По умолчанию `true`                                                               |
| `useCustomPreviewRotation` | Если включено — начальный ракурс превью берётся из `previewRotation` вместо глобального `initialRotation`                                             |
| `previewRotation`          | Euler-углы начального поворота в 3D-превью (инспекция и инвентарь). Активно только при `useCustomPreviewRotation = true`. По умолчанию `(15, -35, 0)` |


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

Создаётся через **Assets > Create > Inventory > Crafting Recipe**.

Порядок ингредиентов не важен — рецепт работает в обе стороны.

---

## Логика — `InventorySystem`

Singleton на GameObject в сцене. Хранит `ItemData[] _slots` — массив фиксированного размера.

**Компакция слотов** — предметы всегда упакованы влево. Пустых ячеек между предметами нет. При удалении или крафте вызывается `CompactSlots()`, который сдвигает все непустые элементы к индексу 0. `AddItem()` добавляет предмет в первый свободный слот, что при компакции всегда означает позицию сразу правее последнего предмета.

### Событие

```csharp
public event Action OnInventoryChanged;
```

Стреляет после каждого изменения массива `_slots`. UI подписывается в `Start`, отписывается в `OnDisable`.

### Публичные методы


| Метод / Свойство                        | Описание                                                                                                 |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `bool IsFull`                           | `true` когда все слоты заняты                                                                            |
| `bool AddItem(item)`                    | Добавляет в первый свободный слот. Возвращает `false` если инвентарь полон — предмет **не** уничтожается |
| `bool RemoveItem(item)`                 | Удаляет предмет и вызывает `CompactSlots()`                                                              |
| `ItemData GetItemAt(i)`                 | Предмет по индексу слота, или `null`                                                                     |
| `bool HasItem(item)`                    | Проверяет наличие предмета                                                                               |
| `void SwapSlots(a, b)`                  | Меняет местами два слота (drag-and-drop в таб-инвентаре)                                                 |
| `bool TryCombine(src, tgt, out result)` | Крафт: ищет рецепт, кладёт результат в `tgt`, очищает `src`, вызывает `CompactSlots()`                   |
| `void ClearAll()`                       | Очищает все слоты (сброс сохранения)                                                                     |


### Защита от переполнения

`AddItem()` возвращает `bool`. Все три пути подбора (`PickableItem.Interact`, `ItemInspector.BeginInspection` без prefab, `ItemInspector.ConfirmPickup`) проверяют результат перед уничтожением мирового объекта — предмет остаётся в мире если места нет.

`MedallionBoxUI.TryRetrieveFromHole()` проверяет `IsFull` **до** вызова `hole.Retrieve()` — медальон не пропадёт при попытке вернуть его в заполненный инвентарь.

### Инспектор


| Поле        | Описание                                                                             |
| ----------- | ------------------------------------------------------------------------------------ |
| `Max Slots` | Максимальное число слотов                                                            |
| `_allItems` | Скрыт (`[HideInInspector]`). Заполняется автоматически — см. `InventoryAutoPopulate` |
| `recipes`   | Скрыт (`[HideInInspector]`). Заполняется автоматически — см. `InventoryAutoPopulate` |


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

Singleton (`Instance`). Управляет панелью инвентаря и связанными объектами. Создаёт слоты один раз в `Start`, потом только обновляет их содержимое.

**Открытие/закрытие** — кнопка из Input System (`Player.Inventory`). При открытии:

- Активирует `inventoryBackdrop` (прозрачный экран за панелью)
- Показывает `inventoryPanel`
- Снимает блокировку курсора
- Отключает ввод игрока (`FPSController.SetPlayerInputEnabled(false)`)
- Автоматически показывает первый предмет инвентаря в 3D-превью (`InventoryItemPreview.Show`)

При закрытии — деактивирует backdrop, очищает превью (`InventoryItemPreview.Clear`) и завершает любой активный `ItemInspector.CancelPreviewIfActive()`.

`CloseInventory()` — публичный метод. Вызывается извне (например из `InventoryBackdrop`). Безопасно игнорирует повторный вызов если инвентарь уже закрыт.

`RefreshSlots()` — вызывается при `OnInventoryChanged`. Проходит по всем слотам и вызывает `slot.Setup(GetItemAt(i))`.

### Инспектор


| Поле                 | Описание                                             |
| -------------------- | ---------------------------------------------------- |
| `Inventory Panel`    | GameObject панели инвентаря                          |
| `Inventory Backdrop` | GameObject полноэкранного фона (`InventoryBackdrop`) |
| `Slot Prefab`        | Префаб одного слота                                  |
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

Один слот в сетке. Всегда видим как фон. Иконка (`Image`) включается/выключается через `Image.enabled` — сам GameObject остаётся активным, чтобы `DraggableItem` работал в любом состоянии.

`OnDrop` — точка входа для drag-and-drop:

```
Предмет брошен на слот
├── Оба слота заняты → TryCombine(source, target)
│   ├── Рецепт найден → результат в target, source очищается
│   │                   InventoryItemPreview.Show(craftedItem) — превью обновляется сразу
│   └── Рецепта нет  → SwapSlots (предметы меняются местами)
└── Один слот пуст  → SwapSlots (предмет переезжает)
```

`OnPointerClick (ЛКМ)` — показывает встроенный 3D-превью предмета через `InventoryItemPreview.Show(item)`. Перед открытием скрывает тултип.

---

## UI — `InventoryItemPreview`

Встроенный 3D-превью в правой части инвентаря. Всегда виден пока инвентарь открыт — отдельная панель не требуется. Компонент крепится на `RawImage` (`PreviewImage`), который рендерит `RenderTexture` от выделенной камеры.

### Как работает

1. В `Awake` создаётся `RenderTexture 512×512` (квадратная, чтобы не было искажений)
2. `InventoryPreviewCamera` настраивается как `Orthographic`, `aspect = 1.0`, culling mask = слой `Inspection`
3. `Show(item)` — инстанциирует `item.inspectionPrefab` в точке `(500, -1000, 0)`, вычисляет bounds, создаёт пивот, применяет `initialRotation`, включает камеру
4. Модель автоматически вращается (`idleSpinSpeed`). Drag по `RawImage` — ручное вращение
5. `Clear()` — уничтожает пивот и свет, отключает камеру

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


| Поле                  | По умолчанию   | Описание                                                                                          |
| --------------------- | -------------- | ------------------------------------------------------------------------------------------------- |
| `Preview Camera`      | —              | Ссылка на `InventoryPreviewCamera`                                                                |
| `Item Name Text`      | —              | `TextMeshProUGUI` для названия                                                                    |
| `Description Text`    | —              | `TextMeshProUGUI` для описания                                                                    |
| `Idle Spin Speed`     | `30`           | Скорость автовращения (°/сек)                                                                     |
| `Drag Rotation Speed` | `0.4`          | Чувствительность ручного вращения                                                                 |
| `Initial Rotation`    | `(15, -35, 0)` | Глобальный начальный поворот. Применяется если у `ItemData` не включён `useCustomPreviewRotation` |
| `Framing Multiplier`  | `2.2`          | Масштаб кадрирования. Больше — модель меньше                                                      |


### Управление


| Действие             | Результат                      |
| -------------------- | ------------------------------ |
| ЛКМ на слоте         | Показать этот предмет в превью |
| ЛКМ + drag по превью | Ручное вращение модели         |


---

## UI — `DraggableItem`

Компонент на дочернем объекте `Icon` внутри слота. Реализует `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`.

`**OnBeginDrag**:**

1. Обновляет `SourceSlot` через `GetComponentInParent<InventorySlot>()`
2. Отменяет drag если слот пуст (`eventData.pointerDrag = null`)
3. Перепривязывает `Icon` к корневому `Canvas` (чтобы иконка рисовалась поверх всего)
4. `CanvasGroup.blocksRaycasts = false` — пропускает raycast на слот под курсором

`**OnDrag`:** двигает `transform.position` за курсором.

`**OnEndDrag`:** возвращает `Icon` к `_originalParent`, сбрасывает позицию и `CanvasGroup`.

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


| Поле            | Описание                                                  |
| --------------- | --------------------------------------------------------- |
| `Hints Label`   | Ссылка на `TextMeshProUGUI` объекта `HintsText`           |
| `Hints`         | Массив подсказок: каждая запись содержит `key` и `action` |
| `Hints Per Row` | Сколько подсказок на одну строку (по умолчанию 2)         |
| `Separator`     | Строка между клавишей и действием (по умолчанию `—`)      |
| `Column Gap`    | Отступ между подсказками в одной строке                   |


`BuildHintsText()` вызывается в `Start` и строит финальный текст с `<b>` тегами. Можно вызвать вручную если подсказки меняются в рантайме.

---

## Подбор предметов — `PickableItem`

Компонент на любом GameObject в мире. Требует `Collider`. Реализует `IInteractable`.

При взаимодействии игрока открывает панель инспекции (`ItemInspector.BeginInspection`). Если `ItemInspector` недоступен — добавляет предмет напрямую и уничтожает себя.

---

## Инспекция предметов — `ItemInspector`

Singleton на GameObject `InspectionSetup` в сцене. Показывает 3D-превью предмета. Поддерживает два режима:

- **Режим подбора** — открывается при взаимодействии с `PickableItem` в мире. Показывает название, описание. Закрытие кладёт предмет в инвентарь и уничтожает мировой объект.
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


| Метод                                | Описание                                                                             |
| ------------------------------------ | ------------------------------------------------------------------------------------ |
| `BeginInspection(item, worldObject)` | Режим подбора. Вызывается из `PickableItem`                                          |
| `BeginPreview(item)`                 | Режим превью из инвентаря. ПКМ на слоте                                              |
| `ConfirmPickup()`                    | Добавляет предмет в инвентарь и закрывает панель                                     |
| `CancelPreviewIfActive()`            | Закрывает превью без изменения инвентаря. Вызывается из `InventoryUI.CloseInventory` |


### Как работает

1. `PickableItem.Interact()` → `ItemInspector.BeginInspection(item, worldObject)`
2. Инстанциируется `item.inspectionPrefab` в точке `InspectionOrigin` (y = -1000) — вне видимости основной камеры
3. По bounds всех `Renderer` вычисляется геометрический центр модели
4. Создаётся `InspectionPivot` в центре bounds; модель парентится к нему
5. К пивоту применяется `initialRotation` — начальный ракурс 3/4
6. Камера настраивается orthographic, `orthographicSize` = `maxSize * framingMultiplier * 0.5`
7. Запускается idle spin — модель плавно поворачивается

### Управление в режиме подбора


| Действие   | Результат              |
| ---------- | ---------------------- |
| ЛКМ + drag | Ручное вращение модели |
| E / Escape | Подобрать предмет      |


### Управление в режиме превью (из инвентаря)


| Действие         | Результат              |
| ---------------- | ---------------------- |
| ЛКМ + drag       | Ручное вращение модели |
| ПКМ / E / Escape | Закрыть превью         |


### Параметры Inspector


| Поле                | По умолчанию   | Описание                                                                                          |
| ------------------- | -------------- | ------------------------------------------------------------------------------------------------- |
| `inspectionCamera`  | —              | Ссылка на камеру инспекции                                                                        |
| `framingMultiplier` | `2.2`          | Чем больше — тем меньше модель в кадре                                                            |
| `rotationSpeed`     | `180`          | Скорость ручного вращения (градус/сек)                                                            |
| `initialRotation`   | `(15, -35, 0)` | Глобальный начальный поворот. Применяется если у `ItemData` не включён `useCustomPreviewRotation` |
| `idleSpinDuration`  | `1.8`          | Длительность вступительной анимации (сек)                                                         |
| `idleSpinSpeed`     | `80`           | Пиковая скорость idle spin (градус/сек)                                                           |


### Технические детали

- Камера — `Orthographic`, без HDR, `ClearFlags = SolidColor`, прозрачный фон `(0,0,0,0)`
- `RenderTexture` создаётся в `Awake` под размер экрана (`Screen.width × Screen.height`)
- Culling mask камеры ограничен слоем `"Inspection"` — модель невидима основной камерой
- Idle spin использует `Mathf.Cos(t * π/2)` для ease-out затухания
- `worldPositionStays: true` при парентинге → `initialRotation` применяется после `SetParent`
- При закрытии `_inspectionPivot` уничтожается вместе с дочерним instance
- `ItemNameText` и `DescriptionText` восстанавливаются через `SetActive(true)` при закрытии — чтобы режим подбора снова показывал их корректно

### Требования к `ItemData`

Поле `inspectionPrefab` должно быть заполнено. Если оно пустое — предмет подбирается без показа инспекции.

---

## Физическое перетаскивание — `PhysicsDraggable` + `PhysicsGrabber`

Система позволяет игроку тянуть объекты в мире через физику — удерживая клавишу взаимодействия и двигая мышью. Масса объекта определяет ощущение веса.

### Настройка объекта

Каждый перетаскиваемый объект требует:

1. Компонент `PhysicsDraggable` — маркер и настройки поведения
2. Компонент `Rigidbody` — масса влияет на скорость реакции и замедление игрока
3. Слой **Draggable** — иначе raycast не попадёт
4. `Static` флаг — **снять**, иначе физика не работает
5. Для `MeshCollider` — включить **Convex**

### Параметры `PhysicsDraggable`


| Поле              | По умолчанию   | Описание                         |
| ----------------- | -------------- | -------------------------------- |
| `Drag Hint`       | `"[E] Тянуть"` | Текст подсказки при наведении    |
| `Prevent Tipping` | `false`        | Запрещает объекту опрокидываться |


Если `Prevent Tipping` включён — каждый `FixedUpdate` обнуляет X/Z угловую скорость и принудительно выравнивает вращение через `Body.MoveRotation`. Это надёжнее одних `FreezeRotation` constraints при высокоскоростных столкновениях.

### Настройка Player

Компонент `PhysicsGrabber` на GameObject `Player`:


| Поле                   | По умолчанию | Описание                                                             |
| ---------------------- | ------------ | -------------------------------------------------------------------- |
| `Camera Transform`     | —            | Ссылка на камеру персонажа                                           |
| `Grab Distance`        | `3`          | Дистанция захвата (м)                                                |
| `Detection Radius`     | `0.08`       | Радиус SphereCast для обнаружения — больше значение, стабильнее хинт |
| `Hold Distance`        | `2`          | Расстояние удержания перед камерой (м)                               |
| `Spring Strength`      | `200`        | Сила пружины. Больше — быстрее тянется                               |
| `Spring Damping`       | `20`         | Затухание. Больше — меньше раскачки                                  |
| `Max Velocity`         | `8`          | Ограничение скорости объекта (м/с)                                   |
| `Grab Linear Drag`     | `5`          | Drag во время удержания                                              |
| `Reference Heavy Mass` | `20`         | Масса, при которой скорость = `Min Speed Multiplier`                 |
| `Min Speed Multiplier` | `0.4`        | Минимальный множитель скорости при тяжёлом объекте                   |
| `Acceptable Gap`       | `0.6`        | Зазор до hold point, при котором нет дополнительного замедления      |
| `Max Gap`              | `1.5`        | Зазор, при котором игрок останавливается полностью                   |


### Как работает скорость игрока

Скорость вычисляется каждый кадр как произведение двух факторов:

```
итоговый_множитель = massMultiplier × gapFactor
```

- `massMultiplier` — постоянный, задаётся при захвате: `Lerp(1.0, minSpeedMultiplier, mass / referenceHeavyMass)`
- `gapFactor` — динамический: падает до `0` когда разрыв между объектом и hold point достигает `maxGap`. Это не даёт игроку убежать вперёд, оставив объект позади

При отпускании объекта скорость немедленно возвращается к `1.0`.

### Ограничения и правила

- Бег (`Sprint`) блокирует захват. Если игрок начинает бежать во время тяги — объект отпускается
- Захват блокируется при открытом инвентаре (`Cursor.lockState == None`)
- `Rigidbody.collisionDetectionMode` должен быть `ContinuousSpeculative` для объектов с высокой скоростью

---

## Утилита — `MissingScriptCleaner`

Editor-only скрипт. Меню: **Tools → Remove Missing Scripts**.

Рекурсивно проходит по всей иерархии загруженных сцен, удаляет компоненты с отсутствующим скриптом через `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` и сохраняет сцену.

---

## Панель инвентаря для пазлов — `PuzzleInventoryBar`

Горизонтальный бар, появляющийся внизу экрана при входе в режим пазла. Показывает весь инвентарь игрока в виде прокручиваемой ленты. Предметы из бара можно перетаскивать на объекты пазла — при этом пазл сам решает, принять ли предмет.

### Ключевой принцип — CanvasGroup вместо SetActive

GameObject `PuzzleInventoryBar` **всегда остаётся активным** в иерархии. Видимость управляется через `CanvasGroup`:

```csharp
// SetBarVisible(true)  → alpha=1, interactable=true, blocksRaycasts=true
// SetBarVisible(false) → alpha=0, interactable=false, blocksRaycasts=false
```

Это гарантирует, что `Awake()` и `Start()` всегда выполняются при загрузке сцены и `PuzzleInventoryBar.Instance` всегда доступен для пазлов. Если использовать `SetActive(false)` в `Awake`, `Start()` не запустится и синглтон останется `null`.

### Отображение слотов

Бар всегда показывает ровно `MaxSlots` ячеек (или `visibleSlotCount` без прокрутки). Пустые ячейки видны как фон без иконки. Иконка включается/выключается через `Image.enabled` — сам GameObject слота остаётся активным.

Внутри используются два счётчика:

- `_activeSlotCount` — всегда равен `inv.MaxSlots`, определяет сколько слотов создано
- `_filledSlotCount` — число ячеек с предметами, управляет кнопками прокрутки

Кнопки прокрутки активны только когда `_filledSlotCount > visibleSlotCount`.

### `IPuzzleDropHandler`

Интерфейс, который реализует пазл, принимающий предметы из бара:

```csharp
public interface IPuzzleDropHandler
{
    /// <summary>
    /// Вызывается когда игрок бросает предмет из бара.
    /// Возвращает true — предмет принят и удаляется из инвентаря.
    /// Возвращает false — предмет возвращается в бар.
    /// </summary>
    bool HandleDrop(ItemData item, Vector2 screenPosition);
}
```

### Жизненный цикл

```
Пазл открывается
  └─ PuzzleInventoryBar.Instance.Show(this)
       ├─ Регистрирует пазл как текущий IPuzzleDropHandler
       ├─ Заполняет слоты из InventorySystem._slots (все MaxSlots ячеек)
       └─ SetBarVisible(true) → бар становится видимым

Игрок перетаскивает предмет из бара
  └─ PuzzleInventorySlot.OnEndDrag()
       └─ handler.HandleDrop(item, screenPosition)
            ├─ true  → InventorySystem.RemoveItem() → CompactSlots() → RefreshSlots()
            └─ false → предмет возвращается на своё место в баре

Пазл закрывается
  └─ PuzzleInventoryBar.Instance.Hide()
       └─ SetBarVisible(false) → бар скрыт, handler сброшен
```

### Как интегрировать пазл с баром

1. Реализовать `IPuzzleDropHandler` на контроллере пазла:

```csharp
public class MyPuzzleController : MonoBehaviour, IInteractable, IPuzzleDropHandler
{
    public bool HandleDrop(ItemData item, Vector2 screenPosition)
    {
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


| Поле                                     | Описание                                                         |
| ---------------------------------------- | ---------------------------------------------------------------- |
| `slotPrefab`                             | Префаб одного слота (`PuzzleInventorySlot`)                      |
| `slotContent`                            | Transform контейнера слотов внутри вьюпорта                      |
| `scrollLeftButton` / `scrollRightButton` | Кнопки прокрутки. Неактивны когда предметов ≤ `visibleSlotCount` |
| `visibleSlotCount`                       | Сколько слотов видно без прокрутки                               |
| `slotSize`                               | Размер ячейки в пикселях                                         |
| `slotSpacing`                            | Отступ между ячейками                                            |
| `ghostSize`                              | Размер иконки-призрака при перетаскивании                        |
| `barVerticalPadding`                     | Высота бара = `slotSize + barVerticalPadding`                    |
| `iconPadding`                            | Отступ иконки от края ячейки (0 = заполнить полностью)           |


### Фильтрация предметов

Каждый пазл сам отвечает за фильтрацию в `HandleDrop`. Стандартный паттерн — проверка предмета против массива разрешённых `ItemData`:

```csharp
[SerializeField] private ItemData[] _acceptedItems;

public bool HandleDrop(ItemData item, Vector2 screenPosition)
{
    if (System.Array.IndexOf(_acceptedItems, item) < 0) return false;
    // ...
}
```

Примеры реализаций:

- `MedallionBoxUI` — принимает только медальоны из `_medallionOrder`, определяет лунку через Physics raycast. При возврате медальона проверяет `IsFull` перед `hole.Retrieve()`.
- `ElectricPuzzleController` — принимает предохранитель, определяет зону вставки через raycast на `_fuseAnchorCollider`

---

## Как добавить новый предмет

1. **Assets > Create > Inventory > Item Data** — заполнить `itemName`, `icon`, `inspectionPrefab`
2. Положить ассет в `Assets/Data/Items/` — `InventoryAutoPopulate` подхватит его автоматически
3. Открыть созданный `ItemData` в Inspector — появится 3D-виджет. Drag внутри виджета, чтобы повернуть модель к читаемому ракурсу; углы запишутся автоматически
4. Создать GameObject в сцене, добавить `PickableItem`, назначить новый `ItemData`

## Как добавить рецепт крафта

1. **Assets > Create > Inventory > Crafting Recipe** — указать два ингредиента и результат
2. Положить ассет в `Assets/Data/Recipes/` — `InventoryAutoPopulate` подхватит его автоматически