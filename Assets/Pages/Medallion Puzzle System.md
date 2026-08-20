Загадка с китайской шкатулкой: игрок подбирает 5 медальонов стихий, открывает шкатулку и раскладывает монеты по лункам в правильном порядке. Состояние полностью сохраняется через [@ id="/Pages/Private/Save System.md" label="Save System"].

---

## Компоненты системы

```
MedallionBoxInteraction    ← на 3D-объекте шкатулки (ISaveable)
MedallionBoxUI             ← на MedallionBoxPanel в Canvas (IPuzzleDropHandler)
  MedallionSlot × N        ← слоты инвентаря (дочерние объекты панели)
MedallionHole × 5          ← Hole_0..Hole_4 на шкатулке в сцене
MedallionCollectionTracker ← синглтон, отдельный GameObject (ISaveable)
PickableItem × 5           ← на каждой монете в сцене (ISaveable)
```

---

## Компоненты подробно

### `MedallionBoxInteraction`

Размещается на 3D-объекте шкатулки. Реализует `ISaveable`. Управляет звуками, анимацией открытия шкатулки и синхронизацией с `PuzzleModeController`.

**Inspector — References**


| Поле              | Описание                                                                         |
| ----------------- | -------------------------------------------------------------------------------- |
| `_controller`     | `PuzzleModeController` на том же объекте                                         |
| `_panel`          | Корневой GameObject панели `MedallionBoxPanel` в Canvas                          |
| `_solvedObject`   | GameObject, который активируется когда загадка решена (свет, эффект)             |
| `_medallionOrder` | Массив `ItemData` в правильном порядке: 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood |
| `_holes`          | Массив `MedallionHole` (Hole_0..Hole_4) — для подписки на звуки                  |


**Inspector — Settings**


| Поле             | Описание                                                               |
| ---------------- | ---------------------------------------------------------------------- |
| `_sideZoneWidth` | Ширина боковой зоны клика для закрытия (доля ширины экрана, 0.05–0.49) |


**Inspector — Sounds**


| Поле              | Описание                                             |
| ----------------- | ---------------------------------------------------- |
| `_openBoxClip`    | Звук первого осмотра шкатулки (один раз за сессию)   |
| `_solvedClip`     | Звук решения загадки (в момент начала анимации Open) |
| `_coinDropClip`   | Звук укладки медальона в лунку                       |
| `_coinPickupClip` | Звук извлечения медальона из лунки                   |


**Save ID:** `"medallion_puzzle"`

**Что сохраняет:** флаг `solved` + `ItemId` медальона в каждой из 5 лунок (пустая лунка = пустая строка).

```json
{ "solved": false, "placedItemIds": ["Fire", "", "Earth", "", ""] }
```

---

### Анимация шкатулки

Аниматор (`ChineseBoxAnimator.controller`) должен содержать:

**Параметр:**

- `IsOpen` — `Bool`

**Стейты:**

```
[Entry] → Idle ──(IsOpen=true)──→ Open ──(Has Exit Time)──→ Opened
```


| Стейт    | Описание                                                       |
| -------- | -------------------------------------------------------------- |
| `Idle`   | Шкатулка закрыта. Стейт по умолчанию (оранжевый)               |
| `Open`   | Анимация открытия крышки. Воспроизводится один раз при решении |
| `Opened` | Шкатулка открыта. Финальная поза. Без зацикливания             |


**Переход Idle → Open:**

- Condition: `IsOpen = true`
- Has Exit Time: выключено

**Переход Open → Opened:**

- Condition: нет
- Has Exit Time: включено (переход срабатывает по окончании клипа)

**Логика в коде:**

- При решении: `SetBool("IsOpen", true)` → корутина ждёт `normalizedTime >= 1f` стейта `Open` → вызывает `SetSolved()` → камера возвращается к игроку.
- При загрузке сохранения: `SetBool("IsOpen", true)` + `Play("Opened", 0, 1f)` — мгновенный телепорт в конец `Opened`, анимация `Open` не воспроизводится.

