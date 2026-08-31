## Screwdriver Minigame System

Мини-игра "Skill Check" в стиле Dead by Daylight. Стрелка вращается по кругу с постоянной скоростью. Игрок нажимает ЛКМ в момент, когда стрелка проходит над сектором попадания. Сектор состоит из белой (идеальной) зоны в центре и серой зоны по краям. Прогресс-бар заполняется при попаданиях. Белая зона даёт больше прогресса, серая — меньше. При превышении лимита промахов или полном обороте без клика — провал.

### Структура UI

Панель `ScrewdriverMinigamePanel` живёт в Canvas сцены (не в префабе).

```
Canvas
└── ScrewdriverMinigamePanel              # полноэкранная панель, inactive по умолчанию
    ├── CircleContainer                   # контейнер круга, 260x260, по центру
    │   ├── RingBackground                # Image (Filled, Radial360) — фон круга
    │   ├── GraySector                    # Image (Filled, Radial360) — серая зона
    │   ├── WhiteSector                   # Image (Filled, Radial360) — белая зона
    │   └── Arrow                         # Image, pivot (0.5, 0), 4x130px — стрелка
    ├── ProgressBarBg                     # 300x18, тёмный фон, под кругом
    │   └── ProgressFill                  # Image (Filled, Horizontal) — заполнение
    └── MissCounter                       # TextMeshProUGUI — счётчик промахов
```

### Скрипт

`Assets/Scripts/UI/ScrewdriverMinigamePanel.cs`

### Логика работы

```
Внешний контроллер вызывает StartMinigame()
  → Сброс прогресса и счётчика промахов
  → StartNewCheck(): случайная позиция сектора, случайная стартовая позиция стрелки
    (на расстоянии не менее _minStartGap от сектора)
  → Случайное направление вращения (по/против часовой)

Каждый кадр (Update):
  → Стрелка вращается с _arrowSpeed градусов/сек
  → Накапливается _totalRotation (пройденный угол)
  → При _totalRotation >= 360° (полный оборот без клика) → RegisterMiss()
  → При ЛКМ → EvaluateHit()

EvaluateHit():
  → Стрелка в белой зоне → RegisterHit(_whiteZoneProgress)
  → Стрелка в серой зоне → RegisterHit(_grayZoneProgress)
  → Стрелка вне сектора → RegisterMiss()

RegisterHit():
  → Прогресс += очки зоны
  → OnHit?.Invoke()
  → Прогресс >= _progressGoal → OnCompleted?.Invoke(), остановка
  → Иначе → StartNewCheck() (новый сектор, новая позиция стрелки)

RegisterMiss():
  → _misses++
  → Если _missPenalty > 0 → прогресс -= _missPenalty (не ниже 0)
  → OnMiss?.Invoke()
  → Если _maxMisses > 0 и _misses > _maxMisses → OnFailed?.Invoke(), остановка
  → Иначе → StartNewCheck()
```

### События

| Событие       | Когда срабатывает                              |
| ------------- | ---------------------------------------------- |
| `OnCompleted` | Прогресс-бар полностью заполнен (успех)        |
| `OnHit`       | Попадание в сектор (белая или серая зона)      |
| `OnMiss`      | Промах (вне сектора или полный оборот без ЛКМ) |
| `OnFailed`    | Превышен лимит промахов (провал)               |

### Публичные методы

| Метод             | Описание                                 |
| ----------------- | ---------------------------------------- |
| `StartMinigame()` | Запускает мини-игру, сбрасывает прогресс |
| `StopMinigame()`  | Останавливает мини-игру, скрывает сектор |

### Публичные свойства

| Свойство       | Тип     | Описание                        |
| -------------- | ------- | ------------------------------- |
| `Progress`     | `float` | Текущий прогресс (0..goal)      |
| `ProgressGoal` | `float` | Цель прогресса                  |
| `Misses`       | `int`   | Текущее число промахов          |
| `MaxMisses`    | `int`   | Лимит промахов (0 = без лимита) |
| `IsRunning`    | `bool`  | Запущена ли мини-игра           |

### Параметры инспектора

**Arrow Settings**

| Поле           | Тип     | По умолчанию | Описание                                                   |
| -------------- | ------- | ------------ | ---------------------------------------------------------- |
| `_arrowSpeed`  | `float` | 200          | Скорость вращения стрелки (градусов в секунду)             |
| `_minStartGap` | `float` | 90           | Мин. расстояние старта стрелки от сектора (градусы, 0–180) |

**Sector Settings**

| Поле             | Тип     | По умолчанию | Описание                                      |
| ---------------- | ------- | ------------ | --------------------------------------------- |
| `_sectorSize`    | `float` | 55           | Полный размер сектора (градусы, 10–360)       |
| `_whiteZoneSize` | `float` | 16           | Размер белой зоны по центру сектора (градусы) |

**Progress Settings**

