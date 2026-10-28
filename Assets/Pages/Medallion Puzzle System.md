Загадка с китайской шкатулкой: игрок подбирает 5 медальонов стихий, открывает шкатулку и раскладывает монеты по лункам в правильном порядке. Состояние полностью сохраняется через [@ id="/Pages/Private/Save System.md" label="Save System"].

---

## Компоненты системы

```
MedallionBoxInteraction    ← на 3D-объекте шкатулки (ISaveable, IPuzzleDropHandler, [DefaultExecutionOrder(-7)])
MedallionBoxUI             ← на MedallionBoxPanel в Canvas (IPuzzleDropHandler)
MedallionHole × 5          ← Hole_0..Hole_4 на шкатулке в сцене
MedallionCollectionTracker ← синглтон, отдельный GameObject (ISaveable, [DefaultExecutionOrder(-5)])
MedallionRoomTransformation← трансформация комнаты после решения (ISaveable, [DefaultExecutionOrder(-3)])
PickableItem × 5           ← на каждой монете в сцене (ISaveable)
MedallionSlot              ← DEPRECATED: пустая заглушка (логика слотов переехала в PuzzleInventoryBar + PuzzleInventorySlot)
```

---

## Компоненты подробно

### `MedallionBoxInteraction`

Файл: `Assets/Scripts/Puzzle/Medallion/MedallionBoxInteraction.cs`. Размещается на 3D-объекте шкатулки. Реализует `ISaveable` и `IPuzzleDropHandler` (делегирует дроп в `MedallionBoxUI`). Управляет панелью, звуками, анимацией открытия шкатулки и синхронизацией с `PuzzleModeController`.

**Inspector — References**


| Поле              | Описание                                                                         |
| ----------------- | -------------------------------------------------------------------------------- |
| `_controller`     | `PuzzleModeController` (если пуст — берётся с того же объекта)                   |
| `_panel`          | Корневой GameObject панели `MedallionBoxPanel` в Canvas                          |
| `_solvedObject`   | GameObject, активируемый при решении (свет, эффект)                              |
| `_medallionOrder` | Массив `ItemData` в правильном порядке: 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood |
| `_holes`          | Массив `MedallionHole` — для подписки на звуки. Если пуст — автозаполняется из детей (`GetComponentsInChildren`, включая неактивные) |


**Inspector — Sounds**


| Поле                | Описание                                             |
| ------------------- | ---------------------------------------------------- |
| `_openBoxClip`      | Звук первого осмотра шкатулки (один раз за сессию)   |
| `_solvedClip`       | Звук решения загадки (в момент начала анимации Open) |
| `_coinDropClip`     | Звук укладки медальона в лунку                       |
| `_coinPickupClip`   | Звук извлечения медальона из лунки                   |
| `_openBoxVolume`    | Громкость (default: 1)                               |
| `_solvedVolume`     | Громкость (default: 1)                               |
| `_coinDropVolume`   | Громкость (default: 0.8)                             |
| `_coinPickupVolume` | Громкость (default: 0.7)                             |


Все звуки проигрываются через `AudioManager.PlaySFX`.

**Save ID:** `"medallion_puzzle"` (константа, не поле Inspector).

**Что сохраняет:** флаг `solved` + `ItemId` медальона в каждой из 5 лунок (пустая лунка = пустая строка).

```json
{ "solved": false, "placedItemIds": ["Fire", "", "Earth", "", ""] }
```

**Порядок выполнения:** `[DefaultExecutionOrder(-7)]` — после `SaveManager` (−10) и до `MedallionCollectionTracker` (−5), чтобы `ApplyPendingLoad()` заполнил лунки до стартовой синхронизации трекера.

**IPuzzleDropHandler:** `HandleDrop(item, screenPosition, out replacement)` делегирует в `MedallionBoxUI.HandleDrop` с панели из `_panel`.

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

- При решении: `SetBool("IsOpen", true)` → корутина `WaitForOpenAnimationRoutine` ждёт `normalizedTime >= 1f` стейта `Open` → вызывает `SetSolved()` → камера возвращается к игроку.
- При загрузке сохранения: `SetBool("IsOpen", true)` + `Play("Opened", 0, 1f)` — мгновенный телепорт в конец `Opened`, анимация `Open` не воспроизводится.

---

### `MedallionBoxUI`

Файл: `Assets/Scripts/Puzzle/Medallion/MedallionBoxUI.cs`. Размещается на `MedallionBoxPanel`. Реализует `IPuzzleDropHandler` — принимает предметы из `PuzzleInventoryBar`. Проверяет ПОРЯДОК расстановки. Drag/ghost-логика и слоты — на стороне `PuzzleInventoryBar`.

