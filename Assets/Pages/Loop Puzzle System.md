## Обзор

Головоломка «Комната с картинами» (`PaintPuzzle`). Игрок осматривает панель `ControlPanel` (слой `Interactable Layer`) — через `PuzzleInteractable` + `PuzzleModeController` включается режим загадки с камерой `MainPuzzleCamera`, показывающей комнату управления. В режиме загадки управление мышью: `PuzzleCursor` рейкастит коллайдеры кнопок на слое `PuzzleInteractable`. Задача — настроить высоту картин, цвет линз прожекторов и схему питания так, чтобы на всех четырёх полотнах одновременно проявились скрытые символы. Когда все символы видны, проигрывается кинематик (общий `PuzzleSolvedCinematic`) с открытием обоих ящиков стола, и игрок автоматически выходит из режима загадки.

---

## Префаб

`Assets/Prefabs/puzzle/paint/PRF_PaintPuzzle.prefab`

```
PaintPuzzle                   ← LoopPuzzlePowerCircuit, LoopPuzzleController (IPowerConsumer),
                                PuzzleSolvedCinematic, CinematicAudioController,
                                Animator (PaintPuzzleCinematic.controller), PaintCinematicSignals
├── CameraSolved
│   └── PaintSolved           ← CinemachineCamera — камера solved-кинематика (приоритет 3000, start inactive)
├── SolvedLight
│   └── Point Light           ← SolvedLightFade — свет решённой комнаты
├── Maincamera
│   └── MainPuzzleCamera      ← CinemachineCamera — камера режима загадки (приоритет 2000)
├── ControlPanel              ← PuzzleModeController, PuzzleInteractable, BoxCollider (слой Interactable Layer)
│                                 — вход в загадку; коллайдер отключается при входе и при решении
├── tv
│   └── TV_CCTV_01            ← PeepholeTVCamera, TVGlitchEffect
│       ├── PositionSwitch    ← TVChannelButton, ButtonPressAnimation (коллайдер: слой PuzzleInteractable)
│       │   └── buttonPosition ← Transform — физически нажимается
│       ├── pPlane1           ← экран; получает runtime-материал TVGlitch (slot 0)
│       └── TV_CCTV_02        ← корпус телевизора
├── ControlRoom
│   ├── PowerButtons
│   │   └── Button_S1..S6    ← LoopPuzzleButton, ButtonPressAnimation (S1–S5 — PRF_ChainButton, S6 — PRF_Pwerbutton)
│   ├── ColumnButtons
│   │   └── Button_Q1..Q4    ← PaintingColumnTrigger, ButtonPressAnimation
│   ├── LensButtons
│   │   └── Button_L1..L4*   ← SpotlightLensButton (без ButtonPressAnimation — призму поворачивает своя логика)
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
- Ящики-награда живут **вне префаба**, в сцене: `directorsTable/Table Drawer_01 (3)` (верхний) и `Table Drawer_01 (5)` (нижний). На каждом: `DrawerDrag` (`_isLocked = true`, `_lockedClip = S_DoorLocked.aif`) + `DrawerAutoOpen`. Оба открываются кинематиком через `PaintCinematicSignals.OnOpenDrawer` и snap-открываются при загрузке решённого сейва (`LoopPuzzleController._rewardDrawer` / `_rewardDrawerLower`).

---

## Скрипты


| Файл                         | Назначение                                                                                                                                                                                       |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `LensColor.cs`               | Enum: `None / Red / Blue / Yellow / Green`                                                                                                                                                       |
| `PaintingSpotlight.cs`       | Прожектор с линзой и синтезом                                                                                                                                                                    |
| `SpotlightLensButton.cs`     | Кнопка смены линзы: 4 такта (0→1→2→3→0), поворот физического объекта, ISaveable                                                                                                                  |
| `LoopPuzzleButton.cs`        | Рубильник питания S1–S6, текстурированный emission-гло, блокируется при решении. `CanInteract()` проверяет `enabled` — отключается `ElectricDevice` при отсутствии питания                       |
| `LoopPuzzlePowerCircuit.cs`  | Схема питания (OR-of-AND), `LockAllSwitches()`, события `OnPowerChanged` и `OnMasterToggled`                                                                                                     |
| `LoopPuzzleController.cs`    | Центральный контроллер условий (ISaveable, IPowerConsumer), cinematic при решении, `AutoSolve()` для читов, `RandomizeColumns()` с инвариантом решаемости, сброс при выключении S6 |
| `PaintPuzzleUnlockTool.cs`   | Editor-чит: Tools/PuzzlesCheats/Solve Paint Puzzle — мгновенное решение через `AutoSolve()` (лежит в `Assets/Scripts/Editor/PuzzleCheats/`)                                                      |
| `LoopPuzzleHiddenDoor.cs`    | Скользящая дверь (ISaveable)                                                                                                                                                                     |
| `PaintingColumn.cs`          | Ниша с изменяемой высотой (ISaveable), глобальный счётчик `IsAnyMoving`, события `OnMoveFinished` / `OnHeightChanged`                                                                            |
| `PaintingColumnTrigger.cs`   | Кнопка смены высоты: glow во время движения, два независимых флага блокировки (питание / решение). `CanInteract() => enabled` — отключается `LoopPuzzleController` при отсутствии общего питания |
| `PaintingRoomLightSwitch.cs` | Выключатель света, блокируется при решении                                                                                                                                                       |
| `SymbolFader.cs`             | Плавное появление символа с настраиваемым HDR-цветом свечения                                                                                                                                    |
| `SolvedLightFade.cs`         | Плавный фейд света решённой комнаты (`SolvedLight`): FadeIn по `OnEnable`, `FadeOut()` перед деактивацию                                                                                        |
| `PaintCinematicSignals.cs`   | Мост Animation Events кинематика: открывает ящики (`_drawers[]`), включает/гасит свет (`_solvedLight`), выходит из puzzle mode (`OnExitPuzzleMode` → `_puzzleMode.SetSolved()`)                  |
| `DrawerAutoOpen.cs`          | Обёртка над `DrawerDrag.AutoOpen(duration)` — висит на каждом ящике-награде, вызывается Animation Event'ом `OnOpenDrawer`                                                                        |
| `ButtonPressAnimation.cs`    | Физическое нажатие кнопки + звук клика. Вызывается и `FPSController`, и `PuzzleCursor` (см. раздел про мышь)                                                                                     |
| `PeepholeTVCamera.cs`        | Создаёт RenderTexture, циклически активирует одну из N камер, назначает TVGlitch-материал на экран. `OnDisable` заменяет материал на чёрный без эмиссии, `OnEnable` восстанавливает              |
| `TVGlitchEffect.cs`          | Случайные глитч-события: анимирует `_GlitchAmount` на материале экрана                                                                                                                           |
| `TVChannelButton.cs`         | `IInteractable` на кнопке ТВ; вызывает `PeepholeTVCamera.NextCamera()`. `CanInteract()` проверяет `enabled` — отключается при отсутствии питания                                                 |


---

## Матрица активации


| Ниша          | Цвет луча                   | Высота | Символ | Код |
| ------------- | --------------------------- | ------ | ------ | --- |
| Q1 (Младенец) | Red (L1)                    | TOP    | Ω      | 4   |
| Q2 (Воин)     | Yellow (L2)                 | BOT    | Ψ      | 9   |
| Q3 (Судья)    | Green (L2 Blue + L4 Yellow) | MID    | Σ      | 2   |
| Q4 (Тень)     | Blue (L4)                   | BOT    | Δ      | ?   |


Значение для Q4 (Δ) не было указано — уточнить с дизайнером.

---

## SymbolFader — плавное появление и свечение

`SymbolFader` — компонент на каждом `Symbol_*` объекте.

### Поведение

- `Show()` — фейд-ин за `_fadeDuration` (0.5 с по умолчанию). Активирует GameObject если он был неактивен.
- `Hide()` — фейд-аут. Оставляет GameObject активным с `alpha = 0` — последующий `Show()` сразу запускает корутину.
- `HideImmediate()` — мгновенное скрытие без анимации. Используется при инициализации и reset.
- `IsTargetVisible` — логическое состояние (не зависит от текущей анимации). `LoopPuzzleController.CheckWinCondition` читает это значение вместо `activeSelf`.

### Цвет свечения

Поле `_glowColor` (HDR, alpha игнорируется) — **мультипликатор** поверх оригинального `SpriteRenderer.color`.


| `_glowColor`                  | Результат                                   |
| ----------------------------- | ------------------------------------------- |
| `(1, 1, 1)` — белый (default) | Исходный цвет спрайта сохраняется           |
| `(2, 2, 2)`                   | Цвет спрайта удваивается → активирует Bloom |
| `(2, 0, 0)`                   | Красный HDR-гло поверх исходного цвета      |


В `Awake` оригинальный `SpriteRenderer.color` кешируется как `_baseColor`. `SetAlpha` умножает `_baseColor * _glowColor` и подставляет текущий alpha от fade-системы.

> Важно: `Sprite-Unlit-Default` в URP хардкодит `Blend SrcAlpha OneMinusSrcAlpha` — изменить режим смешивания через `_SrcBlend` / `_DstBlend` невозможно без кастомного шейдера.

### Требования к сцене для Bloom

- `Global Volume` → `Is Global = true`, содержит `Bloom`
- `Main Camera` → `Universal Additional Camera Data` → `Post Processing = enabled`
- Значения `_glowColor` выше `1.0` по хотя бы одному каналу

### Настройка в префабе

```
PaintingColumn_Q1/Symbol_Omega  ← _baseColor = (0.855, 0.003, 0.003)  — красный
PaintingColumn_Q2/Symbol_Psi    ← _baseColor = (1, 0.746, 0)          — оранжевый
PaintingColumn_Q3/Symbol_Sigma  ← _baseColor = (0, 1, 0.04)           — зелёный
PaintingColumn_Q4/Symbol_Delta  ← _baseColor = (0.008, 0.445, 1)      — синий
```

`_baseColor` — это `SpriteRenderer.color` из Inspector/префаба. Менять его напрямую в Inspector, не в коде.

---

## PaintingColumn — движение картин

`PaintingColumn` управляет плавным перемещением картины между тремя высотами.

### Глобальный счётчик движения

```csharp
public static bool IsAnyMoving       // true пока хоть одна колонна движется
public static event Action<bool> OnAnyMovingChanged  // true = началось, false = все остановились
```

`PaintingColumnTrigger` и `LoopPuzzleController` используют `IsAnyMoving` чтобы блокировать взаимодействие и отложить проверку победы до конца анимации.

### События


| Событие           | Когда                                                  |
| ----------------- | ------------------------------------------------------ |
| `OnHeightChanged` | Сразу при вызове `AdvanceHeight()` — до конца анимации |
| `OnMoveFinished`  | По окончании корутины `SlideTo`                        |


---

## PaintingColumnTrigger — кнопки Q1–Q4

### Логика блокировки

Два независимых флага:


| Флаг                | Причина        | Кто ставит                                    |
| ------------------- | -------------- | --------------------------------------------- |
| `_isLockedByPower`  | S6 выключен    | `OnMasterToggled` из `LoopPuzzlePowerCircuit` |
| `_isLockedByPuzzle` | Загадка решена | `LoopPuzzleController` → `SetLocked(true)`    |


Кнопка также игнорирует нажатие при `PaintingColumn.IsAnyMoving`.

### Текст подсказки


| Состояние         | Текст                                   |
| ----------------- | --------------------------------------- |
| Активна           | `_interactText` («Нажать»)              |
| Нет питания       | `_noPowerText` («Нет питания»)          |
| Решено / движение | `_lockedInteractText` («Заблокировано») |


### Привязка к PowerCircuit

`LoopPuzzlePowerCircuit` ищется автоматически через `GetComponentInParent` в `Start()`. Поле `_powerCircuit` в Inspector — опциональный override для нестандартной иерархии.

### Glow во время движения

При нажатии включается emission через `_EmissionMap = _albedoTexture` + `_EmissionColor`. Гаснет когда обе привязанные колонны (`_primaryColumn`, `_linkedColumn`) завершили `OnMoveFinished`. Если питание отрубается во время движения — гасится немедленно.

---

## SpotlightLensButton — кнопки L1, L2, L4

Кнопка циклически переключает линзу прожектора по 4 тактам: `0 → 1 → 2 → 3 → 0`.

### Поля Inspector


| Секция     | Поле               | Описание                                         |
| ---------- | ------------------ | ------------------------------------------------ |
| Save       | `_saveId`          | Уникальный ID для ISaveable                      |
| Linked     | `_targetSpotlight` | `PaintingSpotlight` который управляется          |
| Lens Cycle | `_lensOptions[4]`  | `LensColor` на каждый такт (0–3)                 |
| Rotation   | `_rotationTarget`  | Transform для поворота (null = сам GameObject)   |
| Rotation   | `_axis`            | Ось вращения в локальных координатах (X / Y / Z) |
| Rotation   | `_stepAngle`       | Угол за один шаг в градусах (default 15°)        |
| Rotation   | `_rotateDuration`  | Длительность анимации поворота (default 0.2 с)   |


Итоговые углы: `0° → 15° → 30° → 45° → 0°` при `_stepAngle = 15`.

При загрузке сохранения поворот применяется мгновенно (`SnapRotation`) без анимации.

---

## LoopPuzzlePowerCircuit — события питания


| Событие / свойство | Тип                  | Описание                                 |
| ------------------ | -------------------- | ---------------------------------------- |
| `OnPowerChanged`   | `event Action`       | Любое изменение состояния прожекторов    |
| `OnMasterToggled`  | `event Action<bool>` | S6 включён (`true`) / выключен (`false`) |
| `IsMasterOn`       | `bool`               | Текущее состояние S6                     |


`PaintingColumnTrigger` подписывается на `OnMasterToggled` чтобы управлять `_isLockedByPower`.

---

## Логика синтеза L3

`PaintingSpotlight` на L3 имеет поле `_synthesisInputs = [L2, L4]`.

- L2 линза = Blue **и** L4 линза = Yellow → `GetEffectiveColor()` = `Green`
- Любая другая комбинация → `None` (символ не проявляется)
- L3 подписывается на `OnLensChanged` каждого из inputs и автоматически перекрашивает Unity Light при изменении

**Исправление (апрель 2025):** В `PaintingSpotlight.SetPowered` теперь вызывается `OnLensChanged?.Invoke()` — без этого `LoopPuzzleController` не пересчитывал эффективный цвет при изменении питания. `GetEffectiveColor` также проверяет `IsPowered` у самого прожектора и у каждого `_synthesisInputs` перед смешиванием цветов, чтобы отключённые входы не давали ложный результат.

---

## Схема питания (LoopPuzzlePowerCircuit)

Рубильники S1–S6, S6 — мастер (если выкл., все прожекторы выкл.).


| Прожектор | Условие питания (OR-of-AND) |
| --------- | --------------------------- |
| L1        | S1=ON                       |
| L2        | (S1+S2) OR (S4)             |
| L3        | (S5+S3+S1+S2) OR (S5+S3+S4) |
| L4        | S3=ON                       |


### Lights Out — матрица смежности

S1–S5 реализуют загадку Lights Out. Нажатие рубильника Si переключает его самого и всех соседей по матрице смежности. Матрица задаётся в Inspector компонента `LoopPuzzlePowerCircuit` через визуальный редактор.

Текущая конфигурация (сложная, 3 соседа у части переключателей) имеет одно решение — проверяется кнопкой **«Проверить (GF(2) анализ)»** в Inspector.

### Игровой процесс

1. Игрок нажимает S6 → S1–S5 разблокируются, кнопки Q1–Q4 разблокируются.
2. Игрок нажимает S1–S5 — каждое нажатие применяет Lights Out каскад.
3. Когда правильная комбинация активна → все прожекторы включаются.
4. `LoopPuzzleController` проверяет высоту картин и цвет линз → символы проявляются → дверь открывается.

### Блокировка при решении

При решении загадки `LoopPuzzlePowerCircuit.LockAllSwitches()` блокирует все S1–S6. После этого `LoopPuzzleButton.SetLocked(true)` предотвращает любое взаимодействие с рубильниками.

---

## Индикаторы кнопок (LoopPuzzleButton)

Каждый рубильник `Button_S1..S6` имеет дочерний рендерер с материалом `Panel.mat`.


| Состояние | `_EmissionMap`               | `_EmissionColor`                            |
| --------- | ---------------------------- | ------------------------------------------- |
| OFF       | `null`                       | `black` — нет свечения                      |
| ON        | `_BaseMap` (текстура кнопки) | HDR `_activeEmissionColor` — textured bloom |
| Locked    | `null`                       | `_lockedEmissionColor` (default black)      |


Emission работает поверх текстуры — нет плоского однотонного свечения. `_albedoTexture` кешируется в `Awake` из `_BaseMap`.

---

## Блокировка взаимодействий при решении

Когда загадка решена (`OnPuzzleSolved`), `LoopPuzzleController.LockAllInteractions()` вызывает:

- `_powerCircuit.LockAllSwitches()` — блокирует S1–S6
- `_roomLightSwitch.SetLocked(true)` — блокирует выключатель света
- `SetLocked(true)` на всех `PaintingColumnTrigger` (Q1–Q4) и `SpotlightLensButton` (L1/L2/L4) в детях

`_roomLightSwitch` — `[SerializeField]` на `LoopPuzzleController`. Назначить в Inspector. Если не назначен (`null`), `?.SetLocked(true)` молча пропускается.

---

## Cinematic при решении (общий PuzzleSolvedCinematic)

Когда все 4 символа проявились, `OnPuzzleSolved()` (через `CheckWinCondition`) вызывает `LockAllInteractions()` и запускает кинематик: `_solvedCinematic.PlayCinematic()`. Играет **общий компонент `PuzzleSolvedCinematic`** (на корне `PaintPuzzle`) — тот же конвейер, что у Fifteen/Board/Metamorf-загадок. **Нельзя** вызывать `_puzzleMode.SetSolved()` при решении (до кинематика): паззл-камера мгновенно отключится и Cinemachine улетит к камере игрока ещё до фейда — выход из puzzle mode делает Animation Event `OnExitPuzzleMode` (см. таймлайн).

### Компоненты и объекты

| Объект | Компонент | Роль |
| --- | --- | --- |
| `PaintPuzzle` (корень) | `PuzzleSolvedCinematic` (`_saveId = "loop_puzzle_cinematic"`) | Управляет fade/камерой через Animation Events; `_cinematicCamera` → `CameraSolved/PaintSolved`, `_animator` → собственный Animator на корне, `_audioController` → `CinematicAudioController` там же |
| `PaintPuzzle` (корень) | `Animator` (`PaintPuzzleCinematic.controller`) + `PaintCinematicSignals` |Animator и мост событий — на **том же объекте**, что `PuzzleSolvedCinematic` (Animation Events ищут приёмник на GameObject аниматора). Idle-состояние по умолчанию, клип запускается переходом по триггеру `PlayCinematic`. **Не делайте** cinematic-клип дефолтным состоянием — он проиграется сразу при загрузке сцены (ящики откроются без разгадки) |
| `PaintPuzzle` (корень) | `CinematicAudioController` | Клип `S_CardboardBoxOpen.aif` (index 0) — играет через Animation Event `PlayByIndex(0)` |
| ящики `Table Drawer_01 (3)` и `(5)` (в комнате, вне префаба) | `DrawerDrag` + `DrawerAutoOpen` | Animation Event `OnOpenDrawer` → `DrawerAutoOpen.OpenDrawer()` → `DrawerDrag.AutoOpen(2 c)` на каждом |

### Таймлайн Animation Events (`PaintPuzzleSolvedCinematic.anim`, длина 10 с)

| Время | Событие | Эффект |
| --- | --- | --- |
| 0.0 | `OnFadeIn(1)` | Экран уходит в чёрный |
| 1.0 | `OnCinematicCameraActivate(0)` | Cut на `PaintSolved` **на пике чёрного** |
| 1.1 | `OnFadeOut(1)` + `OnShowSolvedLight` | Игрок видит комнату, свет разгорается |
| 1.3 | `OnOpenDrawer` | **Оба** ящика выезжают |
| 1.6 | `PlayByIndex(0)` | Звук открытия ящика |
| 4.0 | `OnHideSolvedLight` + `OnFadeIn(1)` | Свет гаснет, экран темнеет |
| 4.9 | `OnExitPuzzleMode` | Exit puzzle mode, пока PaintSolved ещё активен (визуально ничего не меняется) |
| 5.0 | `OnCinematicCameraDeactivate(0)` | Cut к игроку **на пике чёрного**, blend = 0 — резкий cut, без «плывущего» бленда |
| 5.1 | `OnFadeOut(1)` | Проявляется вид от первого лица |
| 6.0 | `OnCinematicEnd` | Ввод восстановлен |

Порядок t=4.9 → t=5.0 важен: `ExitPuzzleMode` восстанавливает бленд 0.75 с, поэтому он должен отработать **до** cut'а (PaintSolved с приоритетом 3000 всё ещё активен — переход «в никуда» не виден), а cut к игроку идёт уже с blend = 0.

### Поля Inspector (LoopPuzzleController → Solved Cinematic)


| Поле                 | Тип                     | Описание                                                             |
| -------------------- | ----------------------- | -------------------------------------------------------------------- |
| `_solvedCinematic`   | `PuzzleSolvedCinematic` | Общий кинематик-компонент на корне, запускается из `OnPuzzleSolved()` |
| `_solvedLight`       | `SolvedLightFade`       | Свет решённой комнаты: включается/выключается через `PaintCinematicSignals` (Animation Events `OnShowSolvedLight` / `OnHideSolvedLight`) |
| `_rewardDrawer`      | `DrawerDrag`            | Верхний ящик-награда (snap-open при загрузке решённого сейва)         |
| `_rewardDrawerLower` | `DrawerDrag`            | Нижний ящик-награда (опционально; snap-open при загрузке решённого сейва) |


### Восстановление из сейва

При загрузке решённой загадки (`RestoreSolvedState`) cinematic **не** проигрывается — оба ящика мгновенно открываются через `SnapOpen()`, символы показываются через `ShowAllSymbols()`. `PuzzleSolvedCinematic` восстанавливает своё `wasAlreadySolved` из собственного `ISaveable` (тот же механизм, что у всех загадок).

---

## Сохранение (LoopPuzzleController)

`LoopPuzzleController` реализует `ISaveable` (`_saveId = "loop_puzzle_controller"`). Структура `SaveData`:


| Поле              | Тип      | Когда сохраняется             |
| ----------------- | -------- | ----------------------------- |
| `isSolved`        | `bool`   | всегда                        |
| `switchStates`    | `bool[]` | только если `isSolved = true` |
| `conditionLenses` | `int[]`  | только если `isSolved = true` |


`switchStates` — состояния S1–S6 на момент решения. `conditionLenses` — `LensColor` каждого спотлайта из `_conditions`, приведённый к `int` (`-1` если у условия нет спотлайта).

### Рандомизация стартовых высот (`RandomizeColumns`)

На старте не решённой загадки высоты колонн рандомизируются с сохранением инварианта решаемости `(h0 − h1 + h2 − h3) mod 3` относительно решения: h0–h2 выбираются случайно (offset 1–2 от требуемой высоты), h3 выводится из инварианта. Если колонны были загружены из сейва (`WasLoaded`) или условий не ровно 4 — используется простой per-column рандом (`RandomizeStartingHeight`), сохраняя загруженные позиции.

### Сброс при выключении S6 (`ResetPuzzleState`)

Выключение мастер-рубильника S6 (`OnMasterToggled(false)`) автоматически сбрасывает пазл: `ResetSwitchesToOff()` (S1–S5 без каскада), колонны возвращаются к стартовым высотам сессии (`ResetToInitialState`), все символы скрываются (`HideAllSymbols`). Проверка победы откладывается пока `PaintingColumn.IsAnyMoving`.

### Восстановление решённого состояния (`RestoreSolvedState`)

При загрузке с `isSolved = true` вызывается `RestoreSolvedState()` вместо стандартной инициализации:

1. `_powerCircuit.RestoreSwitchStates(switchStates)` → `EvaluateAndApply()` → нужные прожекторы загораются.
2. `spotlight.SetLens(LensColor)` для каждого условия → цвет линз восстанавливается.
3. `ShowAllSymbols()` — `SymbolFader.Show()` на каждом символе (fade-in).
4. `_rewardDrawer.SnapOpen()` — полка мгновенно открыта.
5. `LockAllInteractions()` — S1–S6, выключатель света, кнопки Q и линз заблокированы.
6. Подписка на события **не** происходит — загадка уже решена.

Старые сохранения (только `isSolved`) совместимы: `JsonUtility` оставляет `switchStates` и `conditionLenses` как `null`. В этом случае прожекторы и линзы не восстанавливаются, символы всё равно показываются через `ShowAllSymbols()`.

---

## LoopPuzzleController — сброс состояния

**Как сбросить в Play Mode:**

1. Выбери `PaintPuzzle` в Hierarchy.
2. В Inspector на `LoopPuzzleController` → правая кнопка → **Reset Puzzle**.

Метод сбрасывает `_isSolved = false`, переподписывается на все события, обновляет символы и записывает сброс в сохранение. Заблокированные кнопки **не** разблокируются автоматически — для полного сброса нужно перезапустить Play Mode.

---

## Inspector-справка (LoopPuzzlePowerCircuitEditor)

В Inspector компонента `LoopPuzzlePowerCircuit` есть сворачиваемый раздел **«📖 Справка по настройке загадки»**. Содержит пошаговое руководство:

- **Шаг 1 — Рубильники**: порядок назначения, S6 всегда последний.
- **Шаг 2 — Логика прожекторов**: как читать OR-of-AND матрицу, пример.
- **Шаг 3 — Смежность Lights Out**: как читать матрицу, советы по сложности.
- **Шаг 4 — Проверка**: как интерпретировать результат GF(2)-анализа.
- **Игровой процесс**: чеклист если символы не появляются.

Состояние (развёрнуто/свёрнуто) сохраняется в `EditorPrefs`.

---

## Схема цепи (CircuitDiagramWindow)

Открывается кнопкой **«Открыть схему цепи»** в нижней части Inspector.

### Визуальная структура

```
[L1]     [L2]     [L3]     [L4]      ← spotlight-узлы (рамка цвета своего прожектора)
  │        │        │        │
