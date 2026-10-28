## Обзор

Финальная загадка игры — двухфазная головоломка у последней двери.

**Фаза 1 (медальоны):** игрок осматривает дверь/статуи через отдельные камеры, расставляет 6 медальонов по гнёздам путём drag-and-drop из панели инвентаря. Любой медальон можно вставить в любое гнездо и свободно забрать обратно, пока все 6 не встанут на правильные места. Затем играется кинематик: затемнение → камера обзора → анимация черепа (`scullMove`) → выход из puzzle mode.

**Фаза 2 (череп):** череп становится интерактивным. Игрок вставляет две монеты в глазницы (порядок и сторона не важны) → открывается челюсть (`ScullOpen`) → игрок перетаскивает `SerumColba` на череп → анимация выливания сыворотки → `SetSolved()`.

**Финал:** после решения `EndGameSequence` берёт управление на себя — открывает дверь, ведёт игрока сквозь неё с гейнз-хореографией (взгляды на статуи Смерти и Ангела), затемняет экран и показывает титры. Титры стирают сейв и возвращают игру на стартовую сцену.

Оба состояния (фаза 1 и фаза 2) сохраняются через Save System.

Скрипты: `Assets/Scripts/Puzzle/FinalDoor/`.

---

## Структура системы

```
FinalDoorPuzzle (root)
├── FinalDoorPuzzleController   # puzzle mode: камеры, курсор, фонарь, сейв solved
├── FinalDoorPuzzleInteraction  # логика фазы 1: гнёзда, кинематик активации, сейв
├── FinalDoorPuzzleUI           # drag-and-drop медальонов, ghost, hover, победа
├── EndGameSequence             # финальный кинематик после OnSolved
├── Collider                    # fallback-взаимодействие с корнем
├── CinemachineCamera "All/Overview"    # камера обзора (автопоиск по имени)
├── Статуи (по одной на сторону)
│   ├── FinalDoorSideInteractable       # вход в puzzle mode с камерой статуи
│   └── CinemachineCamera               # close-up камеры статуй
├── Гнёзда MedallionHole × 6            # компоненты гнёзд двери
└── Scull
    ├── ScullPuzzlePhase        # фаза 2: монеты, челюсть, сыворотка, сейв
    ├── CinemachineCamera       # close-up черепа
    └── Animator                # scullMove / ScullOpen
```

---

## Компоненты

### `FinalDoorPuzzleController`

