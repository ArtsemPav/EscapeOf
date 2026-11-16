<!-- version: 8 -->

## Обзор

Головоломка «Комната с картинами» (`PaintPuzzle`). Игрок стоит в комнате управления и через глазок наблюдает за комнатой с четырьмя нишами (Q1–Q4). Задача — настроить высоту картин, цвет линз прожекторов и схему питания так, чтобы на всех четырёх полотнах одновременно проявились скрытые символы. Когда все символы видны, открывается скрытая дверь.

---

## Префаб

`Assets/Prefabs/puzzle/paint/PRF_PaintPuzzle.prefab`

```
PaintPuzzle                   ← LoopPuzzlePowerCircuit, LoopPuzzleController (IPowerConsumer)
├── Peephole                  ← глазок (+ BoxCollider, слой Interactable Layer)
│   ├── PeepholeCamera        ← CinemachineCamera (вид через глазок)
│   ├── Cube                  ← визуальная заглушка глазка
│   ├── TVCamera..TVCamera6   ← Camera (чистые, без скриптов)
├── tv
│   └── TV_CCTV_01            ← PeepholeTVCamera, TVGlitchEffect
│       ├── PositionSwitch    ← TVChannelButton, ButtonPressAnimation, слой Interactable Layer
│       ├── pPlane1           ← экран; runtime-материал TVGlitch (slot 0)
│       └── TV_CCTV_02        ← корпус телевизора
├── ControlRoom
│   ├── PowerButtons
│   │   └── Button_S1..S6    ← LoopPuzzleButton, слой Interactable Layer
│   ├── ColumnButtons
│   │   └── Button_Q1..Q4    ← PaintingColumnTrigger, слой Interactable Layer
│   ├── LensButtons
│   │   └── Button_L1..L4*   ← SpotlightLensButton, слой Interactable Layer
│   └── HiddenDoor            ← LoopPuzzleHiddenDoor
└── PaintingRoom
    ├── Spotlights
    │   └── Spotlight_L1..L4  ← PaintingSpotlight
    └── Paintings
        └── PaintingColumn_Q1..Q4  ← PaintingColumn
            ├── Symbol_Omega/Psi/Sigma/Delta  ← SpriteRenderer + SymbolFader
            └── ...
```

- `LensButtons` для L3 не нужна — синтетический прожектор.

---

## Скрипты

| Файл | Назначение |
| --- | --- |
| `LensColor.cs` | Enum: `None / Red / Blue / Yellow / Green` |
| `PaintingSpotlight.cs` | Прожектор с линзой и синтезом |
| `SpotlightLensButton.cs` | Кнопка смены линзы: 4 такта (0→1→2→3→0), поворот физического объекта, ISaveable |
| `LoopPuzzleButton.cs` | Рубильник питания S1–S6, текстурированный emission-гло, блокируется при решении |
| `LoopPuzzlePowerCircuit.cs` | Схема питания, Lights Out каскад, `LockAllSwitches()`, события `OnPowerChanged` и `OnMasterToggled` |
| `LoopPuzzleController.cs` | Центральный контроллер условий (ISaveable, IPowerConsumer), `AutoSolve()` для читов, `RandomizeColumns()` с инвариантом решаемости, сброс при выключении S6 |
| `LoopPuzzleHiddenDoor.cs` | Скользящая дверь (ISaveable) |
| `PaintingColumn.cs` | Ниша с изменяемой высотой (ISaveable), глобальный счётчик `IsAnyMoving`, события `OnMoveFinished` / `OnHeightChanged` |
| `PaintingColumnTrigger.cs` | Кнопка смены высоты: glow во время движения, флаги блокировки. `CanInteract() => enabled` |
| `PaintingRoomLightSwitch.cs` | Выключатель света, блокируется при решении |
| `SymbolFader.cs` | Плавное появление символа с настраиваемым HDR-цветом свечения |
| `LightsOutPanel.cs` | Кастомный Inspector для `LoopPuzzlePowerCircuit` — визуальный редактор матрицы смежности (в `Assets/Scripts/Editor/`) |
| `SolvedLightFade.cs` | Плавный фейд света решённой комнаты (`SolvedLight`) |
| `ButtonPressAnimation.cs` | Физическое нажатие кнопки |
| `PeepholeTVCamera.cs` | RenderTexture, циклический переключатель TV-камер, TVGlitch-материал на экране, blackout при отсутствии питания |
| `TVGlitchEffect.cs` | Случайные глитч-события: анимирует `_GlitchAmount` на материале экрана |
| `TVChannelButton.cs` | `IInteractable` на кнопке ТВ; вызывает `PeepholeTVCamera.NextCamera()` |
| `PaintCinematicSignals.cs` | Мост Animation Events кинематика → ящики, свет, выход из режима загадки |