---

### `MedallionBoxUI`

Размещается на `MedallionBoxPanel`. Управляет drag-and-drop, размещением монет в лунках и проверкой победы. Реализует `IPuzzleDropHandler` — принимает предметы из `PuzzleInventoryBar`.

**Inspector**


| Поле            | Описание                                                                                      |
| --------------- | --------------------------------------------------------------------------------------------- |
| `_holes`        | Массив `MedallionHole` в порядке 0=Fire..4=Wood                                               |
| `_holeLayer`    | LayerMask коллайдеров лунок (для Physics.Raycast)                                             |
| `_coinPrefab`   | Запасной prefab монеты — используется только если у `ItemData` не заполнен `inspectionPrefab` |
| `_dropHeight`   | Высота начала анимации падения / подъёма монеты (метры)                                       |
| `_dropDuration` | Длительность падения / подъёма (секунды)                                                      |
| `_ghostSize`    | Размер иконки-призрака при перетаскивании (пиксели)                                           |


**Событие:** `OnPuzzleSolved` — Action без аргументов, подписывается `MedallionBoxInteraction`.

**Логика победы:** все 5 лунок заполнены И каждая содержит правильный `ItemData` согласно `_medallionOrder`.

**Интеграция с `PuzzleInventoryBar**`

`MedallionBoxUI` реализует `IPuzzleDropHandler`. `HandleDrop` выполняет два последовательных условия:

1. Предмет должен присутствовать в `_medallionOrder` — иначе возвращает `false` и медальон возвращается в бар
2. Raycast по `_holeLayer` от позиции курсора определяет целевую лунку — если попали в свободную лунку, монета вставляется

```csharp
public bool HandleDrop(ItemData item, Vector2 screenPosition)
{
    // Только медальоны из этой загадки принимаются
    if (Array.IndexOf(_medallionOrder, item) < 0) return false;

    var ray = Camera.main.ScreenPointToRay(screenPosition);
    if (!Physics.Raycast(ray, out var hit, 50f, _holeLayer, QueryTriggerInteraction.Collide))
        return false;

    var hole = hit.collider.GetComponent<MedallionHole>();
    if (hole == null || hole.IsFilled) return false;

    hole.Fill(item, _coinPrefab, _dropHeight, _dropDuration);
    CheckVictory();
    return true;
}
```

`MedallionBoxInteraction.Open()` вызывает `PuzzleInventoryBar.Instance.Show(this)`, `Close()` — `PuzzleInventoryBar.Instance.Hide()`. Бар показывает весь инвентарь; предметы, не являющиеся медальонами из `_medallionOrder`, возвращаются в бар при попытке бросить их на шкатулку.

---

### `MedallionHole`

Размещается на каждом из объектов `Hole_0`..`Hole_4` на шкатулке.

**Inspector — Coin Animation**


| Поле            | Описание                                                                        |
| --------------- | ------------------------------------------------------------------------------- |
| `_coinMaterial` | Опциональный материал для монеты. Если `null` — используется материал из prefab |


**Inspector — Hover Highlight**


| Поле                 | Описание                                                                                                          |
| -------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `_highlightEmission` | HDR-цвет emission при наведении курсора (пикер поддерживает HDR). По умолчанию тёплое золото `(0.55, 0.42, 0.08)` |


**Публичный API**