Файл: `Assets/Scripts/Puzzle/FinalDoor/FinalDoorPuzzleController.cs`. Контроллер puzzle mode в стиле других загадок: управляет камерами (Cinemachine, приоритет 2000 перекрывает камеру игрока), курсором, модальным состоянием `UIManager`, панелью инвентаря (`PuzzleInventoryBar`), фонарём и lens distortion. Реализует `ISaveable` и `IInteractable` (fallback на корне — обычно взаимодействие перехватывают статуи через `FinalDoorSideInteractable`).

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_saveId` | ID сохранения (default: `"final_door_puzzle_mode"`), хранит только `isSolved` |
| `_overviewCamera` | Камера обзора для кинематика. Автопоиск по имени `"All"`/`"Overview"` если пусто |
| `_statueCameras` | Камеры статуй. Автообнаружение из дочерних объектов, если пусто |
| `_blendDuration` | Длительность бленда переходов внутри puzzle mode в сек (default: 0.75) |
| `_showInventoryBar` | Показывать `PuzzleInventoryBar` в puzzle mode (default: `true`) |
| `_disableFlashlightInPuzzle` | Принудительно выключить фонарь в puzzle mode (default: `true`) |
| `_flashlightSupportLights` | Свет-компенсация на время отключённого фонаря |
| `_interactText` | Подсказка fallback-взаимодействия (default: `"Осмотреть"`) |
| `_crosshairMode` | Режим прицела (default: `Hand`) |
| `_useExitPopup` / `_exitPopupText` / `_exitPopupType` | Попап-подсказка «Выход: Esc» при входе (default: `Warning`, 3 сек) |
| `OnPuzzleModeEntered` / `OnPuzzleModeExited` / `OnPuzzleSolved` | `UnityEvent` |

**События (C#):** `OnEntered`, `OnExited`, `OnSolved`.

**Публичные свойства:** `IsActive`, `IsSolved`, `IsPhase1Completed`, `ActiveCamera`.

**Публичные методы**

| Метод | Описание |
| ----- | -------- |
| `EnterPuzzleMode(entryCamera, dropHandler)` | Вход в puzzle mode: мгновенный кат на камеру, модальное состояние UI, курсор, фонарь off, инвентарь-бар |
| `ExitPuzzleMode()` / `ExitPuzzleModeInstant()` | Выход с блендом / мгновенно (для затемнений) |
| `SwitchCamera(cam)` / `SwitchCameraInstant(cam)` | Переключение камеры внутри режима (плавно / кат) |
| `SwitchToOverview()` / `SwitchToOverviewInstant()` | Переход к камере обзора |
| `SetSolved()` | Помечает загадку решённой, шлёт события, выходит из режима, сохраняет |
| `MarkPhase1Completed()` | Блокирует взаимодействия корня и статуй — работает только череп |

Esc (`InputManager.OnMenuPerformed`) выходит из режима, но только если все дочерние `IPuzzleExitGuard` разрешают (`CanExitPuzzle`).

### `FinalDoorSideInteractable`

Файл: `Assets/Scripts/Puzzle/FinalDoor/FinalDoorSideInteractable.cs`. Лёгкий `IInteractable` на каждой статуе. `FPSController` находит его через `TryGetComponent` на коллайдере статуи — он имеет приоритет над fallback-интеракцией корня.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_controller` | `FinalDoorPuzzleController`. Автопоиск `GetComponentInParent` если пусто |
| `_camera` | `CinemachineCamera` этой статуи — активируется мгновенно при взаимодействии |
| `_interactText` | Подсказка (default: `"Осмотреть"`) |
| `_crosshairMode` | Режим прицела (default: `Hand`) |

`CanInteract()` возвращает `false`, когда режим активен, загадка решена или фаза 1 завершена.

### `FinalDoorPuzzleInteraction`

Файл: `Assets/Scripts/Puzzle/FinalDoor/FinalDoorPuzzleInteraction.cs`. Логика фазы 1 и кинематик активации. `[DefaultExecutionOrder(-7)]`. Реализует `ISaveable`, `IPuzzleDropHandler` (делегирует в UI), `IPuzzleExitGuard`. SaveId фиксированный: `"final_door_puzzle"` — хранит `phase1Completed` и id медальонов во всех гнёздах.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_controller` | Контроллер (автопоиск на том же объекте) |
| `_doorHoles` | 6 гнёзд `MedallionHole` в порядке 0..5. Автопоиск из детей если пусто |
| `_doorMedallionOrder` | Правильный `ItemData` для каждого гнезда, в порядке 0..5 |
| `_allMedallions` | Все медальоны загадки — для восстановления из сейва (порядок не важен) |
| `_activationAnimator` | Аниматор активации двери (триггер `_activationTrigger`, default `"Activate"`) |
| `_scullAnimator` | Аниматор черепа — играет состояние `_scullMoveStateName` (default: `"scullMove"`) |
| `_activationDuration` | Запас времени ожидания анимации в сек (default: 3) |
| `_fadeDuration` | Длительность фейдов кинематика в сек (default: 1) |
| `_solvedObject` | GameObject, активируемый после кинематика |
| `_scullPhase` | `ScullPuzzlePhase` — активируется после фазы 1 (автопоиск в детях) |
| `_activationClip` / `_solvedClip` / `_coinDropClip` / `_coinPickupClip` + тома | Звуки активации, решения, вставки/подбора медальона |

**Кинематик активации (после `FinalDoorPuzzleUI.OnPuzzleSolved`):**

```
FadeIn (чёрный экран)
  → SwitchToOverviewInstant (кат на камеру обзора)
  → FadeOut (виден дверной проём)
  → _activationAnimator.SetTrigger("Activate") + _scullAnimator.Play("scullMove")
  → ожидание конца анимации (или _activationDuration fallback)
  → FadeIn
  → _solvedObject.SetActive(true)
  → ExitPuzzleModeInstant + MarkPhase1Completed
  → _scullPhase.Activate()   # фаза 2 стала доступна
  → FadeOut → SaveManager.Save()
