## Что делает загадка

Игрок управляет лабораторной станцией с пятью устройствами: **центрифуга**, **спиртовка**, **миксер**, **анализатор** и **мусорка** (`TrashController`). Все ингредиенты выдаются в **неопознанном** виде — их нужно либо опознать в анализаторе, либо использовать напрямую (неопознанные дублируют функционал опознанных). Для победы нужно синтезировать и опознать `SerumColba`.

---

## Конфигурация рецептов (`ChemicalSynthesisController`)

Файл: `Assets/Scripts/Puzzle/ChemicalPuzzle/ChemicalSynthesisController.cs`. Вся цепочка синтеза задаётся centrally на контроллере — полями `SynthesisStep[] _synthesisSteps`. Каждая запись `SynthesisStep` — один шаг обработки на одном устройстве; значения инжектируются в контроллеры устройств в `Awake()` (`ApplySynthesisRecipe`).

`SynthesisStep` (заполняются только поля выбранного устройства, остальные игнорируются):


| Поле                 | Устройство          | Описание                                                                 |
| -------------------- | ------------------- | ------------------------------------------------------------------------ |
| `label`              | любое               | Подпись шага только для Inspector, на рантайм не влияет                   |
| `device`             | любое               | `Burner / Centrifuge / Mixer / Analyzer`                                  |
| `burnerInputs`       | Burner              | Правильные входы (все дают `successResult`, остальное — `spoiledResult`)  |
| `centrifugeInput`    | Centrifuge          | Один вход → один выход. Дополнительные маппинги = дополнительные шаги     |
| `successResult` / `spoiledResult` | Burner / Centrifuge | Результаты при верном/неверном входе                             |
| `mixerIngredients`   | Mixer               | Ингредиенты рецепта (порядок не важен, важен факт наличия)                |
| `mixerResult`        | Mixer               | Выход рецепта. Один шаг = один рецепт; первый полностью совпавший выигрывает |
| `analyzerWinItemId`  | Analyzer            | `ItemId` колбы, дающей победу (default: `"SerumColba"`)                   |


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


**Принцип:** все устройства принимают оба варианта и дают одинаковый результат через общую `_equivalenceMap` (поле на `ChemicalSynthesisController`) + метод `Normalize()` из `IChemicalPuzzleContext`. Анализатор опознаёт неопознанный предмет и возвращает идентифицированную версию. Финальный шаг — обязательный анализ `UnknownSerumColba`.

`_acceptedItems` — глобальный белый список (`ItemData[]`): всё, что можно класть в устройства. Перечисляются **опознанные** версии — Unknown-варианты нормализуются автоматически. Все входы/выходы шагов `SynthesisStep` должны присутствовать в этом списке.

---

## Цепочка синтеза