| Метод / свойство                               | Описание                                                                                                                       |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `IsFilled`                                     | `true` если лунка занята                                                                                                       |
| `PlacedItem`                                   | `ItemData` монеты в лунке, или `null`                                                                                          |
| `Fill(item, fallbackPrefab, height, duration)` | Разместить монету с анимацией падения (ease-in). Использует `item.inspectionPrefab`; если он `null` — `fallbackPrefab`         |
| `FillImmediate(item, fallbackPrefab)`          | Разместить монету мгновенно — при восстановлении из сохранения. Та же логика выбора prefab                                     |
| `Retrieve(riseHeight, riseDuration)`           | Извлечь монету: возвращает `ItemData` немедленно, монета поднимается вверх с анимацией ease-out и уничтожается в верхней точке |
| `Highlight(bool on)`                           | Включить / выключить emission-подсветку на коине. Вызывается из `MedallionBoxUI` при наведении курсора                         |


**Как работает подсветка:** при `Fill` / `FillImmediate` инстанцируется коин, получается его `Renderer`, на нём вызывается `renderer.material.EnableKeyword("_EMISSION")` (создаёт per-instance материал). `Highlight()` меняет `_EmissionColor` через `MaterialPropertyBlock` без дополнительных аллокаций. При `Retrieve()` подсветка сбрасывается и ссылка на рендерер обнуляется.

---

### `MedallionCollectionTracker`

Синглтон-ISaveable. Запоминает **порядок**, в котором игрок подобрал медальоны. `MedallionBoxUI` показывает слоты именно в этом порядке.

**Inspector**


| Поле          | Описание                                       |
| ------------- | ---------------------------------------------- |
| `_medallions` | Все 5 `ItemData` медальонов (порядок не важен) |


**Save ID:** `"medallion_collection"`

**Что сохраняет:** список `ItemId` в порядке подбора.

---

## Иерархия в сцене

```
chinesBox                                ← MedallionBoxInteraction, Collider, Interactable Layer
  Hole_0                                 ← MedallionHole, SphereCollider (Hole Layer)
  Hole_1
  Hole_2
  Hole_3
  Hole_4

Canvas
  MedallionBoxPanel                      ← MedallionBoxUI (активен только при осмотре)
    MedallionSlot_0..N                   ← MedallionSlot
    CloseButton                          ← находится по имени автоматически

Env/FirstRoom/props/chinesCoin           ← PickableItem (Fire)
Env/FirstRoom/props/chinesCoin (1)       ← PickableItem (Earth)
...

MedallionCollectionTracker               ← отдельный GameObject с компонентом
```

---

## Как работает полный цикл

```
Старт сессии
  └─ SaveManager.Start() → LoadSaveData() на всех ISaveable
       ├─ InventorySystem восстанавливает слоты
       ├─ MedallionCollectionTracker восстанавливает порядок сбора
       ├─ MedallionBoxInteraction хранит данные в _pendingLoad
       └─ PickableItem: collected=true → Destroy (монета не появляется в сцене)
  └─ MedallionBoxInteraction.Start() → ApplyPendingLoad()
       ├─ MedallionBoxUI.RestoreState() → FillImmediate() для каждой занятой лунки
       └─ если solved=true:
            ├─ _animator.SetBool("IsOpen", true)
            └─ _animator.Play("Opened", 0, 1f)  ← мгновенно, без воспроизведения Open

Игрок подбирает монету
  └─ PickableItem.Interact() → ItemInspector.BeginInspection()
       └─ Клик → ConfirmPickup()
            ├─ InventorySystem.AddItem()  → Save() (автоматически в AddItem)
            └─ PickableItem.NotifyPickedUp() → _collected=true → Save()

Игрок открывает шкатулку (E или ЛКМ)
  └─ CinemachineCamera активируется, панель открывается
  └─ PuzzleInventoryBar.Instance.Show(this) — бар появляется внизу экрана
  └─ MedallionBoxUI.Populate() → слоты заполняются в порядке сбора

Игрок перетаскивает монету из бара в лунку
  └─ MedallionBoxUI.HandleDrop(item, screenPos)
       ├─ Проверка: item входит в _medallionOrder — иначе возврат в бар
       ├─ Physics.Raycast по _holeLayer — определяет лунку
       ├─ MedallionHole.Fill() — анимация падения монеты
       ├─ InventorySystem.RemoveItem() → Save()
       └─ CheckVictory() — если всё верно → OnPuzzleSolved

ЛКМ по занятой лунке → монета возвращается в инвентарь
  └─ MedallionBoxUI.Update() → TryRetrieveFromHole()
       ├─ MedallionHole.Retrieve(riseHeight, riseDuration)
       │    ├─ Highlight(false) + _placedRenderer = null
       │    └─ RiseRoutine: монета поднимается вверх (ease-out) и уничтожается
       └─ InventorySystem.AddItem() → Save()

Загадка решена
  └─ HandlePuzzleSolved():
       ├─ _solvedObject.SetActive(true)
       ├─ PlaySFX(_solvedClip)
       ├─ _animator.SetBool("IsOpen", true)  ← переход Idle → Open
       └─ StartCoroutine(WaitForOpenAnimationRoutine())
            └─ ждёт пока IsName("Open") && normalizedTime >= 1f
                 └─ _controller.SetSolved()
                      ├─ ExitPuzzleMode() ← камера возвращается к игроку
                      └─ SaveManager.Save() (финальный снимок с solved=true)
```

