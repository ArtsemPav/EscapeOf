## Обзор

Головоломка кинопроектора: игрок находит кинобабину (Data_Babina) и перетаскивает её из инвентарного бара на якорь проектора. После установки нажатие кнопки Cylinder запускает проектор — включается слайдшоу `DecalSlideshow`, зацикленный звук и луч света. Мини-игры нет: решение мгновенно. Повторное нажатие Start после Stop перезапускает проектор, кнопка Stop выключает его.

**Интеграция с питанием (Lighting System):** всё — слайдшоу, звук, луч, вращение бабин — работает только при включённом питании. `ReelSpinner` реализует `IPowerConsumer` и реагирует на master power автоматически; `DecalSlideshow` (тоже `IPowerConsumer`) получает состояние питания через явный вызов `OnPowerStateChanged` из контроллера. Питание не «будит» остановленный кнопкой Stop проектор — перезапуск только кнопкой Start.

Работает совместно с `PuzzleModeController` (режим пазла) и `PuzzleInventoryBar` (drag&drop предметов).

---

## Скрипты

```
Assets/Scripts/Puzzle/Projector/
├── FilmProjectorPuzzleController.cs — контроллер пазла: дроп бабины, кнопки, сейв
└── ReelSpinner.cs                   — вращение бабины, IPowerConsumer
```

---

## FilmProjectorPuzzleController

**Файл:** `FilmProjectorPuzzleController.cs`
`[DefaultExecutionOrder(-7)]`. Реализует `IPuzzleDropHandler`, `IPuzzleDropTarget`, `IPuzzleExitGuard`, `ISaveable`. Вешается на объект проектора (или туда же, где `PuzzleModeController` — тогда подхватывается автоматически в `Awake`).

### Поля инспектора


| Поле                            | Тип                   | Default                           | Описание                                                                                 |
| ------------------------------- | --------------------- | --------------------------------- | ---------------------------------------------------------------------------------------- |
| `Save Id`                       | string                | `"film_projector_puzzle"`         | Ключ сохранения                                                                          |
| `Controller`                    | PuzzleModeController  | —                                 | Режим пазла. Если пусто — берётся с этого же GameObject                                  |
| `Slideshow`                     | DecalSlideshow        | —                                 | Слайдшоу на объекте Film; при установке бабины `ReelInstalled = true`                    |
| `Babina Item`                   | ItemData              | —                                 | Предмет-бабина (Data_Babina), который принимается в слот                                 |
| `Anchor Collider`               | Collider              | —                                 | Коллайдер якоря дропа — цель рейкаста при отпускании предмета                            |
| `Anchor Transform`              | Transform             | —                                 | Точка спавна визуала / ghost-превью                                                      |
| `Ghost Prefab`                  | GameObject            | —                                 | Префаб ghost-превью. Если пуст — берётся `inspectionPrefab` предмета                     |
| `Reel Visuals`                  | `GameObject[]`        | —                                 | Меши бабин, скрытые до установки (babina 1 / babina 2); включаются вместо спавна префаба |
| `Reel Spinners`                 | `ReelSpinner[]`       | —                                 | Спиннеры бабин; при установке им разрешается вращение через `SetSpinning`                |
| `Insert Clip` / `Insert Volume` | AudioClip, float(0–1) | —, `1`                            | Звук установки бабины                                                                    |
| `Start Button`                  | ButtonPressAnimation  | —                                 | Кнопка запуска (Cylinder с ButtonPressAnimation)                                         |
| `Stop Button`                   | ButtonPressAnimation  | —                                 | Кнопка выключения                                                                        |
| `Anchor Layer`                  | LayerMask             | —                                 | Слой якоря для Raycast (например Interactable Layer)                                     |
| `Ghost Material`                | Material              | —                                 | Материал ghost-превью. Если пуст — `CardLamp_Ghost.mat` из Resources                     |
| `Drop Hint`                     | string                | `"Установить бабину"`             | Подсказка при наведении предмета на якорь                                                |
| `Hint No Item`                  | string                | `"Установите бабину в проектор."` | Подсказка при нажатии Start без бабины                                                   |


### Сохранение

`SaveData { solved, placed }`. При загрузке `placed = placed || solved` — решённый пазл всегда с бабиной. В `Start()`: если `placed` — включаются меши бабин; если `solved` — применяется `ApplyReelInstalled()` и `controller.SetSolved()`.

### Блок-схема