```

Восстановление из сейва: гнёзда заполняются через `RestoreState` (без анимации), аниматоры перематываются в конечное состояние (`Play("Activated", 0, 1f)`, `Play("scullMove", 0, 1f)`), фаза 2 активируется. Поиск предметов для восстановления: `_allMedallions` → `_doorMedallionOrder` → `InventorySystem.AllItems`.

### `FinalDoorPuzzleUI`

Файл: `Assets/Scripts/Puzzle/FinalDoor/FinalDoorPuzzleUI.cs`. UI-контроллер фазы 1: drag-and-drop медальонов в 3D-гнёзда, ghost-превью при перетаскивании, hover-подсветка заполненных гнёзд, извлечение по ЛКМ. Реализует `IPuzzleDropHandler` — контроллер находит его через `GetComponentInChildren` при входе в режим. Камерами не управляет.

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_doorHoles` | Гнёзда в порядке 0..5 (автопоиск из детей) |
| `_holeLayer` | LayerMask коллайдеров гнёзд для рейкастов |
| `_coinPrefab` | Fallback-префаб монеты, если `ItemData.inspectionPrefab` пуст |
| `_dropHeight` | Высота старта анимации падения в метрах (default: 0.005) |
| `_dropDuration` | Длительность падения в сек (default: 0.35) |
| `_controller` | Контроллер (автопоиск на том же объекте) |

**Событие:** `OnPuzzleSolved` — все 6 гнёзд заполнены правильно (`PlacedItem == _medallionOrder[i]` повсюду). `MarkSolved()` блокирует дальнейшие взаимодействия. `RestoreState` заполняет гнёзда по id и молча проверяет победу (без события).

### `ScullPuzzlePhase`

Файл: `Assets/Scripts/Puzzle/FinalDoor/ScullPuzzlePhase.cs`. Фаза 2 — череп. Реализует `IInteractable`, `IPuzzleDropHandler`, `IPuzzleExitGuard`, `ISaveable`. SaveId: `"final_door_scull_phase"` — хранит `activated`, `jawOpen`, `serumPoured` и id монет в глазницах.

**Inspector (основные поля)**

| Поле | Описание |
| ---- | -------- |
| `_controller` | Контроллер (автопоиск по родителю) |
| `_scullCamera` | Close-up камера черепа |
| `_eye0Object` / `_eye1Object` | Визуалы монет в глазницах (Coin1/Coin2), скрыты до вставки |
| `_eyeLayer` | LayerMask коллайдеров глазниц (default: bit 10, PuzzleInteractable) |
| `_scullAnimator` / `_scullOpenStateName` | Аниматор и состояние открытия челюсти (default: `"ScullOpen"`) |
| `_coinItems` | Принимаемые монеты (Coin 1, Coin 2) — любая в любую глазницу |
| `_serumColbaItem` | `ItemData` колбы с сывороткой |
| `_colbaTransform` / `_colbaReadTransform` | Transforms колбы: исходный и финальный (`colbaRead`) для анимации переворота |
| `_liquidWobble` | `ChemicalPuzzle.LiquidWobble` жидкость — `fillFraction` анимируется до 0 при выливании |
| `_liquidRenderer` | Рендерер жидкости |
| `_pourDuration` / `_flipDuration` | Длительности анимаций выливания и переворота в сек (default: 2 / 1) |
| `_scullDropCollider` / `_scullDropLayer` | Коллайдер черепа для приёма колбы (default layer bit 7, Interactable); включается после открытия челюсти |
| `_coinPrefab` / `_coinRenderingLayerMask` | Ghost-префаб монеты и URP rendering layer mask (default: биты 22 `CoridorFDoor` + 9 `flashLight`) |
| `_dropHeight` / `_dropDuration` | Анимация вставки монеты (default: 0.05 / 0.3) |
| `_fadeDuration` | Фейды финального кинематика (default: 1) |
| `_interactText` | Подсказка (default: `"Осмотреть череп"`) |
| `_coinDropClip` / `_jawOpenClip` / `_pourClip` + тома | Звуки |

**Логика фазы 2:**

