## Обзор

Головоломка «Комната с картинами» (`PaintPuzzle`). Игрок стоит в комнате управления и через глазок наблюдает за комнатой с четырьмя нишами (Q1–Q4). Задача — настроить высоту картин, цвет линз прожекторов и схему питания так, чтобы на всех четырёх полотнах одновременно проявились скрытые символы. Когда все символы видны, открывается скрытая дверь.

---

## Префаб

`Assets/Prefabs/puzzle/paint/PaintPuzzle.prefab`

```
PaintPuzzle                   ← LoopPuzzlePowerCircuit, LoopPuzzleController
├── Peephole                  ← PeepholeInteractable (+ BoxCollider, слой Interactable Layer)
│   ├── PeepholeCamera        ← CinemachineCamera (вид через глазок)
│   ├── Cube                  ← визуальная заглушка глазка
│   └── TVCamera              ← Camera + PeepholeTVCamera + TVGlitchEffect
├── tv
│   └── TV_CCTV_01
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

\* `LensButtons` для L3 не нужна — синтетический прожектор.

---

## Скрипты

| Файл | Назначение |
|---|---|
| `LensColor.cs` | Enum: `None / Red / Blue / Yellow / Green` |
| `PaintingSpotlight.cs` | Прожектор с линзой и синтезом |
| `SpotlightLensButton.cs` | Кнопка смены линзы (ISaveable) |
| `LoopPuzzleButton.cs` | Рубильник питания S1–S6, блокируется при решении |
| `LoopPuzzlePowerCircuit.cs` | Схема питания (OR-of-AND), `LockAllSwitches()` |
| `LoopPuzzleController.cs` | Центральный контроллер условий (ISaveable) |
| `LoopPuzzleHiddenDoor.cs` | Скользящая дверь (ISaveable) |
| `PaintingColumn.cs` | Ниша с изменяемой высотой (ISaveable) |
| `PaintingColumnTrigger.cs` | Кнопка смены высоты картины |
| `PaintingRoomLightSwitch.cs` | Выключатель света, блокируется при решении |
| `SymbolFader.cs` | Плавное появление символа + аддитивный блендинг |
| `PeepholeInteractable.cs` | Глазок: переключение камеры, modal state |
| `PeepholeTVCamera.cs` | Создаёт RenderTexture, назначает TVGlitch-материал на экран, синхронизирует параметры шейдера каждый кадр |
| `TVGlitchEffect.cs` | Случайные глитч-события: анимирует `_GlitchAmount` на материале экрана |

---

## Матрица активации

| Ниша | Цвет луча | Высота | Символ | Код |
|---|---|---|---|---|
| Q1 (Младенец) | Red (L1) | TOP | Ω | 4 |
| Q2 (Воин) | Yellow (L2) | BOT | Ψ | 9 |
| Q3 (Судья) | Green (L2 Blue + L4 Yellow) | MID | Σ | 2 |
| Q4 (Тень) | Blue (L4) | BOT | Δ | ? |

Значение для Q4 (Δ) не было указано — уточнить с дизайнером.

---

## SymbolFader — плавное появление и аддитивный блендинг

`SymbolFader` — компонент на каждом `Symbol_*` объекте (вместо `SetActive`).

### Поведение

- `Show()` — фейд-ин по `SpriteRenderer.color.a` за `_fadeDuration` (0.5 с по умолчанию). Активирует GameObject если он был неактивен.
- `Hide()` — фейд-аут. Не деактивирует GameObject — оставляет активным с `alpha = 0` чтобы последующий `Show()` мог сразу запустить корутину.
- `HideImmediate()` — мгновенное скрытие без анимации. Используется при инициализации и reset.
- `IsTargetVisible` — логическое состояние (не зависит от текущей анимации). `LoopPuzzleController.CheckWinCondition` читает это значение вместо `activeSelf`.

### Аддитивный блендинг

В `Awake()` компонент инстанцирует материал (`_renderer.material`) и устанавливает:

```
_SrcBlend = SrcAlpha (5)
_DstBlend = One (1)
```

Результат: `Symbol × alpha + Scene × 1` — символ накладывается на картину, не перекрывая её. Работает с `Sprites/Default` и `Universal Render Pipeline/2D/Sprite-Unlit-Default`. Если шейдер не поддерживает эти свойства (`HasProperty` = false), блендинг молча пропускается.

### Настройка в префабе

`SymbolFader` уже добавлен на все четыре символа в `PaintPuzzle.prefab`:

```
PaintingColumn_Q1/Symbol_Omega   ← SymbolFader (_fadeDuration = 0.5)
PaintingColumn_Q2/Symbol_Psi     ← SymbolFader (_fadeDuration = 0.5)
PaintingColumn_Q3/Symbol_Sigma   ← SymbolFader (_fadeDuration = 0.5)
PaintingColumn_Q4/Symbol_Delta   ← SymbolFader (_fadeDuration = 0.5)
```

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
|---|---|
| L1 | S1=ON |
| L2 | (S1+S2) OR (S4) |
| L3 | (S5+S3+S1+S2) OR (S5+S3+S4) |
| L4 | S3=ON |

### Lights Out — матрица смежности

S1–S5 реализуют загадку Lights Out. Нажатие рубильника Si переключает его самого и всех соседей по матрице смежности. Матрица задаётся в Inspector компонента `LoopPuzzlePowerCircuit` через визуальный редактор.

Текущая конфигурация (сложная, 3 соседа у части переключателей) имеет одно решение — проверяется кнопкой **«Проверить (GF(2) анализ)»** в Inspector.

### Игровой процесс

1. Игрок нажимает S6 → S1–S5 разблокируются.
2. Игрок нажимает S1–S5 — каждое нажатие применяет Lights Out каскад.
3. Когда правильная комбинация активна → все прожекторы включаются.
4. `LoopPuzzleController` проверяет высоту картин и цвет линз → символы проявляются → дверь открывается.

### Блокировка при решении

При решении загадки `LoopPuzzlePowerCircuit.LockAllSwitches()` блокирует все S1–S6. После этого `LoopPuzzleButton.SetLocked(true)` предотвращает любое взаимодействие с рубильниками.

---

## Блокировка взаимодействий при решении

Когда загадка решена (`OnPuzzleSolved`), `LoopPuzzleController.LockAllInteractions()` вызывает:

- `_powerCircuit.LockAllSwitches()` — блокирует S1–S6
- `_roomLightSwitch.SetLocked(true)` — блокирует выключатель света

**`PaintingRoomLightSwitch.SetLocked(bool)`** — новый метод. Когда заблокирован, `Interact()` возвращает управление сразу, `GetInteractText()` возвращает `_lockedInteractText` (по умолчанию: «Заблокировано»).

`_roomLightSwitch` — `[SerializeField]` на `LoopPuzzleController`. Назначить в Inspector. Если не назначен (`null`), `?.SetLocked(true)` молча пропускается.

---

## Сохранение (LoopPuzzleController)

`LoopPuzzleController` реализует `ISaveable`. Структура `SaveData`:

| Поле | Тип | Когда сохраняется |
|---|---|---|
| `isSolved` | `bool` | всегда |
| `switchStates` | `bool[]` | только если `isSolved = true` |
| `conditionLenses` | `int[]` | только если `isSolved = true` |

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

| Элемент | Цвет |
|---|---|
| L1 правила | Оранжевый |
| L2 правила | Голубой |
| L3 правила | Фиолетовый |
| L4 правила | Лаймовый |
| Смежность dist=1 | Ярко-жёлтый |
| Смежность dist=2 | Золотой |
| Смежность dist=3 | Янтарный |
| Смежность dist=4+ | Оранжево-красный |

### Условные обозначения линий

- **Сплошная** — первая AND-группа правила.
- **Пунктирная** — дополнительная OR-группа того же прожектора.
- **Точка ●** на шине — реальное соединение (не пересечение).

### Маршрутизация правил (bus-lane routing)

Каждый прожектор имеет свою горизонтальную шину на отдельном уровне между рядами узлов. Линии от рубильников идут вертикально до шины своего прожектора, затем горизонтально до вертикали прожектора, затем вертикально к узлу. Это исключает слияние линий разных прожекторов.

### Дуги смежности (Lights Out)

Дуги идут ниже ряда рубильников. Высота дуги пропорциональна дистанции между рубильниками: `depth = 26 + dist × 26 px`. Рисуются от дальних к ближним — ближние дуги поверх дальних.

---

## Индикаторы кнопок (LoopPuzzleButton)

Каждый рубильник `Button_S1..S6` имеет дочерний `Cube` с материалом `ButtonIndicator.mat` (`Assets/Materials/LightPuzzle/ButtonIndicator.mat`).

| Состояние | `_BaseColor` | `_EmissionColor` |
|---|---|---|
| OFF | `(0.05, 0.05, 0.05)` — почти чёрный | `black` — нет свечения |
| ON | `(0, 1, 0.3)` — зелёный | HDR `_activeEmissionColor` — bloom-свечение |

**Требования к сцене:**
- `Global Volume` → `Is Global = true`, содержит `Bloom`
- `Main Camera` → `Universal Additional Camera Data` → `Post Processing = enabled`
- Материал `ButtonIndicator.mat` → `Emission` keyword включён, `Global Illumination = Realtime`

---

## Взаимодействие (FPSController)

`LoopPuzzleButton` использует `UseLMBClick = true` — срабатывает по ЛКМ через `HandleDragInteraction()`.

`FPSController.OnInteract()` обрабатывает **только** объекты где `UseLMBClick = false` (E-клавиша), чтобы избежать двойного вызова `Interact()` в один кадр.

---

## Глазок (PeepholeInteractable)

- Активирует `CinemachineCamera` → `_peepholeCamera`
- Вызывает `UIManager.PushModalState()` (блокирует движение игрока)
- Курсор остаётся залочен
- Выход: LMB, WASD (новое нажатие), Esc
- Выход работает через **собственные `InputAction`** (не из Player action map), так как `PushModalState()` отключает весь Player map

---

## TV-экран (PeepholeTVCamera + TVGlitchEffect)

### Шейдер

`Assets/Shaders/TVGlitch.shader` — кастомный URP Unlit шейдер (`Custom/TVGlitch`).

| Свойство | Тип | Описание |
|---|---|---|
| `_BaseMap` | Texture2D | RenderTexture от `TVCamera` |
| `_ScanlineCount` | Float | Количество скан-линий (180 по умолчанию) |
| `_ScanlineDarkness` | Range(0, 0.5) | Затемнение между линиями |
| `_NoiseAmount` | Range(0, 1) | Доля статика поверх изображения |
| `_NoiseSpeed` | Range(1, 60) | Частота смены кадра шума (fps) |
| `_EmissionStrength` | Range(0, 10) | Множитель яркости RT-контента |
| `_EmissionColor` | HDR Color | Аддитивное свечение экрана (не зависит от контента) |
| `_GlitchAmount` | Range(0, 1) | Интенсивность глитча — управляется скриптом |

### PeepholeTVCamera

Находится на `TVCamera`. Создаёт `RenderTexture`, направляет на неё камеру, создаёт runtime-экземпляр материала `Custom/TVGlitch` и назначает его на `pPlane1` (slot `_materialIndex`).

Поля в секции **Screen Appearance** в Inspector:

| Поле Inspector | Поле C# | Описание |
|---|---|---|
| Emission Strength | `_emissionStrength` | Яркость RT-контента (default 2.5) |
| Noise Amount | `_noiseAmount` | Статик (default 0.12) |
| Emission Color | `_emissionColor` | HDR свечение экрана (default black) |

Значения синхронизируются с материалом каждый кадр (`Update` → `SyncMaterialProperties`) — изменения в Inspector работают в реальном времени в Play Mode.

### TVGlitchEffect

Находится на том же `TVCamera`. Периодически запускает корутины глитча: снапает `_GlitchAmount` до случайного значения, затем плавно сводит к 0. Поддерживает burst-режим (несколько глитчей подряд).

### Настройка яркости экрана

- Если изображение **тусклое** — подними **Emission Strength** (4–8).
- Если изображение **чёрное** — проверь что RT-камера (`TVCamera`) имеет `Target Texture` назначен (в Awake назначается автоматически), и что сцена освещена.
- Для **свечения экрана** (bloom-ореол) — выставь **Emission Color** в HDR с `Intensity > 1` (значение > 1.0 по порогу bloom).
- **Noise Amount** держи в диапазоне `0.05–0.15`; значения выше 0.5 делают экран почти полным статиком.

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
- [ ] Расставить кнопки `Button_L1`, `Button_L2`, `Button_L4` физически в сцене (позиция в ControlRoom)
- [ ] Настроить высоты `_lowY / _midY / _highY` в `PaintingColumn` под реальную геометрию сцены
- [ ] Уточнить код Δ (значение символа Q4) у дизайнера