```
HandleDrop(item, screenPos)                 ← дроп из PuzzleInventoryBar
├── CanAccept? (предмет == Babina Item, не solved, не placed)
├── Raycast по Anchor Layer попадает в Anchor Collider?
├── _isPlaced = true → SaveManager.Save()
├── SetReelVisualsActive(true)  ← меши бабин вместо спавна
└── PlaySFX(_insertClip)

HandleStartButtonPressed()                  ← нажатие Start
├── _isSolved и проектор стоит:
│   └── повторный запуск: StartProjector() + AllowReelSpinning(true)
├── !_isPlaced → PopupMessage(_hintNoItem), выход
└── РЕШЕНО (первый раз):
    ├── _isSolved = true
    ├── ApplyReelInstalled()  ← slideshow.ReelInstalled = true,
    │                           спиннерам SetSpinning(true),
    │                           slideshow.OnPowerStateChanged(текущее питание)
    ├── controller.SetSolved()  ← выход из режима пазла + сохранение
    └── SaveManager.Save()

HandleStopButtonPressed()                   ← нажатие Stop
└── если solved и запущен: slideshow.StopProjector() + AllowReelSpinning(false)
    (питание проектор не будит — перезапуск только Start)

Update() — ghost-превью:
├── активно только в режиме пазла, пока бабина не установлена
└── PuzzleInventoryBar.IsDragging && CanAccept && курсор над якорем
    → ghost (инстанс с отключёнными коллайдерами и материалом CardLamp_Ghost)
```

### Прочие реализации интерфейсов

- `CanExitPuzzle() => true` — проектор не блокирует выход из пазла.
- `GetDropHint() => _dropHint` — текст подсказки при наведении предмета.

---

## ReelSpinner

**Файл:** `ReelSpinner.cs`
Реализует `IPowerConsumer` (Lighting System). Вращает объект вокруг локальной оси — имитация вращения кинобабины.

### Особенность включения

В `Awake()` компонент регистрируется в `LightingSystem` и **отключает сам себя** (`enabled = false`) — пока питание не придёт. Дальше активен только при одновременном выполнении двух условий:

```
enabled = isPowered && (_spinOnPower || _spinRequested)
```

- `_isPowered` — приходит от `LightingSystem` через `OnPowerStateChanged` (при регистрации доставляется текущее состояние).
- `_spinOnPower` — разрешать ли вращение автоматически при включении света.
- `_spinRequested` — «разрешение» извне через `SetSpinning()` (например, от `FilmProjectorPuzzleController` после установки бабины). Сохраняется и применяется при последующем включении света.

### Поля инспектора


| Поле                 | Тип     | Default   | Описание                                                                                          |
| -------------------- | ------- | --------- | ------------------------------------------------------------------------------------------------- |
| `Local Axis`         | Vector3 | `(1,0,0)` | Локальная ось вращения бабины                                                                     |
| `Degrees Per Second` | float   | `120`     | Скорость вращения, град/сек                                                                       |
| `Spin On Power`      | bool    | `true`    | Включать ли вращение автоматически при включении света. Если `false` — только через `SetSpinning` |


### Публичный API

```csharp
public void OnPowerStateChanged(bool isPowered);  // IPowerConsumer, вызывается LightingSystem
public void SetSpinning(bool spinning);           // разрешение вращения извне
public bool IsPowered { get; }                    // текущее состояние питания
```

---

## Связанные системы

- **DecalSlideshow** (`Assets/Scripts/Decal/DecalSlideshow.cs`) — `IPowerConsumer`; слайдшоу, зацикленный звук и луч света стартуют от питания. Ключевой флаг: `ReelInstalled` (устанавливается контроллером), плюс `StartProjector()` / `StopProjector()`.
- **PuzzleModeController** — режим пазла; `OnEntered`/`OnExited` скрывают ghost, `SetSolved()` завершает режим.
- **PuzzleInventoryBar** — drag&drop из инвентаря; контроллер читает `IsDragging`/`DraggedItem` для ghost-превью.
- **ButtonPressAnimation** — физическое нажатие кнопок Start/Stop, события `OnPressed`.
- **LightingSystem** — master power; см. [@ id="/Pages/Private/Lighting System.md" label="Lighting System"].

## Настройка

1. На корне проектора: `FilmProjectorPuzzleController` (+ `PuzzleModeController` на том же объекте или в поле `Controller`).
2. Назначить `Slideshow` (DecalSlideshow на Film), `Babina Item` (Data_Babina), якорь (коллайдер + Transform), меши бабин и их `ReelSpinner`.
3. Кнопки Start/Stop — Cylinder-объекты с `ButtonPressAnimation`, повесить их `OnPressed` автоматически через ссылки в инспекторе.
4. Убедиться, что питание активировано (Electric Puzzle) — иначе проектор стоит даже с установленной бабиной.