```
Activate()  ← вызывается из FinalDoorPuzzleInteraction после кинематика фазы 1
  → Interact() (CanInteract: _activated && !_serumPoured && !_pouring)
      → EnterPuzzleMode(_scullCamera, this)   # свой drop-handler
  → Монеты: HandleDrop → рейкаст по глазницам → CoinInsertRoutine
      (анимированная монета опускается сверху → заменяется реальным Coin1/Coin2)
  → Обе глазницы заполнены → OpenJawRoutine:
      пауза 0.3с → звук → Play("ScullOpen") → _jawOpen = true
      → коллайдеры глазниц off, коллайдер приёма сыворотки on → Save
  → SerumColba: HandleDrop → рейкаст по коллайдеру черепа → PourRoutine:
      активировать colbaRead → переворот (SmoothStep) + fillFraction → 0
      → пауза 0.5с → скрыть колбу → FadeIn
      → _serumPoured = true → ExitPuzzleModeInstant → SetSolved() → FadeOut → Save
```

Ghost-превью монет и колбы делаются инстансом префаба с прозрачным URP-материалом (`_Surface`, alpha 0.4). Извлечение монеты из глазницы — по ЛКМ, с проверкой места в инвентаре.

### `EndGameSequence`

Файл: `Assets/Scripts/Puzzle/FinalDoor/EndGameSequence.cs`. Финальный кинематик, стартующий от `FinalDoorPuzzleController.OnSolved` (срабатывает, пока экран ещё чёрный от фейда выливания).

**Inspector (ключевые поля)**

| Поле | Описание |
| ---- | -------- |
| `_controller` | Контроллер — источник события `OnSolved` |
| `_doorAnimator` / `_doorOpenStateName` | Аниматор двери и состояние открытия (default: `"doorOpen"`) |
| `_doorOpenFallbackDuration` | Fallback-ожидание без аниматора в сек (default: 4.5) |
| `_walkTarget` / `_doorCenter` / `_walkDistanceBeyondDoor` | Точка прогулки; без явной цели строится автоматически: центр двери + направление «игрок → дверь», продлённое на 2.5 м за дверь |
| `_playerStartPoint` | Точка снапа игрока (только горизонталь, высота сохраняется) — снап происходит на чёрном экране |
| `_lookPoint` | Точка, на которую заблокирован взгляд до конца финального фейда |
| `_deathPoint` / `_angelPoint` | Статуи для гейнз-хореографии: после открытия двери взгляд плавно переводится на Смерть, затем на Ангела |
| `_gazeStartDelay` / `_gazeHoldTime` / `_gazeArrivalAngle` / `_gazeTimeout` | Параметры гейнза (default: 1 / 1.2 сек / 3° / 8 сек) |
| `_cameraTurnSpeed` / `_aimAcceleration` / `_aimSettleAngle` / `_lookHoldDistance` | Плавность поворота камеры: разгон, торможение у цели, остановка прицела вблизи точки (default: 120°/с, 240°/с², 45°, 1.5 м) |
| `_maxStallTime` | Если игрок «застрял» дольше этого времени — фейд форсируется (default: 2.5 сек) |
| `_walkSpeed` / `_guiltWalkSpeed` / `_guiltPauseDuration` | Скорости ходьбы; финальный подход — медленные «тяжёлые» шаги (default: 0.8 / 0.4 м/с, пауза 2 сек) |
| `_arrivalThreshold` / `_maxWalkDuration` | Порог прихода и страховочный таймаут прогулки (default: 0.2 м / 20 сек) |
| `_portal` | `PortalFunnelController` — активируется вместе с открытием двери (всю игру чёрная дыра держится выключенной) |
| `_startDelay` / `_earlyFadeLead` / `_fadeDuration` | Задержка старта, ранний фейд за N секунд до конца прогулки (default: 5), длительность финального фейда (default: 2.5) |
| `_creditsView` | `EndGameCreditsView` (автопоиск по типу) |
| `_debugLogging` | Отладочные логи |

**Последовательность:**

