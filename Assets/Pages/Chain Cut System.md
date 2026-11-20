<!-- version: 5 -->

## Обзор

Цепь на двойной двери Tambur (`PRF_Tambur`): до среза — статичная картинка (меши звеньев без физики), в момент среза включается физика — цепь срывается с ручек, падает на пол и застывает. Решается загадкой «кускачки»: игрок входит в режим загадки (автоматически, по триггеру `chainenter`), перетаскивает кусачки из `PuzzleInventoryBar` на цепь — кусачки подлетают, перекусывают цепь (`Take 001`), цепь падает и двери отпираются.

## Компоненты

### `ChainCutPuzzleController`

Файл: `Assets/Scripts/Puzzle/ChainCut/ChainCutPuzzleController.cs`. Висит на `/chain` рядом с `PuzzleModeController`. Реализует `IPuzzleDropHandler`, `IPuzzleDropTarget`, `IPuzzleExitGuard`, `ISaveable` (`_saveId = "chain_cut_puzzle_tambur"`).


| Поле                 | Описание                                                                                                                             |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `Save ID`            | Уникальный ID сейва (default `chain_cut_puzzle_tambur`)                                                                              |
| `Required Item`      | `ItemData` кусачков (`DataChainCutter`)                                                                                              |
| `Chain Collider`     | Collider цепи — цель рейкаста дропа (BoxCollider на `/chain`)                                                                        |
| `Chain`              | `ChainCuttable` (авто-резолв в детях)                                                                                                |
| `Cutter Animator`    | Animator кусачков (`ACChainCut.controller`, стейт `Cut` по триггеру `CutChain`)                                                      |
| `Cutter Start Point` | Точка старта подлёта (`/chain/CutterStartPoint`)                                                                                     |
| `Chain Cut Point`    | Точка перекусывания (`/chain/ChainCutPoint`)                                                                                         |
| `Ghost Prefab`       | `Prefabs/chainCutter/bolt_cutter.prefab` + материал `ChainCutter_Ghost.mat` (альфа 0.15, runtime приводится к прозрачному блендингу) |
| `Fly Duration`       | Длительность подлёта кусачек, сек (1)                                                                                                |
| `Cut Impact Delay`   | Задержка от смыкания челюстей до включения физики, сек (0.2)                                                                         |
| `Cut Complete Clip`  | Звук завершения среза                                                                                                                |


### `PuzzleModeTrigger`

Файл: `Assets/Scripts/Puzzle/Shared/PuzzleModeTrigger.cs`. Висит на `/chain/chainCut` (триггер-коллайдер): вход игрока в объём → `PuzzleModeController.EnterPuzzleMode()` без нажатия E. Поведение:

- На время режима загадки свой триггер-коллайдер **выключается** (не срабатывает повторно и не мешает).
- Вышел без решения (Esc) — коллайдер **включается** обратно, игрок может зайти снова.
- Решил — остаётся выключенным навсегда; то же при загрузке сейва с решённой загадкой (`OnEnable` проверяет `IsSolved`).

Вход по E — отдельно: `/chain/chainEnter` (BoxCollider без триггера, слой Interactable Layer + `PuzzleInteractable` → контроллер на `/chain`).

### `ChainCuttable`

Файл: `Assets/Scripts/Interaction/ChainCuttable.cs`. Размещается на корневом узле `/chain` (префаб `PRF_Tambur`). Звенья лежат в дочернем узле `chain` (`Chain0…Chain23`, инстансы префабов `Chain1`/`Chain2`/`Chain3`).

### `ChainLinkFloorProbe`

Файл: `Assets/Scripts/Interaction/ChainLinkFloorProbe.cs`. Вешается на каждое звено автоматически в момент среза; отмечает первое касание пола.

---

## Как работает загадка

```
Игрок заходит в триггер chainCut (или нажимает E на chainEnter)
  → PuzzleModeTrigger / PuzzleInteractable → PuzzleModeController.EnterPuzzleMode()
  → триггер chainCut выключается на время загадки
  → камера ChainCamera, PuzzleInventoryBar с кусачками

Игрок тащит кусачки над цепью
  → ghost-превью (bolt_cutter.prefab, материал ChainCutter_Ghost; поза/масштаб берутся с выставленного в иерархии bolt_cutter)
  → отпускает на коллайдере /chain → HandleDrop() → CutSequence()

CutSequence:
  → bolt_cutter показывается, твином подлетает к ChainCutPoint (Fly Duration)
  → Animator.SetTrigger("CutChain") — стейт Cut, Take 001 (2.25 с)
  → через Cut Impact Delay — ChainCuttable.CutChain() (физика, падение)
  → ждём OnCutSettled (цепь улеглась и заморожена)
  → CompletionSequence: кусачки расходуются (RemoveItem), Save, SetSolved()

OnPuzzleSolved (UnityEvent):
  → DoorInteraction.UnlockAndOpen() на створках doors_wide (тамбур)
  → DoorInteraction.Unlock() на двери морга (Morgue/Walls/doors_A/Doormesh) — дверь сама не открывается, только снимается замок
```

