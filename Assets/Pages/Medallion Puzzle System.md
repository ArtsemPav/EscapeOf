## Medallion Puzzle System

Загадка с китайской шкатулкой: игрок подбирает 5 медальонов стихий, открывает шкатулку и раскладывает монеты по лункам в правильном порядке. Состояние полностью сохраняется через Save System.

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

Размещается на 3D-объекте шкатулки. Реализует `IInteractable` и `ISaveable`.

**Inspector — References**

| Поле | Описание |
|---|---|
| `_boxCamera` | `CinemachineCamera`, которая наводится на шкатулку |
| `_panel` | Корневой GameObject панели `MedallionBoxPanel` в Canvas |
| `_solvedObject` | GameObject, который активируется когда загадка решена (свет, эффект) |
| `_medallionOrder` | Массив `ItemData` в правильном порядке: 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood |

**Inspector — Settings**

| Поле | Описание |
|---|---|
| `_interactText` | Текст подсказки на прицеле (`"Осмотреть шкатулку"` по умолчанию) |
| `_blendDuration` | Длительность плавного перехода камеры (секунды) |
| `_sideZoneWidth` | Ширина боковой зоны клика для закрытия (доля ширины экрана, 0.05–0.49) |

**Inspector — Events**

| Поле | Описание |
|---|---|
| `_onPuzzleSolved` | `UnityEvent` — срабатывает однократно при решении загадки |

**Save ID:** `"medallion_puzzle"`

**Что сохраняет:** флаг `solved` + `ItemId` медальона в каждой из 5 лунок (пустая лунка = пустая строка).

```json
{ "solved": false, "placedItemIds": ["Fire", "", "Earth", "", ""] }
```

---

### `MedallionBoxUI`

Размещается на `MedallionBoxPanel`. Управляет drag-and-drop, размещением монет в лунках и проверкой победы. Реализует `IPuzzleDropHandler` — принимает предметы из `PuzzleInventoryBar`.

**Inspector**

| Поле | Описание |
|---|---|
| `_holes` | Массив `MedallionHole` в порядке 0=Fire..4=Wood |
| `_holeLayer` | LayerMask коллайдеров лунок (для Physics.Raycast) |
| `_coinPrefab` | Запасной prefab монеты — используется только если у `ItemData` не заполнен `inspectionPrefab` |
| `_dropHeight` | Высота начала анимации падения / подъёма монеты (метры) |
| `_dropDuration` | Длительность падения / подъёма (секунды) |
| `_ghostSize` | Размер иконки-призрака при перетаскивании (пиксели) |

**Событие:** `OnPuzzleSolved` — Action без аргументов, подписывается `MedallionBoxInteraction`.

**Логика победы:** все 5 лунок заполнены И каждая содержит правильный `ItemData` согласно `_medallionOrder`.

**Интеграция с `PuzzleInventoryBar`**

`HandleDrop` выполняет два последовательных условия:

1. Предмет должен быть в `_medallionOrder` — иначе возвращает `false` и медальон возвращается в бар
2. Raycast по `_holeLayer` определяет целевую лунку — если попали в свободную, монета вставляется

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

`MedallionBoxInteraction.Open()` вызывает `PuzzleInventoryBar.Instance.Show(this)`, `Close()` — `PuzzleInventoryBar.Instance.Hide()`.

---

### `MedallionHole`

Размещается на каждом из объектов `Hole_0`..`Hole_4` на шкатулке.

| Поле | Описание |
|---|---|
| `_coinMaterial` | Опциональный материал для монеты. Если `null` — используется материал из prefab |

**Публичный API**

| Метод / свойство | Описание |
|---|---|
| `IsFilled` | `true` если лунка занята |
| `PlacedItem` | `ItemData` монеты в лунке, или `null` |
| `Fill(item, fallbackPrefab, height, duration)` | Разместить монету с анимацией падения (ease-in). Использует `item.inspectionPrefab`; если он `null` — `fallbackPrefab` |
| `FillImmediate(item, fallbackPrefab)` | Разместить монету мгновенно — при восстановлении из сохранения. Та же логика выбора prefab |
| `Retrieve(riseHeight, riseDuration)` | Извлечь монету: возвращает `ItemData` немедленно, монета поднимается вверх с анимацией ease-out и уничтожается в верхней точке |

---

### `MedallionCollectionTracker`

Синглтон-ISaveable. Запоминает **порядок**, в котором игрок подобрал медальоны. `MedallionBoxUI` показывает слоты именно в этом порядке.

**Inspector**

| Поле | Описание |
|---|---|
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
    CloseButton

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
       └─ MedallionBoxUI.RestoreState() → FillImmediate() для каждой занятой лунки

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
  └─ MedallionHole.Retrieve(riseHeight, riseDuration)
       ├─ Возвращает ItemData немедленно → InventorySystem.AddItem() → Save()
       └─ RiseRoutine: монета поднимается вверх (ease-out) и уничтожается

Загадка решена
  └─ _solvedObject.SetActive(true)
  └─ _onPuzzleSolved UnityEvent
  └─ SaveManager.Save() (финальный снимок с solved=true)
  └─ Панель и камера закрываются (ForceClose)
```

---

## Настройка с нуля

### 1 — ItemData медальонов

Для каждого из 5 медальонов создай `ItemData` (`Create → Game → Item Data`):
- Заполни `itemName`, `icon`, `inspectionPrefab`
- `ItemId` — уникальная строка, используется системой сохранений

### 2 — PickableItem в сцене

На каждом 3D-объекте монеты на столе:
1. Компонент `PickableItem` → поле `Item Data` — нужный `ItemData`
2. Поле `_saveId` — задай уникальный ID вручную

Использованные в проекте Save ID:

| Монета | Save ID |
|---|---|
| Earth | `pickable_medallion_earth` |
| Fire | `pickable_medallion_fire` |
| Iron | `pickable_medallion_iron` |
| Water | `pickable_medallion_water` |
| Wood | `pickable_medallion_wood` |

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
- Пустой `_saveId` у `PickableItem`. Задай его вручную в Inspector.

**Инвентарь пустой после перезагрузки, хотя монеты были подобраны**
- Медальонов нет в массиве `_allItems` у `InventorySystem`. `FindItemById()` вернёт `null` и слот останется пустым.

**Монеты дублируются (и в лунках, и в инвентаре)**
- Те же причины выше — состояние мира и инвентаря оказывается рассинхронизировано.

**Загадка не помечается решённой после перезагрузки**
- `_solvedObject` не назначен, или `ApplyPendingLoad()` не вызывается в `Start()` у `MedallionBoxInteraction`.

**Слоты в панели отображаются не в том порядке**
- `MedallionCollectionTracker._medallions` пуст или не содержит все 5 медальонов.