```
UnknownDirtColba  (стартовый предмет в мире)
        │
        │  Центрифуга  (UnknownDirtColba нормализуется → DirtColba → SuccessResult)
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

> Любой шаг можно выполнить неопознанным предметом — устройства нормализуют его к опознанному при обработке. Точные выходы каждого шага настраиваются в `_synthesisSteps` (см. таблицу выше).

---

## Устройства

### Центрифуга — `CentrifugeController`

- Три независимых слота (`_slotTransforms`, Colba_Centrifuga1/2/3). Принимает предметы из глобального `_acceptedItems`.
- Кнопка `button2` (`ButtonPressAnimation`) запускает цикл (`_duration`, default 5 сек). Запуск — при хотя бы одном занятом слоте.
- **Логика результата:** маппинги задаются через шаги `Centrifuge` в `_synthesisSteps` (перезаписывают сериализованный fallback `_cleanMappings`). Нормализованный вход, совпавший с `centrifugeInput` шага, даёт `successResult`; иначе — `spoiledResult`.
- Результат может быть рероллен в случайный slag из `_unknownSlagVariants` (см. «Slag-варианты» ниже).
- Экран через `CentrifugeScreenController`, вращение колеса — `WheelRotationSpeed` (задаётся на контроллере, default 360°/с). Колбу можно забрать до старта.
- 3D-звук цикла: `_spinLoopClip` (`_spinLoopMinDistance` 0.5 / `_spinLoopMaxDistance` 6).

### Спиртовка — `BurnerController`

- Один слот (`_flaskSpot` / `_spotCollider`). Принимает предметы из глобального `_acceptedItems`.
- Нагрев начинается автоматически после дропа (`ProcessLoadedFlask`) — кнопок нет.
- **Логика результата:** шаги `Burner` из `_synthesisSteps` задают `burnerInputs → successResult` (нормализация через `_equivalenceMap`); иначе — `spoiledResult` (случайный slag).
- Пламя VFX (`_flameVFX`) активируется во время нагрева. 3D-звук горения: `_burnLoopClip`.

### Миксер — `MixerController`

- Накапливает колбы по одной до `_portionsToExport` (2 шт), затем экспорт результата.
- **Рецепты** задаются шагами `Mixer` в `_synthesisSteps` (первый полностью совпавший выигрывает):
  - `CleanColba + stabilazerColba` → `mixerResult` (UnknownSubstrateColba)
  - `ActivatorColba + ReactiveColba` → `mixerResult` (UnknownSerumColba)
- Нормализация через общую `_equivalenceMap` — неопознанные ингредиенты засчитываются в рецепты.
- **Slag-логика:** если хотя бы один предмет в slag-списке (шаги Mixer → `_mixerSlagItems`, глобально на контроллере) — результат всегда `_mixerSpoiledResult` (случайный slag-вариант). `IsSlag` тоже нормализует.
- `SetGlow` не отключает `MeshRenderer`, если `_glowRenderer == _hoverHighlightRenderer` — защита от исчезновения меша.
- Пустая колба (`_amptyColba`) возвращается в инвентарь после каждого дропа, результат — после инспекции (replace-in-place одной пустой колбы через `replaceTarget`).

### Анализатор — `AnalyzerController`

- Один слот на `Colba_Analize` (`_centerCollider`). Принимает предметы из глобального `_acceptedItems` (`CanDrop` = `context.IsAccepted`, с нормализацией).
- Идентификация — через общий `_equivalenceMap` контроллера: `Identify(item) = context.Normalize(item)`. Отдельного `_identificationMap` на анализаторе больше **нет**.
- Кнопка `button1` запускает цикл: рука спускается (`_diveDepth`, `_diveMoveDuration`) → прогресс 0→100% (`_scanDuration`) → результат на экране (`_resultDisplayDuration`) → рука поднимается.
- **Порядок событий:** `OnSuccess`/`OnFail` стреляют **до** `OnFlaskReturned` — чтобы `ChemicalSynthesisController` успел выставить `_pendingInventoryCleanup = true` до прихода результирующей колбы.
- Возвращает `Identify(flask)` — идентифицированную версию (на экране тоже настоящее имя).
- Если `identified.ItemId == _winItemId` (задаётся шагом `Analyzer`; fallback `_winItemId = "SerumColba"`) → `OnSuccess`.

### Мусорка — `TrashController`

- Drop-zone коллайдер + подсветка при наведении (`DeviceHighlightHelper`), звук `_dropClip`.
- Логика уничтожения — в `ChemicalSynthesisController`: принимаются любые «известные пазлу» предметы (`IsPuzzleItem`); взамен возвращается `_amptyColba`.

---

## Slag-варианты

Поле `_unknownSlagVariants` (пул `UnknownSpoiledColba*`): когда устройство возвращает провальный результат, принадлежащий пулу, `RandomizeSlag()` перероллит его в случайную запись — игрок каждый раз получает разный шлак.

---

## Завершение загадки

После успешного анализа `UnknownSerumColba` события идут строго по порядку:

```
AnalyzerController.AnalyzeCoroutine:
  1. OnSuccess?.Invoke()          → _isSolved = true, _pendingInventoryCleanup = true, играет звук
  2. OnFlaskReturned?.Invoke()    → колба ставится в очередь _pendingResults
       → после инспекции (клик по панели):
            ├─ ClearPuzzleItemsFromInventory() — удаляет все предметы из _puzzleItems
            ├─ _pendingResults.Clear()
            ├─ InventorySystem.Compact()
            ├─ _puzzleMode.SetSolved()   — камера уходит к игроку
            └─ SaveManager.Save()        — сохранение