### Восстановление после загрузки

При загрузке сейва с решённой загадкой падение НЕ повторяется: `ChainCutPuzzleController.RestoreSolvedState` вызывает `ChainCuttable.SetCutRestored()` — та прячет висящую цепь (`/chain/chain`) и показывает узел-двойник `/chainRestored` на корне префаба (лежащая на полу поза, физики нет — просто меши). Узел-двойник запечён в `PRF_Tambur.prefab` (inactive, 22 звена `Chain1…Chain23`, без срезанного `Chain0`). Скрипт ищет двойника через `transform.parent.Find` — то есть строго на корне префаба, не в под-узлах.

## Запекание лежачей позы (`chainRestored`)

Уже запечено в `PRF_Tambur.prefab`. Процедура для повторного запекания (если цепь перенастроена):

1. Войти в Play Mode у двери, дать цепи срезаться и улечься (или `[ContextMenu] Test Cut Chain`).
2. Снять мировые трансформы всех звеньев `chain/Chain*` (срезанного `Chain0` уже нет).
3. Выйти из Prefab Mode (если открыт — его буфер затирает записи!), пересчитать позы в локальные координаты префаба и создать узел `/chainRestored` на корне.
4. Узел `chainRestored` — inactive по умолчанию; проверить сохранение в файле префаба на диске.

## Demo-режим

Отключён (`Demo Door Tug Break = false`). Остался в `ChainCuttable` для отладки.

## Структура цепи

```
/PRF_Tambur/chain            ← ChainCuttable (демо off) + PuzzleModeController + ChainCutPuzzleController + BoxCollider (цель рейкаста)
├── chain                    ← узел со звеньями (висящая поза)
│   ├── Chain0 … Chain23     ← MeshRenderer'ы, инстансы Chain1/2/3
├── bolt_cutter              ← модель кусачков + Animator (ACChainCut), inactive
├── ChainCutPoint            ← точка перекусывания
├── CutterStartPoint         ← точка старта подлёта
├── chainEnter               ← вход по E: BoxCollider (не триггер) + PuzzleInteractable, слой Interactable Layer
├── chainCut                 ← авто-вход: BoxCollider (триггер) + PuzzleModeTrigger
└── ChainCamera              ← CinemachineCamera загадки

/PRF_Tambur/chainRestored    ← лежащая на полу поза (запечён, inactive, 22 звена)
```

- `Chain0` — точка среза (скрывается при срезе).
- **Read/Write на `Assets/Models/chain/chain.fbx` ОБЯЗАТЕЛЕН (Read/Write = вкл на импортере модели).** Без него билд падает: колайдеры звеньев пустые, цепь падает «палкой» без физики звеньев. В редакторе это не проявляется.
- Коллайдеры ручек — `BoxCollider` на створках двери (`doors_wide_wing_L/R`).
- Двери тамбура заперты (`_isLocked = true`), отпираются `UnlockAndOpen` при решении.
- Дверь морга (`Env/Basement/Morgue/Walls/doors_A/Doormesh`) заперта; при решении привязка из сцены `Game.unity` (override на инстансе `Tambur`, источник `PRF_Env.prefab`): `OnPuzzleSolved → Unlock()`, `OnPuzzleSolvedRestored → Unlock()`. Дверь не распахивается — игрок открывает сам.

## Как работает срез (`CutChain()`)

1. Звено-точка среза (`Cut Link Name` = `Chain0`) скрывается.
2. На остальных звеньях включаются `Rigidbody` + `MeshCollider` (convex).
3. Соседние звенья связываются `ConfigurableJoint`:
  - линейные оси **залочены** — жёсткая дистанция между звеньями (линейные лимиты не используются: `ConfigurableJoint` масштабирует их скейлом тела, у звеньев scale 3);
  - вращение полностью свободное (шаровой сустав);
  - `projectionMode = PositionAndRotation` — мгновенный возврат звена на стык при дрейфе;
  - `breakForce/breakTorque = ∞`, `enablePreprocessing = false`.