---

## Известные исправления

### Дублирование медальонов / исчезновение при перезагрузке

**Симптом:** после вставки медальона в лунку и перезапуска игры — медальон оказывался одновременно и в инвентаре, и в лунке. Либо другие уже подобранные медальоны пропадали.

**Причина — race condition при инициализации:**

```
SaveManager.Start()               [order -10] → LoadSaveData() на всех объектах
MedallionCollectionTracker.Start() [order  -5] → OnInventoryChanged() → Save()
MedallionBoxInteraction.Start()   [order   0] → ApplyPendingLoad() ← НЕ УСПЕЛ
```

`MedallionCollectionTracker.Start()` мог вызвать `Save()` до того, как `MedallionBoxInteraction.Start()` применил `_pendingLoad`. `BuildSnapshot()` фиксировал лунки как **пустые** — корректный файл перезаписывался. При следующей загрузке: медальон снова в инвентаре, лунка пуста. Цикл повторялся.

**Исправление — два взаимодополняющих механизма:**

1. `MedallionBoxInteraction` получил `[DefaultExecutionOrder(-7)]` — теперь `ApplyPendingLoad()` выполняется **раньше** Start трекера, и когда тот вызывает `Save()`, лунки уже заполнены.
2. `MedallionCollectionTracker` получил флаг `_isReady` — стартовая синхронизация в `Start()` обновляет `_collectionOrder` без вызова `Save()`. Флаг выставляется после синхронизации; только после этого реальные игровые события могут инициировать сохранение.

### Анимация при повторной загрузке проигрывается снова

**Симптом:** игрок уже решил загадку, перезапустил игру — при входе в сцену анимация открытия шкатулки воспроизводится повторно.

**Причина:** при загрузке `ApplyPendingLoad()` устанавливал только `SetBool("IsOpen", true)`, что запускало переход `Idle → Open` через аниматор.

**Исправление:** добавлен вызов `_animator.Play("Opened", 0, 1f)` сразу после `SetBool`. Это телепортирует аниматор напрямую в стейт `Opened` в нормализованное время `1f` (конец клипа), минуя воспроизведение `Open`.

### Камера возвращается к игроку до окончания анимации открытия

**Симптом:** после решения загадки камера сразу улетала обратно, пока анимация крышки ещё играла.

**Причина:** `HandlePuzzleSolved()` вызывал `_controller.SetSolved()` немедленно, что в свою очередь сразу вызывало `ExitPuzzleMode()`.

**Исправление:** `HandlePuzzleSolved()` запускает анимацию и стартует корутину `WaitForOpenAnimationRoutine()`. Корутина ждёт пока `IsName("Open") && normalizedTime >= 1f`, и только потом вызывает `_controller.SetSolved()` → камера возвращается к игроку.