---

## Матрица активации


| Ниша          | Цвет луча                   | Высота | Символ |
| ------------- | --------------------------- | ------ | ------ |
| Q1 (Младенец) | Red (L1)                    | High   | Ω      |
| Q2 (Воин)     | Yellow (L2)                 | Low    | Ψ      |
| Q3 (Судья)    | Green (L2 Blue + L4 Yellow) | Mid    | Σ      |
| Q4 (Тень)     | Blue (L4)                   | Low    | Δ      |


Значение символа Δ (Q4) не задано — уточнить с дизайнером.

---

## Фазы загадки

1. **Питание**: включить S6 (мастер) → нажимать S1–S5 так, чтобы горели все пять (см. «Схема питания»). Пока не все прожекторы запитаны, кнопки высоты картин заблокированы.
2. **Символы**: через ТВ-глазок настроить высоты картин (Q1–Q4) и линзы (L1/L2/L4) — все 4 символа должны быть видны одновременно.
3. **Кинематик**: общая `PuzzleSolvedCinematic` — затемнение, камера на решённую комнату, выезд ящиков-награды, возврат управления, сохранение.

---

## Схема питания (LoopPuzzlePowerCircuit)

Рубильники S1–S6, S6 — мастер (если выкл., все прожекторы выкл.).

### Правила питания — цепочка пар

Каждый прожектор требует свою **пару соседних рубильников** по цепочке:


| Прожектор | Условие питания (AND) |
| --------- | --------------------- |
| L1        | S1 **и** S2           |
| L2        | S2 **и** S3           |
| L3        | S3 **и** S4           |
| L4        | S4 **и** S5           |


Все 4 прожектора горят **только когда все пять рубильников включены** — цель прозрачна.

### Lights Out — кольцо C5

Нажатие рубильника переключает его самого и двух соседей по кольцу (S1↔S2↔S3↔S4↔S5↔S1). Матрица задаётся в Inspector через визуальный редактор `LightsOutPanel`.

Матрица обратима над GF(2) (ранг 5) — решаема из любого состояния, решение единственно. Решение: **нажать каждую кнопку по одному разу** — каждый рубильник включится трижды (сам + от двух соседей) и останется гореть. Проверяется кнопкой **«Проверить (GF(2) анализ)»** в Inspector.

### Сброс при выключении S6 (`ResetPuzzleState`)

Выключение S6 сбрасывает пазл: S1–S5 гаснут без каскада, колонны возвращаются к стартовым высотам сессии, все символы скрываются.

### Блокировка при решении

При решении загадки `LoopPuzzlePowerCircuit.LockAllSwitches()` блокирует все S1–S6.

---

## Блокировки взаимодействий

Три независимых источника блокировки `PaintingColumnTrigger` (Q1–Q4):


| Флаг                     | Причина                        | Кто ставит                                                |
| ------------------------ | ------------------------------ | --------------------------------------------------------- |
| `_isLockedByPower`       | S6 выключен                    | `OnMasterToggled` из `LoopPuzzlePowerCircuit`             |
| `_isLockedBySpotlights`  | Не все 4 прожектора запитаны   | `OnAllSpotlightsPoweredChanged` из `LoopPuzzleController` |
| `_isLockedByPuzzle`      | Загадка решена                 | `LoopPuzzleController` → `SetLocked(true)`                |


Фаза с высотами картин недоступна, пока не решена фаза рубильников.

При решении `LoopPuzzleController.LockAllInteractions()` блокирует: S1–S6 (`_powerCircuit.LockAllSwitches()`), выключатель света `_roomLightSwitch`, все Q1–Q4 и линз-кнопки в детях.

---

## LoopPuzzleController

Реализует `ISaveable` (`_saveId = "loop_puzzle_controller"`) и `IPowerConsumer`.

### Условие победы (`PaintingCondition`, по одной на нишу)

