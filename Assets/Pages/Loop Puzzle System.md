## Обзор

Головоломка «Комната с картинами» (`PaintPuzzle`). Игрок стоит в комнате управления и через глазок наблюдает за комнатой с четырьмя нишами (Q1–Q4). Задача — настроить высоту картин, цвет линз прожекторов и схему питания так, чтобы на всех четырёх полотнах одновременно проявились скрытые символы. Когда все символы видны, открывается скрытая дверь.

---

## Префаб

`Assets/Prefabs/puzzle/paint/PaintPuzzle.prefab`

```
PaintPuzzle                   ← LoopPuzzlePowerCircuit, LoopPuzzleController (IPowerConsumer)
├── Peephole                  ← PeepholeInteractable (+ BoxCollider, слой Interactable Layer)
│   ├── PeepholeCamera        ← CinemachineCamera (вид через глазок)
│   ├── Cube                  ← визуальная заглушка глазка
│   ├── TVCamera              ← Camera (чистая, без скриптов)
│   ├── TVCamera2..6          ← Camera (чистые, без скриптов)
├── tv
│   └── TV_CCTV_01            ← PeepholeTVCamera, TVGlitchEffect
│       ├── PositionSwitch    ← TVChannelButton, ButtonPressAnimation, слой Interactable Layer
│       │   └── buttonPosition ← Transform — физически нажимается
│       ├── pPlane1           ← экран; получает runtime-материал TVGlitch (slot 0)
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


| Файл                         | Назначение                                                                                                                                                                                       |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `LensColor.cs`               | Enum: `None / Red / Blue / Yellow / Green`                                                                                                                                                       |
| `PaintingSpotlight.cs`       | Прожектор с линзой и синтезом                                                                                                                                                                    |
| `SpotlightLensButton.cs`     | Кнопка смены линзы: 4 такта (0→1→2→3→0), поворот физического объекта, ISaveable                                                                                                                  |
| `LoopPuzzleButton.cs`        | Рубильник питания S1–S6, текстурированный emission-гло, блокируется при решении. `CanInteract()` проверяет `enabled` — отключается `ElectricDevice` при отсутствии питания                       |
| `LoopPuzzlePowerCircuit.cs`  | Схема питания (OR-of-AND), `LockAllSwitches()`, события `OnPowerChanged` и `OnMasterToggled`                                                                                                     |
| `LoopPuzzleController.cs`    | Центральный контроллер условий (ISaveable), cinematic при решении, `AutoSolve()` для читов                                                                                                       |
| `PaintPuzzleUnlockTool.cs`   | Editor-чит: Tools/PuzzlesCheats/Solve Paint Puzzle — мгновенное решение через `AutoSolve()`                                                                                                      |
| `LoopPuzzleHiddenDoor.cs`    | Скользящая дверь (ISaveable)                                                                                                                                                                     |
| `PaintingColumn.cs`          | Ниша с изменяемой высотой (ISaveable), глобальный счётчик `IsAnyMoving`, события `OnMoveFinished` / `OnHeightChanged`                                                                            |
| `PaintingColumnTrigger.cs`   | Кнопка смены высоты: glow во время движения, два независимых флага блокировки (питание / решение). `CanInteract() => enabled` — отключается `LoopPuzzleController` при отсутствии общего питания |
| `PaintingRoomLightSwitch.cs` | Выключатель света, блокируется при решении                                                                                                                                                       |
| `SymbolFader.cs`             | Плавное появление символа с настраиваемым HDR-цветом свечения                                                                                                                                    |
| `PeepholeInteractable.cs`    | Глазок: переключение камеры, modal state                                                                                                                                                         |
| `PeepholeTVCamera.cs`        | Создаёт RenderTexture, циклически активирует одну из N камер, назначает TVGlitch-материал на экран. `OnDisable` заменяет материал на чёрный без эмиссии, `OnEnable` восстанавливает              |
| `TVGlitchEffect.cs`          | Случайные глитч-события: анимирует `_GlitchAmount` на материале экрана                                                                                                                           |
| `TVChannelButton.cs`         | `IInteractable` на кнопке ТВ; вызывает `PeepholeTVCamera.NextCamera()`. `CanInteract()` проверяет `enabled` — отключается `ElectricDevice` при отсутствии питания                                |


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

`_roomLightSwitch` — `[SerializeField]` на `LoopPuzzleController`. Назначить в Inspector. Если не назначен (`null`), `?.SetLocked(true)` молча пропускается.

---

## Cinematic при решении (SolvedCinematicRoutine)

Когда все 4 символа проявились, `OnPuzzleSolved()` запускает корутину `SolvedCinematicRoutine()` вместо мгновенного открытия ящика. Последовательность:

1. **Блокировка ввода и HUD** — `InputManager.SetPlayerInputEnabled(false)`, `InteractionUI.SetVisible(false)` (скрывает crosshair и подсказку), курсор залочен.
2. **Затемнение экрана** — `ScreenFader.Instance.FadeIn(_fadeDuration)`. Переиспользует общий синглтон `ScreenFader` (тот же, что в `PuzzleSolvedCinematic` и `DocumentUI`).
3. **Мгновенное переключение камеры** — `CinemachineBrain.DefaultBlend.Time` временно обнуляется, `_solvedCamera` (CinemachineCamera на объекте `PaintSolved`) активируется с приоритетом 3000. Cut происходит пока экран чёрный — без плавного бленда.
4. **Осветление экрана** — `ScreenFader.Instance.FadeOut(_fadeDuration)`. Игрок видит комнату с картинами через cinematic-камеру.
5. **Звук + ящик** — проигрывается `_solvedClip`, вызывается `_rewardDrawer.AutoOpen(_drawerOpenDuration)` — плавная анимация выезда полки.
6. **Удержание кадра** — `_solvedCameraDuration` секунд (default 3 с) — игрок видит как выезжает полка.
7. **Затемнение экрана** — `ScreenFader.Instance.FadeIn(_fadeDuration)`.
8. **Мгновенный возврат камеры** — `_solvedCamera.Priority = 0`, `SetActive(false)`. Cut происходит пока экран чёрный.
9. **Восстановление бленда** — `CinemachineBrain.DefaultBlend.Time` возвращается к оригинальному значению.
10. **Осветление экрана** — `ScreenFader.Instance.FadeOut(_fadeDuration)`.
11. **Возврат управления и HUD** — `InputManager.SetPlayerInputEnabled(true)`, `InteractionUI.SetVisible(true)`.
12. **Сохранение** — `SaveManager.Instance.Save()`.

### Поля Inspector (Solved Cinematic)


| Поле                    | Тип                 | Default | Описание                                              |
| ----------------------- | ------------------- | ------- | ----------------------------------------------------- |
| `_solvedCamera`         | `CinemachineCamera` | —       | Камера cinematic-кадра (PaintSolved). Start inactive. |
| `_fadeDuration`         | `float`             | 1 с     | Длительность fade to/from black                       |
| `_solvedCameraDuration` | `float`             | 3 с     | Сколько секунд камера удерживает кадр                 |
| `_drawerOpenDuration`   | `float`             | 2 с     | Длительность анимации выезда полки                    |


### Мгновенное переключение камеры

Cinemachine по умолчанию делает плавный бленд между камерами. Чтобы переключение было мгновенным (пока экран чёрный):

- В `Awake` кешируется `CinemachineBrain` с Main Camera и сохраняется `_originalBlendTime`
- Перед переключением вызывается `SetBlendDuration(0f)` — обнуляет `DefaultBlend.Time`
- После переключения — `yield return null` (один кадр) чтобы brain обработал cut
- Перед осветлением экрана оригинальный бленд восстанавливается через `SetBlendDuration(_originalBlendTime)`

### Аварийная очистка

В `OnDestroy()` — если корутина прервана (смена сцены и т.д.):

- `_solvedCamera.Priority = 0`, `SetActive(false)`
- `SetBlendDuration(_originalBlendTime)` — восстановить бленд
- `InputManager.SetPlayerInputEnabled(true)`
- `InteractionUI.SetVisible(true)` — восстановить HUD
- `ScreenFader.Instance.FadeOut(0f)` — мгновенно убрать затемнение

### Восстановление из сейва

При загрузке решённой загадки (`RestoreSolvedState`) cinematic **не** проигрывается — ящик мгновенно открывается через `SnapOpen()`, символы показываются через `ShowAllSymbols()`.

---

## Сохранение (LoopPuzzleController)

`LoopPuzzleController` реализует `ISaveable`. Структура `SaveData`:


| Поле              | Тип      | Когда сохраняется             |
| ----------------- | -------- | ----------------------------- |
| `isSolved`        | `bool`   | всегда                        |
| `switchStates`    | `bool[]` | только если `isSolved = true` |
| `conditionLenses` | `int[]`  | только если `isSolved = true` |


`switchStates` — состояния S1–S6 на момент решения. `conditionLenses` — `LensColor` каждого спотлайта из `_conditions`, приведённый к `int` (`-1` если у условия нет спотлайта).

### Восстановление решённого состояния (`RestoreSolvedState`)

При загрузке с `isSolved = true` вызывается `RestoreSolvedState()` вместо стандартного `Start()`:

1. `_powerCircuit.RestoreSwitchStates(switchStates)` → `EvaluateAndApply()` → нужные прожекторы загораются.
2. `spotlight.SetLens(LensColor)` для каждого условия → цвет линз восстанавливается.
3. `ShowAllSymbols()` — `SymbolFader.Show()` на каждом символе (fade-in).
4. `LockAllInteractions()` — S1–S6 и выключатель света заблокированы.
5. Подписка на события **не** происходит — загадка уже решена.

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

## Глазок (PeepholeInteractable)

- Активирует `CinemachineCamera` → `_peepholeCamera`
- Вызывает `UIManager.PushModalState()` (блокирует движение игрока)
- Курсор остаётся залочен
- Выход: LMB, WASD (новое нажатие), Esc
- Выход работает через **собственные `InputAction**` (не из Player action map), так как `PushModalState()` отключает весь Player map

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

Находится на `TV_CCTV_01`. Управляет списком камер `_cameras` (TVCamera1–6): в любой момент только одна камера активна и рендерит в shared `RenderTexture`. Остальные отключены (`camera.enabled = false`, `targetTexture = null`).

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
| Emission Strength | `_emissionStrength` | Яркость RT-контента                            |
| Noise Amount      | `_noiseAmount`      | Статик (держи 0.03–0.15)                       |
| Noise Speed       | `_noiseSpeed`       | Частота шума (fps)                             |
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

Когда `OnPowerStateChanged(false)` вызывается (питание выключено):


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

- [x] Добавить `BoxCollider` на `Peephole`
- [x] Создать объекты `LensButtons/Button_L1`, `Button_L2`, `Button_L4` в префабе, назначить `SpotlightLensButton` с уникальными `_saveId`
- [x] На `Spotlight_L3` → `PaintingSpotlight._synthesisInputs` назначить `Spotlight_L2` и `Spotlight_L4`
- [x] В `LoopPuzzleController._conditions` выставить `requiredColor`: Q1=Red, Q2=Yellow, Q3=Green, Q4=Blue
- [x] `Global Volume` выставить `Is Global = true`, включить `Post Processing` на `Main Camera`
- [x] `SymbolFader` добавлен на `Symbol_Omega`, `Symbol_Psi`, `Symbol_Sigma`, `Symbol_Delta` в префабе
- [ ] Добавить `PaintingRoomLightSwitch` на объект выключателя в сцене, назначить его в `LoopPuzzleController._roomLightSwitch`
- [ ] На объекты освещения в PaintingRoom добавить компонент **Light Zone** (`LightGroup.cs`) с `Zone Id = "painting_room"`
- [x] Расставить кнопки `Button_L1`, `Button_L2`, `Button_L4` физически в сцене (позиция в ControlRoom)
- [ ] Настроить высоты `_lowY / _midY / _highY` в `PaintingColumn` под реальную геометрию сцены
- [ ] Уточнить код Δ (значение символа Q4) у дизайнера
- [x] **Cinematic**: `CinemachineCamera` добавлена на `PaintSolved`, объект деактивирован, назначен в `LoopPuzzleController._solvedCamera`
- [x] **ScreenFader**: объект `/Canvas/ScreenFader` активен в сцене (`activeSelf = true`)