**Inspector**


| Поле            | Описание                                                                                      |
| --------------- | --------------------------------------------------------------------------------------------- |
| `_holes`        | Массив `MedallionHole` в порядке 0=Fire..4=Wood                                               |
| `_holeLayer`    | LayerMask коллайдеров лунок (для Physics.Raycast). В рантайме объединяется с реальными слоями назначенных лунок (`_effectiveHoleLayer`) — рейкаст не «теряется», если лунки на другом слое |
| `_coinPrefab`   | Запасной prefab монеты — используется только если у `ItemData` не заполнен `inspectionPrefab` |
| `_dropHeight`   | Высота начала анимации падения / подъёма монеты (default: 0.005 м)                            |
| `_dropDuration` | Длительность падения / подъёма (default: 0.35 с)                                              |


**Событие:** `OnPuzzleSolved` — `Action` без аргументов, подписывается `MedallionBoxInteraction`.

**Публичный API**


| Метод                                          | Описание                                                                        |
| ---------------------------------------------- | ------------------------------------------------------------------------------- |
| `Populate(ItemData[] medallionOrder)`          | Сохраняет ожидаемый порядок. Вызывается при каждом открытии панели               |
| `MarkSolved()`                                 | Принудительно блокирует извлечение (при загрузке решённого состояния)           |
| `GetHoleStates()`                              | `ItemData[]` текущего содержимого лунок — используется Save System              |
| `RestoreState(string[] ids, ItemData[] items)` | Восстановление из сейва: `FillImmediate` для каждой занятой лунки + тихая проверка победы |
| `HandleDrop(item, screenPos, out replacement)` | Дроп из бара: валидация → рейкаст → `Fill` → `CheckVictory`                      |


**Логика победы:** все 5 лунок заполнены И каждая содержит правильный `ItemData` согласно `_medallionOrder` (`CheckVictory`). `CheckVictorySilent` делает то же без события (при загрузке).

**Интеграция с `PuzzleInventoryBar`**

`HandleDrop` выполняет два последовательных условия:

1. Предмет должен присутствовать в `_medallionOrder` — иначе возвращает `false` и медальон возвращается в бар
2. Raycast по `_effectiveHoleLayer` от позиции курсора определяет целевую лунку — если попали в свободную лунку, монета вставляется

```csharp
public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
{
    replacement = null;
    // Только медальоны из этой загадки принимаются
    if (_medallionOrder == null || Array.IndexOf(_medallionOrder, item) < 0) return false;

    var ray = Camera.main.ScreenPointToRay(screenPosition);
    if (!Physics.Raycast(ray, out var hit, 50f, _effectiveHoleLayer, QueryTriggerInteraction.Collide))
        return false;

    var hole = hit.collider.GetComponent<MedallionHole>();
    if (hole == null || hole.IsFilled) return false;

    hole.Fill(item, _coinPrefab, _dropHeight, _dropDuration, CheckVictory);
    return true;
}
```

Панель открывается через `UIManager.Instance.OpenPanel(_panel)` из `MedallionBoxInteraction.HandleEntered`; сам инвентарь-бар показывает `PuzzleModeController` (`_showInventoryBar = true`).

Каждый кадр в `Update` (пока загадка не решена):

- **Ghost-превью:** при перетаскивании медальона над пустой лункой — полупрозрачная 3D-модель на лунке (`hole.ShowGhost` / `HideGhost`).
- **Hover-подсветка:** рейкаст по лункам — заполненная лунка под курсором подсвечивается (`hole.Highlight(true/false)`).
- **ЛКМ по занятой лунке** (вне drag) — `TryRetrieveFromHole`: `inv.ReleaseAllReservations()`, `inv.Compact()`, `hole.Retrieve(...)`, `inv.AddItem(item)`. Если инвентарь внезапно заполнился — монета мгновенно возвращается в лунку (`FillImmediate`).

---

### `MedallionHole`

Файл: `Assets/Scripts/Puzzle/Medallion/MedallionHole.cs`. Размещается на каждом из объектов `Hole_0`..`Hole_4` на шкатулке. Принимает любой медальон — правильность порядка проверяет `MedallionBoxUI`.

**Inspector — Coin Animation**


| Поле            | Описание                                                                        |
| --------------- | ------------------------------------------------------------------------------- |
| `_coinMaterial` | Опциональный материал для монеты. Если `null` — используется материал из prefab |


**Inspector — Lighting**