4. Коллизии между **всеми парами звеньев** игнорируются (`Physics.IgnoreCollision`) — переплетённые convex-халлы иначе взрывообразно расталкивают цепь.
5. На звенья назначается физматериал **без трения** (`bounciness 0.15`, `frictionCombine = Minimum`) — нижняя петля силой тяжести стягивает цепь с ручек, звенья соскальзывают, а не висят.
6. Стартовый импульс: всем звеньям скорость вниз (`Initial Drop Speed` = 1.5 м/с) + лёгкий случайный импульс первому звену.
7. Звук `chainCut`; звук `chainInteract` играет при рывке двери (демо-режим).
8. Через `Drop Sound Delay` (0.5 с) — звук `chainDrop`, затем ожидание: **все звенья должны коснуться пола** (`ChainLinkFloorProbe` фиксирует контакт с нормалью вверх `≥ 0.6`; таймаут `Settle Timeout` = 2 с) → цепь замораживается (`isKinematic = true`).

## Демо-режим: «дёрганул дверь → цепь порвалась»

Легко отключаемый блок для тестов:

- `Demo Door Tug Break` (выкл) — главный выключатель демо-режима (логика осталась для отладки).
- `Demo Door` — отслеживаемая дверь; если пуста, скрипт сам находит все `DoorInteraction` у ближайшего общего предка с цепью (дверь — сосед цепи по комнате, не родитель).
- Логика: `Update()` опрашивает двери (`IsFullyClosed`); как только любую из них начали открывать — звук рывка (`chainInteract`), пауза `Demo Break Delay` (0.05 с), затем `CutChain()`.

## Inspector — ChainCuttable


| Поле                                                   | Дефолт                                     | Описание                                                                                         |
| ------------------------------------------------------ | ------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| `Cut Link Name`                                        | `Chain0`                                   | Звено-точка среза (скрывается)                                                                   |
| `Cut Impulse`                                          | 0.3                                        | Случайный импульс первому звену                                                                  |
| `Link Mass`                                            | 0.2                                        | Масса звена                                                                                      |
| `Solver Iterations` / `Solver Velocity Iterations`     | 30 / 15                                    | Стабильность суставов                                                                            |
| `Max Depenetration Velocity`                           | 0.5                                        | Скорость выталкивания из коллизий — против «разрыва и сборки»                                    |
| `Projection Distance` / `Projection Angle`             | 0.02 / 30°                                 | Порог мгновенного возврата звена на стык                                                         |
| `Initial Drop Speed`                                   | 1.5                                        | Стартовая скорость вниз — срыв с ручек                                                           |
| `Link Order`                                           | `Hierarchy`                                | Порядок суставов: как расставлено в иерархии (Spatial — жадный обход, если звенья не по порядку) |
| `Drop Sound Delay`                                     | 0.5                                        | Момент звука удара о пол                                                                         |
| `Settle Timeout`                                       | 2                                          | Макс. ожидание касания пола всеми звеньями                                                       |
| `Demo Door Tug Break`                                  | выкл                                       | Демо: дверь начали открывать → разрыв (отключено)                                                |
| `Demo Door` / `Demo Break Delay`                       | — / 0.05                                   | Демо-дверь и пауза-рывок                                                                         |
| `Door Tug Clip` / `Chain Cut Clip` / `Chain Drop Clip` | `chainInteract` / `chainCut` / `chainDrop` | Звуки (в `Assets/sound/chain/`)                                                                  |
| `Demo Volume`                                          | 0.8                                        | Громкость демо-звуков                                                                            |


Ручной срез без демо: `[ContextMenu] Test Cut Chain` на компоненте.

## История фиксов физики (для будущих задач с суставами)

- «Разрыв и сборка звеньев» → `maxDepenetrationVelocity` + итерации солвера + игнор коллизий всех пар звеньев.
- «Растягивание» → `CharacterJoint` заменён на `ConfigurableJoint` с проекцией.
- «Распад на сегменты» → суставы по порядку иерархии (смежность совпадает с порядком расстановки); пространственная сортировка опциональна (`Link Order`).
- «Виснет на ручках» → физматериал без трения + стартовая скорость вниз.
- «Ползает по полу» → заморозка по факту касания пола всеми звеньями.
- «В билде падает без физики звеньев (палкой)» → Read/Write на `chain.fbx`. convex `MeshCollider` печётся в рантайме и требует CPU-доступа к мешу; в билде без Read/Write лог пестрит `CollisionMeshData couldn't be created because the mesh has been marked as non-accessible`, коллайдеры звеньев пустые. В редакторе не воспроизводится.