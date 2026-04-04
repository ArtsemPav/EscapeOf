# Pressure Puzzle System

Загадка на давление: игрок переключает рычаги чтобы выровнять стрелку циферблата в нейтральную позицию (0°). Каждая сессия — новая случайная выигрышная комбинация. Загадка **всегда решаема по построению** — маппинг стрелки сдвигается под выбранное решение. Поддерживает режим реального времени и режим подтверждения.

---

## Компоненты

### `PressureLever`

Ставится на каждый рычаг. Обрабатывает визуальный поворот и звук. **Значения давления не задаются в Inspector** — они назначаются в рантайме через `PressurePuzzle.GenerateAndAssignLeverValues()`.

| Поле | Описание |
|---|---|
| `_angleOnDelta` | Дельта поворота по оси Z при переключении в ON. OFF остаётся в позиции размещения в редакторе |
| `_rotationSpeed` | Скорость анимации поворота рычага (Lerp) |
| `_switchClip` | AudioClip при переключении |
| `_switchVolume` | Громкость клипа |
| `_textWhenOff` | Текст подсказки когда рычаг выключен |
| `_textWhenOn` | Текст подсказки когда рычаг включён |

**Публичные методы**

| Метод | Описание |
|---|---|
| `SetStateQuiet(bool on)` | Устанавливает состояние мгновенно без анимации и без вызова событий. Используется `PressurePuzzle` при инициализации |
| `SnapVisual()` | Принудительно синхронизирует трансформ с текущим `IsOn`. Вызывается из `PressurePuzzle.Start()` в конце рандомизации — гарантирует корректный визуал независимо от порядка инициализации скриптов |
| `CanInteract()` | Возвращает `false` если загадка решена. `FPSController` полностью исключает объект из взаимодействия — нет подсказки, нет смены прицела |

### `PressurePuzzle`

Ставится на корневой GameObject загадки. Генерирует значения рычагов, выбирает случайное решение, управляет стрелкой и фиксирует победу.

**References**

| Поле | Описание |
|---|---|
| `_arrow` | Transform стрелки внутри циферблата (`screen/arrow`) |
| `_saveId` | Уникальный ID для системы сохранений. Никогда не меняй после первого сохранения |

**Dial Settings**

| Поле | Описание |
|---|---|
| `_arrowAngleAtMin` | Угол X стрелки при минимальном суммарном давлении |
| `_arrowAngleAtMax` | Угол X стрелки при максимальном суммарном давлении |
| `_arrowSmoothSpeed` | Скорость анимации стрелки (SmoothDamp) |
| `_solveAngleTolerance` | Допуск от 0° при котором загадка считается решённой. Держи маленьким (1–5°) — большой допуск создаёт много побочных решений и ослабляет гарантию минимального числа переключений |

**Puzzle**

| Поле | Описание |
|---|---|
| `_confirmOnInteract` | Если включено — стрелка двигается только при взаимодействии с манометром, не при каждом рычаге |
| `_rewardObjects` | GameObjects активируемые при решении (двери, свет и т.д.) |
| `_onSolved` | UnityEvent срабатывающий один раз при решении |

**Randomization**

| Поле | Описание |
|---|---|
| `_minStartDistanceFraction` | Минимальное стартовое расстояние угла от 0° как доля от полного диапазона (0–1). Предотвращает старт рядом с решением |
| `_minFlipsFromSolution` | Минимальное количество переключений рычагов до **любого** валидного решения из стартового состояния. Проверяется против всех выигрышных комбинаций, не только основной |

**Solution**

| Поле | Описание |
|---|---|
| `_minLeversOnInSolution` | Минимальное количество рычагов в положении ON в случайно выбранном решении. Такой же минимум применяется к OFF. Требует минимум `2 × значение` рычагов всего |

**Lever Value Generation**