---

## Настройка с нуля

### 1 — ItemData медальонов

Для каждого из 5 медальонов создай `ItemData` (`Create → Game → Item Data`):

- Заполни `itemName`, `icon`, `inspectionPrefab`
- `ItemId` — уникальная строка, используется системой сохранений

### 2 — PickableItem в сцене

На каждом 3D-объекте монеты на столе:

1. Компонент `PickableItem` → поле `Item Data` — нужный `ItemData`
2. Поле `_saveId` — задай вручную или через **ПКМ на компоненте → Generate Save ID**

Использованные в проекте Save ID:


| Монета | Save ID                    |
| ------ | -------------------------- |
| Earth  | `pickable_medallion_earth` |
| Fire   | `pickable_medallion_fire`  |
| Iron   | `pickable_medallion_iron`  |
| Water  | `pickable_medallion_water` |
| Wood   | `pickable_medallion_wood`  |


### 3 — Лунки (MedallionHole)

На объектах `Hole_0`..`Hole_4` добавь компонент `MedallionHole`. Дай каждому `SphereCollider` на слое, указанном в `MedallionBoxUI._holeLayer`.

### 4 — MedallionBoxUI

Назначь в Inspector:

- `_holes` — перетащи `Hole_0`..`Hole_4` в порядке 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood
- `_coinPrefab` — prefab монеты
- `_holeLayer` — Layer лунок

### 5 — MedallionBoxInteraction

На объекте шкатулки:

- `_medallionOrder` — те же 5 `ItemData` в том же порядке, что и `_holes`
- `_boxCamera`, `_panel`, `_solvedObject` — назначить соответствующие объекты

### 6 — MedallionCollectionTracker

На отдельном GameObject добавь компонент `MedallionCollectionTracker`:

- `_medallions` — все 5 `ItemData` (порядок не важен)

### 7 — InventorySystem._allItems

Все 5 медальонов (`ItemData`) **обязательно** добавить в массив `_allItems` компонента `InventorySystem`. Без этого после перезагрузки инвентарь не сможет восстановить медальоны из файла.

---

## Часто встречающиеся ошибки

**В лунке всегда отображается одна и та же монета (например, Iron)**

- `Fill()` и `FillImmediate()` используют `item.inspectionPrefab` для 3D-модели в лунке. Убедись что `inspectionPrefab` заполнен у каждого из 5 `ItemData` медальонов. Если поле пустое — используется общий `_coinPrefab` из `MedallionBoxUI`.

**Монета не исчезает из сцены после подбора при перезагрузке**

- Пустой `_saveId` у `PickableItem`. Задай его вручную или через Generate Save ID.

**Инвентарь пустой после перезагрузки, хотя монеты были подобраны**

- Медальонов нет в массиве `_allItems` у `InventorySystem`. `FindItemById()` вернёт `null` и слот останется пустым.

**Монеты дублируются (и в лунках, и в инвентаре) / медальоны пропадают после перезагрузки**

- Race condition при инициализации — описан в разделе **Известные исправления** выше. Убедись что в проекте актуальная версия скриптов с `[DefaultExecutionOrder(-7)]` на `MedallionBoxInteraction` и флагом `_isReady` в `MedallionCollectionTracker`.

**Анимация Open воспроизводится повторно при загрузке уже решённой загадки**

- Стейт `Opened` не существует в аниматоре, или называется иначе. `Play("Opened", 0, 1f)` не сработает и аниматор начнёт переход `Idle → Open`. Убедись что стейт называется ровно `Opened`.

**Камера возвращается к игроку до окончания анимации**

- Стейт называется не `Open` (чувствительно к регистру). Корутина `WaitForOpenAnimationRoutine` ищет `IsName("Open")` — если имя не совпадает, ожидание пропускается и `SetSolved()` вызывается мгновенно.