──┼────────┼────────┼────────┼──  ← шина L4 (ближняя к прожекторам)
  │        │        │        │
──┼────────┼────────┼────────┼──  ← шина L3
  │        │        │        │
──┼────────┼────────┼────────┼──  ← шина L2
  │        │        │        │
──┼────────┼────────┼────────┼──  ← шина L1 (ближняя к рубильникам)
  │        │        │        │
[S1]    [S2]    [S3]    [S4]    [S5]  ← switch-узлы
  ╰────────╯  ╰──────────────╯        ← дуги смежности (слоями по дистанции)
```

### Цветовое кодирование


| Элемент           | Цвет             |
| ----------------- | ---------------- |
| L1 правила        | Оранжевый        |
| L2 правила        | Голубой          |
| L3 правила        | Фиолетовый       |
| L4 правила        | Лаймовый         |
| Смежность dist=1  | Ярко-жёлтый      |
| Смежность dist=2  | Золотой          |
| Смежность dist=3  | Янтарный         |
| Смежность dist=4+ | Оранжево-красный |


### Условные обозначения линий

- **Сплошная** — первая AND-группа правила.
- **Пунктирная** — дополнительная OR-группа того же прожектора.
- **Точка ●** на шине — реальное соединение (не пересечение).

---

## Глазок (Peephole)

> Раздел устарел и удалён: глазок (`Peephole`, `PeepholeInteractable.cs`) полностью убран из загадки — телевизор работает напрямую от камер `tv/TVCamera`, `tv/TVCamera2` (список `_cameras` на `PeepholeTVCamera`).

---

## TV-экран (PeepholeTVCamera + TVGlitchEffect + TVChannelButton)

### Шейдер

`Assets/Shaders/TVGlitch.shader` — кастомный URP Unlit шейдер (`Custom/TVGlitch`).


| Свойство            | Тип           | Описание                                            |
| ------------------- | ------------- | --------------------------------------------------- |
| `_BaseMap`          | Texture2D     | Shared RenderTexture от активной TVCamera           |
| `_ScanlineCount`    | Float         | Количество скан-линий (180 по умолчанию)            |
| `_ScanlineDarkness` | Range(0, 0.5) | Затемнение между линиями                            |
| `_NoiseAmount`      | Range(0, 1)   | Доля статика поверх изображения                     |
| `_NoiseSpeed`       | Range(1, 60)  | Частота смены кадра шума (fps)                      |
| `_EmissionStrength` | Range(0, 10)  | Множитель яркости RT-контента                       |
| `_EmissionColor`    | HDR Color     | Аддитивное свечение экрана (не зависит от контента) |
| `_GlitchAmount`     | Range(0, 1)   | Интенсивность глитча — управляется скриптом         |


### PeepholeTVCamera

Находится на `TV_CCTV_01`. Управляет списком камер `_cameras` (теперь это `tv/TVCamera`, `tv/TVCamera2` — глазок удалён): в любой момент только одна камера активна и рендерит в shared `RenderTexture`. Остальные отключены (`camera.enabled = false`, `targetTexture = null`).

При старте создаёт RT, инстанцирует runtime-материал `Custom/TVGlitch` и назначает его на `pPlane1` (slot `_materialIndex`). `TVGlitchEffect` получает `ScreenMaterial` с того же `TV_CCTV_01`.

**Blackout при отсутствии питания:** `PeepholeTVCamera` управляется `ElectricDevice` на `PaintPuzzle`. Когда питание выключено, `ElectricDevice` ставит `enabled = false`, что вызывает `OnDisable`:

- Все камеры останавливаются (`targetTexture = null`, `enabled = false`)
- Материал экрана заменяется на чёрный URP Lit без эмиссии — экран полностью тёмный
При включении питания `OnEnable` восстанавливает RT-материал и активирует текущую камеру.

Поля Inspector:


| Поле              | Поле C#             | Описание                                       |
| ----------------- | ------------------- | ---------------------------------------------- |
| Cameras           | `_cameras`          | Список Camera — порядок = порядок переключения |
| Screen Renderer   | `_screenRenderer`   | MeshRenderer экрана (pPlane1)                  |
| Material Index    | `_materialIndex`    | Слот материала на экране (0)                   |
| Width / Height    | `_width` / `_height`| Размер RenderTexture (в префабе: 1024×576)     |
| Shader            | `_shader`           | Ассет `TVGlitch.shader` — назначен явно, чтобы не быть вырезанным из билда |
| Emission Strength | `_emissionStrength` | Яркость RT-контента (в префабе: 4)             |
| Noise Amount      | `_noiseAmount`      | Статик (в префабе: 0.01)                       |
| Noise Speed       | `_noiseSpeed`       | Частота шума fps (в префабе: 12)               |
| Emission Color    | `_emissionColor`    | HDR свечение экрана                            |


Значения синхронизируются с материалом каждый кадр (`Update` → `SyncMaterialProperties`).

Символы на слое `TVOnly` видны только TV-камерам. `Main Camera` исключает этот слой из `cullingMask` в `Awake` через `HideSymbolsFromMainCamera()`.

### TVGlitchEffect

Находится на `TV_CCTV_01` рядом с `PeepholeTVCamera`. В `Start()` получает `ScreenMaterial` через `GetComponent<PeepholeTVCamera>()`. Периодически запускает корутины глитча: снапает `_GlitchAmount` до случайного значения, затем плавно сводит к 0. Поддерживает burst-режим.

### TVChannelButton

Находится на `PositionSwitch` (слой `Interactable Layer`). Реализует `IInteractable`, вызывает `PeepholeTVCamera.NextCamera()` — переключает на следующую камеру в списке `_cameras` по кругу.

`ButtonPressAnimation` на том же объекте физически нажимает `buttonPosition` по оси Y при взаимодействии.

### Порядок переключения камер

Список `_cameras` на `PeepholeTVCamera` (Inspector) → порядок элементов = порядок переключения. Чтобы изменить — перетащи элементы в списке. `NextCamera()` делает `(currentIndex + 1) % count`.

### Настройка яркости экрана

- Если изображение **тусклое** — подними `_emissionStrength` (4–8).
- Если изображение **чёрное** — RT назначается автоматически в `Awake`; убедись что активная TVCamera смотрит на освещённую сцену.
- Для **свечения экрана** — выставь `_emissionColor` в HDR с `Intensity > 1`.
- `_noiseAmount` держи в `0.03–0.15`; выше 0.5 — экран превращается в статик.

---

## Мышиный ввод в паззл-режиме (PuzzleCursor)

Кнопки кликаются мышью через `PuzzleCursor` (`Assets/Scripts/UI/PuzzleCursor.cs`): рейкаст из активной камеры только по слою `PuzzleInteractable`, дистанция 2.5 м. По `GetComponentInParent<IInteractable>` от коллайдера находится скрипт кнопки (`LoopPuzzleButton`, `PaintingColumnTrigger`, `SpotlightLensButton`, `TVChannelButton`).

**Нажатие кнопки**: по ЛКМ `PuzzleCursor` вызывает `Interact()` **и** `GetComponent<ButtonPressAnimation>()?.Play()` — тот же вызов, что и в `FPSController`. `ButtonPressAnimation` лежит рядом с `IInteractable`-скриптом на корневом объекте кнопки (`Button_S1..S6`, `Button_Q1..Q4`, `PositionSwitch`) и проигрывает продавливание + звук (`S_SwitchClick` для рубильников, `S_MechanicalButtonPush` для S6). У `SpotlightLensButton` (L1/L2/L4) анимации нажатия нет — призму поворачивает собственная логика линз.

---

## Чит-меню (PaintPuzzleUnlockTool)

**Tools → PuzzlesCheats → Solve Paint Puzzle**

Editor-скрипт `Assets/Scripts/Editor/PuzzleCheats/PaintPuzzleUnlockTool.cs`. Работает **только в Play Mode**.

При активации:

1. Ищет `LoopPuzzleController` в сцене через `FindFirstObjectByType`.
2. Проверяет что загадка не решена (`IsSolved`).
3. Вызывает `controller.AutoSolve()`.

### `LoopPuzzleController.AutoSolve()`

Мгновенно решает загадку без прохождения механики:

1. **Включает S6** (мастер-рубильник) через `SetStateSilent(true)`.
2. **Находит правильную комбинацию S1–S5** — перебирает все 2^5 = 32 варианта, находит тот, который питает все 4 прожектора (через `LoopPuzzlePowerCircuit.CheckAllPoweredWith`).
3. **Устанавливает линзы** на каждом прожекторе из `_conditions` (Red, Yellow, Blue; Green синтезируется автоматически от Blue+Yellow на L2+L4).
4. **Выставляет высоты** колонн в требуемые значения из `_conditions` (High, Low, Mid, Low) через `SetInitialHeight`.
5. **Показывает все символы** через `ShowAllSymbols()`.
6. **Запускает кинематик** через `OnPuzzleSolved()` — затемнение, камера, ящик, возврат управления.

После активации проигрывается полный cinematic с затемнением, переключением камеры на PaintSolved, выездом полки и возвратом управления игроку.

---

## Интеграция с системой электричества

`LoopPuzzleController` реализует `IPowerConsumer` и регистрируется в `LightingSystem` в `Start()`. См. [@ id="/Pages/Private/Lighting System.md" label="Lighting System"].

### Что отключается при отсутствии питания

`OnPowerStateChanged(bool isPowered)` — вызывается LightingSystem; при `isSolved` игнорируется. Когда питание выключено:


| Компонент                  | Объект                  | Эффект                                                                              |
| -------------------------- | ----------------------- | ----------------------------------------------------------------------------------- |
| `PeepholeTVCamera`         | TV_CCTV_01              | `enabled = false` → `OnDisable` заменяет материал на чёрный, камеры останавливаются |
| `TVGlitchEffect`           | TV_CCTV_01              | `enabled = false` — глитч-анимация остановлена                                      |
| `TVChannelButton`          | PositionSwitch          | `enabled = false` → `CanInteract() == false` — кнопка не нажимается                 |
| `LoopPuzzleButton` ×6      | Button_S1..S6_Master    | `enabled = false` → `CanInteract() == false` — рубильники не нажимаются             |
| `PaintingColumnTrigger` ×4 | Button_Q1..Q4           | `enabled = false` → `CanInteract() == false` — картины не двигаются                 |
| `LoopPuzzlePowerCircuit`   | PaintPuzzle             | `enabled = false` — схема питания обесточена                                        |
| `Spotlights` (parent)      | PaintingRoom/Spotlights | `SetActive(false)` — все 4 спотлайта выключены                                      |


При включении питания всё автоматически восстанавливается.

### CanInteract() — блокировка без SetActive(false)

`LoopPuzzleButton`, `TVChannelButton` и `PaintingColumnTrigger` реализуют `CanInteract() => enabled`. Когда `LoopPuzzleController` ставит `enabled = false`, `FPSController` видит `CanInteract() == false` и не показывает подсказку / не позволяет нажать. Объекты остаются видимыми — только взаимодействие заблокировано.

### Inspector — LoopPuzzleController (поля электричества)


| Поле                | Тип                        | Описание                                                                                                                            |
| ------------------- | -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `_puzzleMode`       | `PuzzleModeController`     | Выход из режима загадки после solved-кинематика. Автопоиск в детях, если пуст                                                       |
| `_tvCamera`         | `PeepholeTVCamera`         | ТВ-камера — blackout при отсутствии питания                                                                                         |
| `_tvChannelButton`  | `TVChannelButton`          | Кнопка переключения каналов                                                                                                         |
| `_spotlightsParent` | `GameObject`               | Родительский объект спотлайтов                                                                                                      |
| `_columnButtons`    | `PaintingColumnTrigger[4]` | Кнопки Q1–Q4                                                                                                                        |
| `_powerButtons`     | `LoopPuzzleButton[6]`      | Рубильники S1–S6                                                                                                                    |
| `_roomLightSwitch`  | `PaintingRoomLightSwitch`  | Выключатель света в PaintingRoom — блокируется при решении. Опциональное (`null` = пропускается)                                    |
| `_roomLightZoneId`  | `string`                   | Zone ID освещения PaintingRoom (default: `"painting_room"`). Должен совпадать с `ZoneId` на `LightZone` и `PaintingRoomLightSwitch` |


### Условие `requireRoomLightOff` в `PaintingCondition`

Каждое условие в массиве `_conditions` может требовать, чтобы свет в PaintingRoom был **выключен** для проявления символа:

```csharp
bool roomLightOk = !cond.requireRoomLightOff || _roomLightOff;
```

Состояние света читается через `LightingSystem.GetZoneSwitchState(_roomLightZoneId)` — без прямой ссылки на выключатель. Это позволяет выключателю и лампам жить в любом префабе.

Для работы условия необходимо:

1. На объектах освещения в PaintingRoom добавить `LightZone` с `Zone Id = _roomLightZoneId`
2. `PaintingRoomLightSwitch` должен управлять той же зоной

---

## Что нужно сделать вручную (pending)

- [x] Создать объекты `LensButtons/Button_L1`, `Button_L2`, `Button_L4` в префабе, назначить `SpotlightLensButton` с уникальными `_saveId`
- [x] На `Spotlight_L3` → `PaintingSpotlight._synthesisInputs` назначить `Spotlight_L2` и `Spotlight_L4`
- [x] В `LoopPuzzleController._conditions` выставить `requiredColor`: Q1=Red, Q2=Yellow, Q3=Green, Q4=Blue
- [x] `Global Volume` выставить `Is Global = true`, включить `Post Processing` на `Main Camera`
- [x] `SymbolFader` добавлен на `Symbol_Omega`, `Symbol_Psi`, `Symbol_Sigma`, `Symbol_Delta` в префабе
- [x] Расставить кнопки `Button_L1`, `Button_L2`, `Button_L4` физически в сцене (позиция в ControlRoom)
- [x] **Cinematic**: переход на общий `PuzzleSolvedCinematic` (`_saveId = "loop_puzzle_cinematic"`) + `CinematicAudioController` + `Animator` (`PaintPuzzleCinematic.controller`) + `PaintCinematicSignals` на корне. Клип `PaintPuzzleSolvedCinematic.anim` с Animation Events; дефолтное состояние контроллера — **Idle** (не cinematic-клип!), запуск по триггеру `PlayCinematic`. Оба ящика (`Table Drawer_01 (3)` и `(5)`) с `DrawerAutoOpen` открываются событием `OnOpenDrawer`. Выход из puzzle mode — событие `OnExitPuzzleMode` (t=4.9), cut к игроку — t=5.0 с blend 0 (оба на пике чёрного)
- [x] **ScreenFader**: объект `/Canvas/ScreenFader` активен в сцене (`activeSelf = true`)
- [x] **SolvedLight**: `SolvedLightFade` на `SolvedLight`, назначен в `LoopPuzzleController._solvedLight`
- [x] **Мышь в паззл-режиме**: `PuzzleCursor` после `Interact()` вызывает `ButtonPressAnimation.Play()` — кнопки продавливаются и щёлкают (`S_SwitchClick` / `S_MechanicalButtonPush`), как в первом лице
- [x] **Запертые ящики**: оба ящика `_isLocked = true`, `_lockedClip = S_DoorLocked.aif`; `DrawerDrag` повторяет звук при каждом рывке в запертом состоянии (кулдаун 0.4 c)
- [x] **Чистка рудиментов**: удалены `LightsOutPanel.cs` + `LightsOutPanelEditor.cs` (пустые заглушки), `_masterUnlockSequence` из `LoopPuzzlePowerCircuit` и мёртвый `DrawSequenceSection()` из его Editor; рефлексия в `AutoSolve()` заменена на `LoopPuzzlePowerCircuit.SetSwitchSilent()`
- [ ] Добавить `PaintingRoomLightSwitch` на объект выключателя в сцене, назначить его в `LoopPuzzleController._roomLightSwitch`
- [ ] На объекты освещения в PaintingRoom добавить компонент **Light Zone** (`LightGroup.cs`) с `Zone Id = "painting_room"`
- [ ] Настроить высоты `_lowY / _midY / _highY` в `PaintingColumn` под реальную геометрию сцены
- [ ] Уточнить код Δ (значение символа Q4) у дизайнера