```
OnSolved
  1. Отключить ввод (игрок и UI), скрыть прицел, заблокировать курсор
     → SetCinematicControl(true), снап игрока к _playerStartPoint, взгляд на _lookPoint
     (всё это на чёрном экране — снап невидим)
  2. Ждать _startDelay — фейд выхода из загадки открывает сцену
  3. _portal.Activate() + Play("doorOpen"), ждать конец анимации
  4. Гейнз-хореография: пауза → взгляд на Смерть → взгляд на Ангела
     → пауза «виноватого» (_guiltPauseDuration)
  5. Параллельно: AimRoutine (взгляд держится на _lookPoint) и
     WalkThroughDoorRoutine (CharacterController.Move, детекция застревания)
     WalkWithEarlyFadeRoutine — фейд начинается за _earlyFadeLead секунд до конца
  6. Дождаться фейда → _creditsView.Show()
```

### `EndGameCreditsView` и `CreditsEntry`

Файлы: `Assets/Scripts/Puzzle/FinalDoor/EndGameCreditsView.cs`, `CreditsEntry.cs`. Полноэкранные титры: Canvas с чёрным фоном и `TextMeshProUGUI`-меткой, живёт на сцене всю игру с alpha 0. Синглтон (`Instance`).

**Inspector**

| Поле | Описание |
| ---- | -------- |
| `_canvasGroup` | CanvasGroup для фейда (автопоиск на объекте) |
| `_fadeInDuration` | Длительность проявления чёрного фона в сек (default: 2) |
| `_entries` | Массив ассетов `CreditsEntry` — строки титров, показываются по одной |
| `_entryHoldDuration` / `_entryFadeDuration` | Время показа и фейда каждой строки (default: 3 / 0.8 сек) |
| `_creditsLabel` | `TextMeshProUGUI` для строк и финального текста |
| `_thanksText` | Финальный текст (default: `"Спасибо за игру"`) |
| `_thanksHoldDuration` | Сколько держится финальный текст (default: 5 сек) |
| `_startSceneName` | Сцена, загружаемая после титров (default: `"Start"`) |
| `_deleteSaveOnFinish` | Стереть дефолтный слот сейва перед выходом (default: `true`) — пройденную игру нельзя загрузить |

`CreditsEntry` — ScriptableObject (`Create → EscapeOf → Credits Entry`): опциональный заголовок `_title` (роль/раздел) и текст `_text`.

**Рутина `Show()`:**

```
FadeIn фона (unscaled time) → по очереди: fade строки → hold → fade out
  → финальный «Спасибо за игру» → hold
  → SaveManager.ResetActiveSlot() + DeleteSave(slot 0)
  → вернуть курсор → уничтожить все DontDestroyOnLoad-синглтоны
    (GameManager, SaveManager, AudioManager, InputManager, ResolutionManager, PopupMessageSystem —
    иначе InputManager держит отключённые action maps и меню некликабельно)
  → Time.timeScale = 1 → SceneManager.LoadScene("Start")
```

---

## Фазы загадки (сводно)

```
[Фаза 1 — медальоны]                     [Фаза 2 — череп]                    [Финал]
Осмотр двери/статуи                      ScullPuzzlePhase.Activate()         SetSolved
  → EnterPuzzleMode(камера статуи)         → осмотр черепа (своя камера)       → EndGameSequence:
  → drag-and-drop 6 медальонов             → 2 монеты в глазницы                 дверь открывается,
  → все на местах → OnPuzzleSolved         → челюсть открывается                 прогулка, взгляды
  → кинематик активации                    → SerumColba → выливание              на Смерть и Ангела,
    (обзор, scullMove)                     → SetSolved()                         титры → сброс сейва
  → MarkPhase1Completed → фаза 2                                              → стартовая сцена
```

---

## Взаимодействие с другими системами

| Система | Связь |
| ------- | ----- |
| Save System | Три независимых `ISaveable`: контроллер (`isSolved`), фаза 1 (`phase1Completed` + медальоны), фаза 2 (`activated`/`jawOpen`/`serumPoured` + монеты) |
| Inventory System | `PuzzleInventoryBar` — источник drag-and-drop; извлечение предметов обратно в инвентарь |
| Химическая загадка (`ChemicalPuzzle`) | Общие типы `ItemData`, `MedallionHole`, `LiquidWobble` (жидкость в колбе) |
| Flashlight System | Фонарь принудительно выключен и заблокирован в puzzle mode |
| `AudioManager` / `ScreenFader` / `InputManager` / `UIManager` / `MusicDirector` | Звуки, фейды, вход Esc, модальное состояние UI |