| Поле                 | Тип     | По умолчанию | Описание                           |
| -------------------- | ------- | ------------ | ---------------------------------- |
| `_progressGoal`      | `float` | 100          | Сколько очков нужно для завершения |
| `_whiteZoneProgress` | `float` | 18           | Очки за попадание в белую зону     |
| `_grayZoneProgress`  | `float` | 9            | Очки за попадание в серую зону     |

**Penalty Settings**

| Поле           | Тип     | По умолчанию | Описание                                                   |
| -------------- | ------- | ------------ | ---------------------------------------------------------- |
| `_maxMisses`   | `int`   | 3            | Лимит промахов (0 = без лимита). При превышении — OnFailed |
| `_missPenalty` | `float` | 0            | Откат прогресса при промахе (0 = без отката)               |

**UI References** (автоматически находятся по имени в Awake, можно переназначить)

| Поле               | Что назначить                               |
| ------------------ | ------------------------------------------- |
| `_circleContainer` | `RectTransform` на `CircleContainer`        |
| `_ringBackground`  | `Image` на `CircleContainer/RingBackground` |
| `_graySector`      | `Image` на `CircleContainer/GraySector`     |
| `_whiteSector`     | `Image` на `CircleContainer/WhiteSector`    |
| `_arrow`           | `RectTransform` на `CircleContainer/Arrow`  |
| `_progressBarFill` | `Image` на `ProgressBarBg/ProgressFill`     |
| `_missCounterText` | `TextMeshProUGUI` на `MissCounter`          |

**Colors**

| Поле                 | По умолчанию       | Описание        |
| -------------------- | ------------------ | --------------- |
| `_ringColor`         | (0.16, 0.16, 0.16) | Цвет фона круга |
| `_grayZoneColor`     | (0.42, 0.42, 0.42) | Цвет серой зоны |
| `_whiteZoneColor`    | (0.91, 0.91, 0.91) | Цвет белой зоны |
| `_arrowColor`        | (1.0, 0.25, 0.25)  | Цвет стрелки    |
| `_progressFillColor` | (0.78, 0.25, 0.25) | Цвет заполнения |

### Как визуализируется сектор

Все три Image (RingBackground, GraySector, WhiteSector) используют `Image.Type.Filled` с `FillMethod.Radial360`, `FillOrigin = Top` (0), `FillClockwise = true`.

- **RingBackground**: `fillAmount = 1.0` — полный круг, цвет фона.
- **GraySector**: `fillAmount = _sectorSize / 360`, поворот `localEulerAngles.z = -_sectorStart`.
- **WhiteSector**: `fillAmount = _whiteZoneSize / 360`, поворот `localEulerAngles.z = -(whiteStart)`, где `whiteStart = _sectorStart + (_sectorSize - _whiteZoneSize) / 2`.

Стрелка — Image с pivot `(0.5, 0)`, вращается через `localEulerAngles.z = -_arrowAngle`.

Прогресс-бар — Image с `FillMethod.Horizontal`, `fillAmount = _progress / _progressGoal`.

### Авто-резолв ссылок

В `Awake()` компонент автоматически находит дочерние UI-элементы по имени (`CircleContainer`, `RingBackground`, `GraySector`, `WhiteSector`, `Arrow`, `ProgressBarBg/ProgressFill`, `MissCounter`). Если ссылки не заданы в инспекторе — они резолвятся автоматически. Ручная настройка нужна только при нестандартной структуре.

### Быстрая настройка в сцене

1. Панель `ScrewdriverMinigamePanel` уже создана в `/Canvas` сцены `Game.unity`.
2. Деактивируйте панель в инспекторе (снимите галочку activeSelf).
3. На компоненте `ScrewdriverMinigamePanel` настройте параметры:
   - **Arrow Speed** — скорость стрелки.
   - **Sector Size** / **White Zone Size** — размеры сектора и белой зоны.
   - **Progress Goal** / **White Zone Progress** / **Gray Zone Progress** — настройка прогресса.
   - **Max Misses** — лимит промахов (0 = без лимита).
   - **Miss Penalty** — откат прогресса при промахе (0 = без отката).
4. (Опционально) Назначьте спрайты на RingBackground, GraySector, WhiteSector для кастомного визуала.
5. В вашем контроллере подписывайтесь на события `OnCompleted`, `OnHit`, `OnMiss`, `OnFailed` и вызывайте `StartMinigame()` / `StopMinigame()`.

### Важные детали

- Стрелка всегда стартует на безопасном расстоянии от сектора (`_minStartGap`), чтобы у игрока было время среагировать.
- Полный оборот стрелки (360° от стартовой позиции) без клика засчитывается как промах. Отсчёт ведётся по пройденному углу (`_totalRotation`), а не по переходу через 0° — это исключает ложные срабатывания.
- Направление вращения случайно для каждой проверки (по часовой или против).
- После каждого попадания или промаха (если не конец игры) запускается новый чек — сектор и стрелка перегенерируются.
- Все углы внутренне считаются в градусах, 0° = верх круга, направление по часовой.
- Используется `Time.unscaledDeltaTime` — мини-игра не зависит от `Time.timeScale`.
- Ввод считывается через `Mouse.current.leftButton.wasPressedThisFrame` (Input System).