| Поле                     | Описание                                                                                                     |
| ------------------------ | ------------------------------------------------------------------------------------------------------------ |
| `_coinRenderingLayerMask`| URP rendering layer mask для инстанцированной монеты (default: биты 22 `CoridorFDoor` + 9 `flashLight`)       |


**Inspector — Hover Highlight**


| Поле                 | Описание                                                                                                          |
| -------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `_highlightEmission` | HDR-цвет emission при наведении курсора (пикер поддерживает HDR). По умолчанию тёплое золото `(0.55, 0.42, 0.08)` |


**Inspector — Ghost Preview**


| Поле                 | Описание                                                                                        |
| -------------------- | ----------------------------------------------------------------------------------------------- |
| `_ghostMaterial`     | Материал ghost-превью. Если `null` — материал монеты делается полупрозрачным в рантайме (URP/Lit) |
| `_ghostAlpha`        | Прозрачность ghost без явного материала (0.1–0.9, default: 0.45)                                 |
| `_ghostFadeDuration` | Длительность фейд-ин/аут ghost (default: 0.2 с)                                                  |


**Inspector — Coin Placement**


| Поле                  | Описание                                                                                          |
| --------------------- | ------------------------------------------------------------------------------------------------- |
| `_useColliderCenter`  | `true` — монета ставится в world-space центр коллайдера лунки; `false` — в `transform.position` (default: `true`) |
| `_coinPositionOffset` | Локальный офсет позиции монеты относительно лунки (тонкая подгонка)                                |
| `_coinRotationOffset` | Локальный офсет поворота, Эйлер (default: `(0, −90, 180)` — монета лицом вверх, символ правильно) |


**Inspector — Insert / Retrieve Animation**


| Поле                | Описание                                                            |
| ------------------- | ------------------------------------------------------------------- |
| `_insertHeight`     | Высота, с которой монета начинает анимацию вставки (default: 0.3 м) |
| `_insertDuration`   | Длительность вставки (default: 0.4 с, ease-in)                      |
| `_retrieveHeight`   | Высота подъёма при извлечении (default: 0.25 м)                     |
| `_retrieveDuration` | Длительность извлечения (default: 0.4 с)                            |


**События:** `OnFilled`, `OnRetrieved` — подписывается `MedallionBoxInteraction` для звуков.

**Публичный API**


| Метод / свойство                                           | Описание                                                                                                                       |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `IsFilled`                                                 | `true` если лунка занята                                                                                                       |
| `PlacedItem`                                               | `ItemData` монеты в лунке, или `null`                                                                                          |
| `Fill(item, fallbackPrefab, height, duration, onComplete)` | Разместить монету с анимацией вставки (ease-in). Использует `item.inspectionPrefab`; если он `null` — `fallbackPrefab`. `onComplete` вызывается после анимации (туда передаётся `CheckVictory`) |
| `FillImmediate(item, fallbackPrefab)`                      | Разместить монету мгновенно — при восстановлении из сохранения. Та же логика выбора prefab                                      |
| `Retrieve(riseHeight, riseDuration)`                       | Извлечь монету: возвращает `ItemData` немедленно, монета поднимается вверх и уничтожается в верхней точке; сбрасывает подсветку |
| `Highlight(bool on)`                                       | Emission-подсветка монеты. Вызывается из `MedallionBoxUI` по рейкасту курсора                                                   |
| `ShowGhost(item, fallbackPrefab)` / `HideGhost()`          | Полупрозрачное 3D-превью на пустой лунке при перетаскивании (фейд-ин за `_ghostFadeDuration`)                                  |
| `SetColliderEnabled(bool)`                                 | Включить/выключить коллайдер лунки (чтобы не блокировал основной коллайдер взаимодействия, пока игрок вне режима загадки)        |


**Как работает подсветка:** при `Fill` / `FillImmediate` инстанцируется коин, кешируется его `Renderer`, на нём включается `_EMISSION` (per-instance материал). `Highlight()` меняет `_EmissionColor` через `MaterialPropertyBlock` без аллокаций. При `Retrieve()` подсветка сбрасывается, ссылка на рендерер обнуляется.

---

### `MedallionCollectionTracker`

Файл: `Assets/Scripts/Puzzle/Medallion/MedallionCollectionTracker.cs`. Синглтон-ISaveable, `[DefaultExecutionOrder(-5)]`. Запоминает **порядок**, в котором игрок подобрал медальоны (`CollectionOrder`).

**Inspector**


| Поле          | Описание                                       |
| ------------- | ---------------------------------------------- |
| `_medallions` | Все 5 `ItemData` медальонов (порядок не важен) |


**Save ID:** `"medallion_collection"`.