| Поле | Описание |
|---|---|
| `_leverValueBase` | Величина наименьшего рычага. Каждый рычаг получает `offValue = –magnitude`, `onValue = +magnitude` |
| `_leverValueStep` | Шаг между соседними величинами. При 6 рычагах, base=5, step=5 → величины: 5, 10, 15, 20, 25, 30 (перемешаны каждую сессию) |

**Публичные свойства**

| Свойство | Описание |
|---|---|
| `IsSolved` | `true` после решения загадки |
| `ConfirmOnInteract` | Публичный read-only доступ к `_confirmOnInteract`. Используется `PressureGauge.CanInteract()` |

### `PressureGauge`

Ставится на коллайдер манометра (`screen`). Реализует `IInteractable` — вызывает `PressurePuzzle.Confirm()`.

| Поле | Описание |
|---|---|
| `_interactText` | Текст подсказки в прицеле |

**`CanInteract()`** возвращает `false` в двух случаях:
- загадка уже решена (`IsSolved`)
- галочка `_confirmOnInteract` **снята** — в режиме реального времени стрелка следит за рычагами сама, манометр не является точкой взаимодействия

---

## Иерархия в сцене

```
PreasurePuzzel              ← PressurePuzzle
  stick1                    ← PressureLever, Interactable Layer
  stick2
  ...
  screen                    ← PressureGauge
    Plate                   ← MeshCollider, Interactable Layer
    arrow                   ← назначить в PressurePuzzle._arrow
```

Количество рычагов определяется числом дочерних GameObjects с компонентом `PressureLever`. Добавляй или удаляй стики в иерархии — изменений в коде не требуется.

---

## Жизненный цикл сессии

```
Start()
 ├── Собрать дочерние PressureLever
 ├── Кэшировать euler стрелки (защита от gimbal lock)
 ├── [если сохранение = решено] RestoreSolvedState() → выход
 │
 ├── GenerateAndAssignLeverValues()
 │     Величины = [base, base+step, ..., base+(N-1)·step]
 │     Fisher-Yates shuffle → назначение в случайном порядке
 │     Каждый рычаг: offValue = –magnitude, onValue = +magnitude
 │
 ├── Вычислить _minTotal / _maxTotal
 │
 ├── PickRandomSolution()
 │     Случайная маска с minOn ≤ ON-count ≤ (N – minOn)
 │     Сохраняет _solutionTotal и _solutionMask
 │
 ├── FindAllValidSolutions()
 │     Перебирает все 2^N масок
 │     Записывает все маски где |PressureToAngle| ≤ _solveAngleTolerance
 │
 └── RandomizeLevers()
       Проход 1: угол ≥ minDistance  И  MinFlipsToAnySolution ≥ _minFlipsFromSolution
        После выхода → SnapVisual() на каждом рычаге (гарантия визуала независимо от порядка Awake/Start)

       Проход 2: только MinFlipsToAnySolution ≥ _minFlipsFromSolution  (угол расслаблен)
```

---

## Гарантия решаемости

`PressureToAngle()` сдвигает маппинг так что `_solutionTotal` всегда соответствует 0°:

```csharp
float raw      = Lerp(atMin, atMax, InverseLerp(minTotal, maxTotal, pressure));
float solAngle = Lerp(atMin, atMax, InverseLerp(minTotal, maxTotal, _solutionTotal));
return raw - solAngle;   // 0° когда pressure == _solutionTotal
```

Никакой ручной проверки решаемости не нужно. HelpBox в Inspector показывает величины и количество валидных комбинаций.

---

## Гарантия минимального числа переключений

`_minFlipsFromSolution` проверяется против **всех** валидных комбинаций найденных `FindAllValidSolutions()`. Это закрывает обходной путь через второстепенную выигрышную комбинацию на расстоянии 1 переключения:

```
MinFlipsToAnySolution(startMask) = min(popcount(startMask XOR sol))
                                   для каждого sol в _validSolutionMasks

RandomizeLevers() отклоняет любой старт где это значение < _minFlipsFromSolution
```

