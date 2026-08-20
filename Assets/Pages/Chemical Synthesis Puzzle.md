## Что делает загадка

Игрок управляет лабораторной станцией с четырьмя устройствами: **центрифуга**, **спиртовка**, **миксер** и **анализатор**. Все ингредиенты выдаются в **неопознанном** виде — их нужно либо опознать в анализаторе, либо использовать напрямую (неопознанные дублируют функционал опознанных). Для победы нужно синтезировать и опознать `SerumColba`.

---

## Система неопознанных предметов

Каждый химический предмет существует в двух вариантах:


| Опознанный        | Неопознанный             | Отличие                  |
| ----------------- | ------------------------ | ------------------------ |
| `DirtColba`       | `UnknownDirtColba`       | Только описание и ItemId |
| `CleanColba`      | `UnknownCleanColba`      | —                        |
| `stabilazerColba` | `UnknownStabilazerColba` | —                        |
| `ReactiveColba`   | `UnknownReactiveColba`   | —                        |
| `SubstrateColba`  | `UnknownSubstrateColba`  | —                        |
| `ActivatorColba`  | `UnknownActivatorColba`  | —                        |
| `SerumColba`      | `UnknownSerumColba`      | Финальный — нужен анализ |


**Принцип:** Все устройства (центрифуга, спиртовка, миксер) принимают оба варианта и дают одинаковый результат через `_equivalenceMap` + метод `Normalize()`. Анализатор опознаёт неопознанный предмет и возвращает идентифицированную версию. Финальный шаг — обязательный анализ `UnknownSerumColba`.

---

## Цепочка синтеза

```
UnknownDirtColba  (стартовый предмет в мире)
        │
        │  Центрифуга  (UnknownDirtColba нормализуется → DirtColba → CleanResult)
        ▼
UnknownCleanColba  +  UnknownStabilazerColba  (ингредиент из мира)
        │
        └──────────────────┬────────────────────┘
                           │  Миксер (рецепт 1: CleanColba + stabilazerColba)
                           ▼
              UnknownSubstrateColba
                           │
                           │  Спиртовка
                           ▼
              UnknownActivatorColba  +  UnknownReactiveColba  (ингредиент из мира)
                           │
                           └──────────┬──────────────────────┘
                                      │  Миксер (рецепт 2: ActivatorColba + ReactiveColba)
                                      ▼
                          UnknownSerumColba
                                      │
                                      │  Анализатор  ← ОБЯЗАТЕЛЬНО опознать
                                      ▼
                              SerumColba  →  ПОБЕДА
```

> Любой шаг можно выполнить неопознанным предметом — устройства нормализуют его к опознанному при обработке.

---

## Устройства

### Центрифуга — `CentrifugeController`

- Три независимых слота. Принимает все предметы из `_acceptedItems` (15 шт — опознанные + Unknown-варианты).
- Кнопка `button2` запускает цикл (`_duration` сек).
- **Логика результата:** `Normalize(_loadedFlasks[i]) == _cleanInputItem` → `_cleanResult`; иначе → `_spoiledResult`.
  - `_cleanInputItem = DirtColba` — прямое сравнение по ссылке, исключает ложные совпадения по ItemId.
  - `_equivalenceMap`: `UnknownDirtColba → DirtColba` — чтобы неопознанная грязная жидкость давала тот же результат.
- `_cleanResult = UnknownCleanColba` — результат специально неопознанный, игрок не знает что получил.
- Экран через `CentrifugeScreenController`, колбу можно забрать до старта.

### Спиртовка — `BurnerController`

- Один слот. Принимает `_droppableItems` (белый список).
- Нагрев начинается автоматически после дропа — кнопок нет.
- **Логика результата:** `IsSuccessItem` (с нормализацией через `_equivalenceMap`) → `_successResult`; иначе → `_spoiledResult`.
  - `_equivalenceMap`: `UnknownSubstrateColba → SubstrateColba`.
  - `_successItems`: `[SubstrateColba]` → `_successResult`: `UnknownActivatorColba`.
- Пламя VFX активируется во время нагрева.

### Миксер — `MixerController`

- Накапливает колбы по одной до `_portionsToExport` (2 шт).
- **Рецепты** (`MixingRecipe[]`, первый совпадающий выигрывает):
  - `CleanColba + stabilazerColba` → `UnknownSubstrateColba`
  - `ActivatorColba + ReactiveColba` → `UnknownSerumColba`
- Нормализация через `_equivalenceMap` (5 маппингов) — неопознанные ингредиенты засчитываются в рецепты.
- **Slag-логика:** если хотя бы один предмет в `_slagItems` (`SpoiledColba`) — результат всегда `SpoiledColba`. `IsSlag` тоже нормализует.
- `SetGlow` не отключает `MeshRenderer` если `_glowRenderer == _hoverHighlightRenderer` — защита от исчезновения меша.
- Пустая колба (`_amptyColba`) возвращается в инвентарь после каждого дропа, результат — после инспекции.

### Анализатор — `AnalyzerController`

- Один слот на `Colba_Analize`. Принимает предметы двумя способами:
  1. Есть маппинг в `_identificationMap` → `Identify(item) != item` → принимает.
  2. Явно в `_acceptedItems` (только опознанные, для «повторного анализа»).