**Что сохраняет:** список `ItemId` в порядке подбора.

**Защита от преждевременного сохранения:** флаг `_isReady` — стартовая синхронизация в `Start()` обновляет `_collectionOrder` без вызова `Save()`.

---

### `MedallionRoomTransformation`

Файл: `Assets/Scripts/Puzzle/Medallion/MedallionRoomTransformation.cs`. `[DefaultExecutionOrder(-3)]`, `ISaveable` (`_saveId = "medallion_room_transformation"`). Пост-solve трансформация комнаты 1.

Ключевая идея: последовательность стартует **не** в момент решения, а когда решение + подобран открывшийся медальон (`_medallionPickable` — `PickableItem`) — иначе запертая дверь софтлокнула бы сейв.

**Inspector:** `_puzzle` (PuzzleModeController), `_roomDoor` (закрывается и запирается `CloseAndLock`), `_crystalsRoot` + `_crystals[]` (кристаллы растут из нуля до авторских скейлов), `_crystalGrowthDurationMin/Max`, `_growthCurve`, `_iceCrackingClip/Volume` (луп при росте), `_ventGrill` + `_ventGrillAnimator` (параметр `IsFall`, стейт `Fall`), `_grillImpactClip/Volume`, `_medallionPickable`, `_doorCloseClip/Volume`, `_coldLightColor` + `_lightColorLerpDuration` (свет комнаты лерпится в холодный синий), `_whisperSource` (одноразовый шёпот из вентиля после падения решётки).

**Сохранение:** `hasFired` — при загрузке выполненного состояния финал применяется мгновенно, без звуков и анимаций.

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
            ├─ boxUI.MarkSolved() (блокировка извлечения)
            ├─ _solvedObject.SetActive(true)
            ├─ _animator.SetBool("IsOpen", true)
            ├─ _animator.Play("Opened", 0, 1f)  ← мгновенно, без воспроизведения Open
            └─ _controller.SetSolved()

Игрок подбирает монету
  └─ PickableItem.Interact() → ItemInspector.BeginInspection()
       └─ Клик → ConfirmPickup()
            ├─ InventorySystem.AddItem()  → Save() (автоматически в AddItem)
            └─ PickableItem.NotifyPickedUp() → _collected=true → Save()

Игрок открывает шкатулку (E или ЛКМ)
  └─ PuzzleModeController.EnterPuzzleMode() (камера, курсор, бар)
  └─ MedallionBoxInteraction.HandleEntered():
       ├─ звук первого осмотра (один раз)
       ├─ UIManager.OpenPanel(_panel)
       └─ boxUI.Populate(_medallionOrder)

Игрок перетаскивает монету из бара в лунку
  └─ MedallionBoxUI.HandleDrop(item, screenPos, out _)
       ├─ Проверка: item входит в _medallionOrder — иначе возврат в бар
       ├─ Physics.Raycast по _effectiveHoleLayer — определяет лунку
       ├─ MedallionHole.Fill() — анимация падения монеты
       └─ onComplete = CheckVictory() — если всё верно → OnPuzzleSolved

ЛКМ по занятой лунке → монета возвращается в инвентарь
  └─ MedallionBoxUI.Update() → TryRetrieveFromHole()
       ├─ InventorySystem.ReleaseAllReservations() + Compact()
       ├─ MedallionHole.Retrieve(riseHeight, riseDuration)
       │    ├─ Highlight(false) + _placedRenderer = null
       │    ├─ OnRetrieved → звук
       │    └─ RetrieveRoutine: монета поднимается и уничтожается
       └─ InventorySystem.AddItem() → Save()
            (если инвентарь полон — монета мгновенно возвращается FillImmediate)

Загадка решена
  └─ MedallionBoxInteraction.HandlePuzzleSolved():
       ├─ _puzzleSolved = true
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

На объектах `Hole_0`..`Hole_4` добавь компонент `MedallionHole`. Дай каждому `SphereCollider` на слое, указанном в `MedallionBoxUI._holeLayer`. При необходимости настрой `_coinPositionOffset` / `_coinRotationOffset` под визуал.

### 4 — MedallionBoxUI

Назначь в Inspector:

- `_holes` — перетащи `Hole_0`..`Hole_4` в порядке 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood
- `_coinPrefab` — prefab монеты
- `_holeLayer` — Layer лунок

### 5 — MedallionBoxInteraction

На объекте шкатулки:

- `_medallionOrder` — те же 5 `ItemData` в том же порядке, что и `_holes`
- `_panel`, `_solvedObject`, `_holes` — назначить соответствующие объекты (`_holes` при пустом массиве автособирается из детей)

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