---

## Режимы взаимодействия

### Реальное время (`_confirmOnInteract = false`)
Стрелка следует за суммой рычагов мгновенно. Загадка решается автоматически при достижении верной комбинации. Манометр **не интерактивен** — `CanInteract()` возвращает `false`.

### Подтверждение (`_confirmOnInteract = true`)
Стрелка не двигается пока игрок не нажмёт на манометр. Убирает живую обратную связь — игрок считает результат мысленно.

---

## Управление интерактивностью

Система использует `IInteractable.CanInteract()` — метод с дефолтной реализацией `true`. `FPSController.HandleInteractionDetection()` проверяет его первым: при `false` объект полностью исключается из обработки (нет подсказки, нет смены прицела, `Interact()` не вызывается).

| Компонент | Условие `CanInteract() = false` |
|---|---|
| `PressureLever` | `puzzle.IsSolved` |
| `PressureGauge` | `puzzle.IsSolved` ИЛИ `!puzzle.ConfirmOnInteract` |

---

## Фиксация решения

Решение засчитывается в `Update()` в момент когда стрелка **визуально входит** в зону допуска — не после полной асимптотической сходимости SmoothDamp. Это устраняет воспринимаемую паузу между "стрелка встала" и "загадка решена":

```
Update() каждый кадр:
  SmoothDamp двигает стрелку к _targetArrowAngle
  │
  ├── если |targetAngle| ≤ tolerance И |currentAngle| ≤ tolerance
  │     → snap + Solve() немедленно  ← визуальный вход в зону
  │
  └── если |current – target| < 0.01°
        → snap + Solve() (резервная проверка при финальном снапе)
```

Условие на `_targetArrowAngle` предотвращает ложное срабатывание когда стрелка просто проходит через ноль по дороге к другой цели.

---

## Система сохранений

`PressurePuzzle` реализует `ISaveable`. При решении сериализуются `isSolved` и состояния рычагов (`leverStates[]`) — позиции выигрышной комбинации. При восстановлении:

- `LoadSaveData()` сохраняет оба значения **до** `Start()`
- `Start()` пропускает генерацию и рандомизацию
- `RestoreSolvedState()` применяет позиции рычагов из сохранения, снапает стрелку в 0°, активирует награды — события не вызываются

Игрок видит именно ту комбинацию рычагов которую он использовал для решения.

---

## Настройка с нуля

1. Создай корневой GameObject → добавь `PressurePuzzle`
2. Добавь дочерние рычаги с `PressureLever` на **Interactable Layer**
3. Настрой `_angleOnDelta` на каждом рычаге (обычно –180°)
4. Назначь Transform `arrow` в `PressurePuzzle._arrow`
5. Добавь `PressureGauge` на `screen` (дочерний коллайдер на **Interactable Layer**)
6. Добавь объекты в `_rewardObjects`
7. Настрой `_arrowAngleAtMin` / `_arrowAngleAtMax` по физическому виду циферблата
8. Настрой `_leverValueBase` и `_leverValueStep` — Inspector показывает итоговые величины и диапазон
9. Задай `_minLeversOnInSolution` (по умолчанию: 2)
10. Задай `_minFlipsFromSolution` (по умолчанию: 3)
11. Держи `_solveAngleTolerance` маленьким (1–5°)
12. Задай уникальный `_saveId`

---

## Вывод в консоль

```
[PressurePuzzle] Lever magnitudes assigned: [20, 5, 30, 10, 25, 15]
[PressurePuzzle] Solution chosen: 3/6 levers ON, total = 15, mask = 010110
[PressurePuzzle] 1 valid solution combination(s) found within ±3°.
[PressurePuzzle] 6 levers. Range [–105…105]. Solution total: 15. Solve at 0° ±3°. Start angle: –127.4°
```