```csharp
bool heightOk    = cond.column != null && cond.column.CurrentHeight == cond.requiredHeight;
bool spotlightOk = cond.spotlight == null || cond.spotlight.IsPowered;
bool colorOk     = cond.spotlight == null || cond.requiredColor == LensColor.None
                                  || cond.spotlight.GetEffectiveColor() == cond.requiredColor;
bool roomLightOk = !cond.requireRoomLightOff || _roomLightOff;
```

Проверка откладывается пока `PaintingColumn.IsAnyMoving`.

### Событие «все прожекторы запитаны»


| Член                            | Тип                  | Описание                                               |
| ------------------------------- | -------------------- | ------------------------------------------------------ |
| `AreAllSpotlightsPowered`       | `bool`               | Все прожекторы из `_conditions` запитаны               |
| `OnAllSpotlightsPoweredChanged` | `event Action<bool>` | Изменение `AreAllSpotlightsPowered` (true = все горят) |


Пересчитывается в `OnAnyStateChanged` / `OnZoneSwitchChanged` / `ResetPuzzleState` / `Start`.

### Рандомизация стартовых высот (`RandomizeColumns`)

Высоты колонн рандомизируются с сохранением инварианта решаемости `(h0 − h1 + h2 − h3) mod 3` относительно решения. Колонны, загруженные из сейва (`WasLoaded`), не рандомизируются.

### Сохранение

`SaveData`: `isSolved` (всегда), `switchStates` и `conditionLenses` (только если решено). При загрузке решённой загадки — `RestoreSolvedState()`: конфигурация рубильников и линз восстанавливается, символы показываются, ящики мгновенно открыты, всё заблокировано. Старые сейвы (только `isSolved`) совместимы.

### Сброс для отладки

Play Mode → `PaintPuzzle` → Inspector на `LoopPuzzleController` → **Reset Puzzle** (контекстное меню).

---

## Синтез цвета L3

`PaintingSpotlight` на L3 не имеет линзы: `_synthesisInputs = [L2, L4]`. L2 = Blue **и** L4 = Yellow → эффективный цвет Green; иначе None. Отключённые входы не дают ложного результата (`IsPowered` проверяется). Кнопка L3 не нужна.

---

## Интеграция с системой электричества

`LoopPuzzleController` реализует `IPowerConsumer` и регистрируется в `LightingSystem`. См. [@ id="/Pages/Private/Lighting System.md" label="Lighting System"].

При выключении общего питания: TV blackout (`PeepholeTVCamera`, `TVGlitchEffect`, `TVChannelButton` — `enabled = false`), рубильники и Q-кнопки не нажимаются, спотлайты деактивированы (`SetActive(false)`), схема обесточена. При включении — всё восстанавливается. Объекты остаются видимыми — блокируется только взаимодействие через `CanInteract() => enabled`.

### Условие `requireRoomLightOff` в `PaintingCondition`

Состояние света в PaintingRoom читается через `LightingSystem.GetZoneSwitchState(_roomLightZoneId)` (default `"painting_room"`). Для работы: на лампах в PaintingRoom должен быть `Lamp` с тем же `Zone Id`, `PaintingRoomLightSwitch` управляет той же зоной.

---

## Глазок и ТВ

Глазок на `PaintPuzzle` открывает вид через `PeepholeCamera` (CinemachineCamera): `UIManager.PushModalState()` блокирует движение игрока; выход — LMB / WASD / Esc через собственные `InputAction` (Player map отключён).

ТВ (`PeepholeTVCamera` на `TV_CCTV_01`): циклически активирует одну из N камер, рендерит в shared `RenderTexture` на экран `pPlane1` через шейдер `Assets/Shaders/TVGlitch.shader` (`Custom/TVGlitch`: `_BaseMap`, `_ScanlineCount`, `_NoiseAmount`, `_EmissionStrength`, `_GlitchAmount` и др.). `TVChannelButton` на `PositionSwitch` переключает камеры (`NextCamera()`). Символы на слое `TVOnly` видны только TV-камерам (`Main Camera` исключает слой в `Awake`).

Настройка яркости экрана: тускло — подними `_emissionStrength` (4–8); свечение — `_emissionColor` HDR > 1; `_noiseAmount` держи в 0.03–0.15.

---

## Чит-меню

**Tools → PuzzlesCheats → Solve Paint Puzzle** (Play Mode): `LoopPuzzleController.AutoSolve()` — включает S6, брутфорсом находит комбинацию S1–S5, питающую все прожекторы, ставит линзы и высоты, запускает кинематик.