- Кнопка `button1` запускает цикл: рука спускается → прогресс 0→100% → результат на экране → рука поднимается.
- **Порядок событий:** `OnSuccess/OnFail` стреляет **до** `OnFlaskReturned` — чтобы `ChemicalSynthesisController` успел выставить `_pendingInventoryCleanup = true` до прихода результирующей колбы.
- Возвращает `Identify(flask)` — идентифицированную версию.
- Если `identified.ItemId == _winItemId` (`"SerumColba"`) → `OnSuccess`.
- `**_identificationMap**` (7 записей, порядок важен из-за `Array.size = 6` override в сцене):
  - `[0]` UnknownReactiveColba, `[1]` UnknownStabilazerColba, `[2]` UnknownCleanColba
  - `[3]` UnknownSubstrateColba, `[4]` UnknownActivatorColba, `[5]` **UnknownSerumColba** ← обязан быть в 0–5
  - `[6]` UnknownDirtColba ← срезается scene override (не критично, центрифуга принимает напрямую)

---

## Завершение загадки

После успешного анализа `UnknownSerumColba` события идут строго по порядку:

```
AnalyzeCoroutine:
  1. OnSuccess?.Invoke()          → _isSolved = true, _pendingInventoryCleanup = true, играет звук
  2. OnFlaskReturned?.Invoke()    → SerumColba добавляется в инвентарь
                                  → ClearPuzzleItemsFromInventory() — удаляет все 18 предметов
                                  → _puzzleMode.SetSolved()         — камера уходит к игроку
                                  → SaveManager.Save()              — сохранение
```

`**_puzzleItems**` (18 предметов, очищаются при победе):

Опознанные: `DirtColba, ReactiveColba, stabilazerColba, CleanColba, SubstrateColba, ActivatorColba`
Unknown: все 7 Unknown-вариантов
Прочее: `SpoiledColba, Ampty, DirtBottle, ReactiveBottle, stabilazerBottle`

---

## Архитектура скриптов

```
ChemicalSynthesisController      ← оркестратор, IPuzzleDropHandler, ISaveable
├── PuzzleModeController         ← камера, курсор, Esc, инвентарь-бар
│
├── CentrifugeController         ← ChemicalDeviceBase
│   └── CentrifugeScreenController
├── BurnerController             ← ChemicalDeviceBase
├── MixerController              ← ChemicalDeviceBase
│   └── LiquidWobble             ← shader-driven жидкость
└── AnalyzerController
    └── AnalyzerScreenController
```

`**ChemicalDeviceBase**` — абстрактный базовый класс: `IsBusy`, `OnProcessComplete`, `CompleteWithResult/s`.

`**IdentificationEntry**` — struct `{ ItemData unknown; ItemData identified; }` — используется в `_equivalenceMap` всех устройств и `_identificationMap` анализатора.

### Поток событий

```
Player drops item
        ▼
ChemicalSynthesisController.HandleDrop()
        │  raycast по _deviceLayerMask, closest collider → устройство
        ▼
Device.LoadFlask(item)  [+ ProcessLoadedFlask() для спиртовки]
        ▼
Device fires OnProcessComplete
        ▼
ChemicalSynthesisController callback
        │  ItemInspector.BeginInspection → игрок закрывает панель
        ▼
InventorySystem.AddItem(result)
```

---

## Hover Preview


| Кадр                    | Метод                       | Условие показа                        |
| ----------------------- | --------------------------- | ------------------------------------- |
| `UpdateCentrifugeHover` | `centrifugaWheel` + дети    | `Accepts(item) && !IsFull && !IsBusy` |
| `UpdateMixerHover`      | `_mixerSlot` коллайдер      | `Accepts(item) && !IsFull && !IsBusy` |
| `UpdateBurnerHover`     | `BurnerController` в parent | `CanDrop(item) && !IsBusy`            |
| `UpdateAnalyzerHover`   | `_analyzerSlot` коллайдер   | `CanDrop(item) && !_isSolved`         |


Ghost-превью строится из `item.inspectionPrefab` с опциональным `_ghostMaterial`. Коллайдеры на ghost отключены. Центрифуга и спиртовка дополнительно анимируют ghost bob-движением.

`UpdateClickRetrieve` — ЛКМ без drag → raycast → возвращает колбу из центрифуги или анализатора пока устройство idle.

---

## Сохранение

`ChemicalSynthesisController` реализует `ISaveable` (`_saveId = "chemical_synthesis"`). Сохраняет только `isSolved`. **Сохранение происходит только после очистки инвентаря** — не в момент анализа.

---

## Самодостаточность префаба

Префаб `/Assets/Prefabs/puzzle/ChemicalPuzzle/ChemicalPuzzle.prefab` полностью самодостаточен. Все данные запечены напрямую — никакие scene overrides не нужны.

`ChemicalSynthesisController.Awake()` автоматически разрешает внутренние ссылки если не заданы в Inspector:

- Устройства через `GetComponentInChildren<T>()`
- `_centrifugeWheel` через `CentrifugeController.WheelTransform`
- `_analyzerSlot` через `AnalyzerController.DropZoneCollider`
- `_burnerSlot`, `_mixerSlot` через `GetComponent<Collider>()` на устройстве

Все обращения к синглтонам (`InventorySystem`, `AudioManager`, `SaveManager`, `ItemInspector`) null-safe через `?.`.

---

## Что нужно сделать вручную при размещении на новой сцене

- Убедиться что `Camera.main` есть в сцене.
- Назначить `_successClip` и `_failClip` в `ChemicalSynthesisController` для звуков победы/поражения.
- Если в сцене несколько экземпляров загадки — изменить `_saveId` у каждого (по умолчанию `"chemical_synthesis"`).
- Слой `Interactable Layer` должен существовать в проекте — используется `_deviceLayerMask` и коллайдерами устройств.