```

`_pendingResults` — очередь результатов устройств: несколько колб (например, 3 из центрифуги) показываются инспекциями по одной, ни один результат не теряется — включая колбу анализатора.

`_puzzleItems` — промежуточные и ингредиентные колбы, удаляемые при победе. **Победную колбу не включать** — она остаётся наградой.

---

## Архитектура скриптов

```
ChemicalSynthesisController      ← оркестратор, IPuzzleDropHandler, ISaveable, IChemicalPuzzleContext, IPuzzleExitGuard
├── PuzzleModeController         ← камера, курсор, Esc, инвентарь-бар
├── CentrifugeController         ← ChemicalDeviceBase
│   └── CentrifugeScreenController
├── BurnerController             ← ChemicalDeviceBase
├── MixerController              ← ChemicalDeviceBase
│   └── LiquidWobble             ← shader-driven жидкость
├── AnalyzerController
│   └── AnalyzerScreenController
└── TrashController
```

**`ChemicalDeviceBase`** — абстрактный базовый класс: `IsBusy`, `OnProcessComplete`, `CompleteWithResult/s`.

**`IdentificationEntry`** — struct `{ ItemData unknown; ItemData identified; }` — используется в `_equivalenceMap` контроллера (общая для всех устройств и анализатора).

**`DeviceTooltipInfo`** — тексты тултипов устройств (`_deviceTooltips`), показываются при наведении (включая мусорку).

**`IChemicalPuzzleContext`** — интерфейс контекста: `IsAccepted`, `Normalize`, `IsPuzzleItem`. Инжектируется в устройства через `Initialize(context)`.

### Поток событий

```
Player drops item
        ▼
ChemicalSynthesisController.HandleDrop()
        │  raycast по _deviceLayerMask, closest collider → устройство
        │  (также обрабатываются hover/tooltip/ghost-превью и ЛКМ-возврат колб)
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
| Trash                   | drop-zone / TrashController | `IsPuzzleItem(item)`                  |


Ghost-превью строится из `item.inspectionPrefab` с опциональным `_ghostMaterial`. Коллайдеры на ghost отключены. Центрифуга и анализатор дополнительно анимируют ghost bob-движением.

`UpdateClickRetrieve` — ЛКМ без drag → raycast → возвращает колбу из центрифуги или анализатора пока устройство idle.

---

## Сохранение

`ChemicalSynthesisController` реализует `ISaveable` (`_saveId = "chemical_synthesis"`). Сохраняет только `isSolved`; при загрузке решённого состояния сразу вызывает `_puzzleMode.SetSolved()`. **Сохранение происходит только после очистки инвентаря** — не в момент анализа.

---

## Читы / отладка

`ChemicalSynthesisController.AutoSolve()` — публичный метод (Play Mode): мгновенно завершает пазл как при анализе победной колбы — звук успеха, победная колба в инвентарь, очистка промежуточных предметов, `SetSolved()`, сохранение. Возвращает `false`, если пазл уже решён или колба не найдена. Используется editor-читом `ChemicalPuzzleUnlockTool` (`Tools/PuzzlesCheats`).

---

## Самодостаточность префаба

Префаб `/Assets/Prefabs/puzzle/ChemicalPuzzle/ChemicalPuzzle.prefab` полностью самодостаточен. Все данные запечены напрямую — никакие scene overrides не нужны.

`ChemicalSynthesisController.Awake()` автоматически разрешает внутренние ссылки, если не заданы в Inspector:

- Устройства через `GetComponentInChildren<T>(true)`
- `_centrifugeWheel` через `CentrifugeController.WheelTransform`
- `_analyzerSlot` через `AnalyzerController.DropZoneCollider`
- `_burnerSlot`, `_mixerSlot` через `GetComponent<Collider>()` на устройстве
- `_trash` через `GetComponentInChildren<TrashController>(true)`

Все обращения к синглтонам (`InventorySystem`, `AudioManager`, `SaveManager`, `ItemInspector`) null-safe через `?.`.

---

## Что нужно сделать вручную при размещении на новой сцене

- Убедиться что `Camera.main` есть в сцене.
- Назначить `_successClip` и `_failClip` в `ChemicalSynthesisController` для звуков победы/поражения.
- Если в сцене несколько экземпляров загадки — изменить `_saveId` у каждого (по умолчанию `"chemical_synthesis"`).
- Слой `Interactable Layer` должен существовать в проекте — используется `_deviceLayerMask` и коллайдерами устройств.
- Вся цепочка синтеза настраивается в `_synthesisSteps` контроллера — устройства перезапишут свои локальные значения из шагов в `Awake()`.
