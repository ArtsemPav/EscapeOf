## Electric Puzzle System

Загадка с электрическим щитком: игрок сначала вставляет предохранитель, затем соединяет шесть цветных клемм с шестью нейтральными в правильном порядке. При верном соединении лампочка загорается зелёным. Рычаг становится доступен только после вставки предохранителя — при верном решении пазл завершается, при неверном провода мгновенно исчезают и играет искровой эффект.

---

## Компоненты системы

```
ElectricPuzzleController    ← на корневом объекте префаба (IInteractable, ISaveable, IPuzzleDropHandler)
ElectricLever               ← на объекте рычага (pCube17)
ElectricTerminal × 12       ← на каждой клемме в Contacts (6 цветных + 6 нейтральных)
ElectricWire                ← создаётся в рантайме контроллером
ElectricPuzzleData          ← ScriptableObject с решением и цветами проводов
```

---

## Компоненты подробно

### `ElectricPuzzleController`

Размещается на корневом объекте `electric`. Реализует `IInteractable`, `ISaveable` и `IPuzzleDropHandler`.

**Inspector — References**

| Поле | Описание |
|---|---|
| `_panelCamera` | `CinemachineCamera`, которая наводится на щиток |
| `_puzzleData` | `ElectricPuzzleData` — ScriptableObject с решением |
| `_wirePrefab` | Prefab `ElectricWire` — создаётся при каждом соединении |
| `_wrongPullParticles` | `ParticleSystem` искр при неверном нажатии рычага |
| `_solvedObject` | GameObject, активируется при решении (свет, эффект) |
| `_lever` | `ElectricLever` — находится автоматически через `GetComponentInChildren` |
| `_lampLight` | `Light` — лампочка индикатора, находится автоматически |

**Inspector — Fuse**

| Поле | Описание |
|---|---|
| `_acceptedItems` | `ItemData[]` предметов, которые принимает пазл (назначить `Safeguard.asset`) |
| `_fuseAnchorCollider` | `SphereCollider` на `Safeguardanchor` — зона дропа предохранителя |
| `_fuseAnchorTransform` | `Transform` `Safeguardanchor` — точка спавна визуала предохранителя |
| `_fusePrefab` | Prefab, инстанциируемый при вставке предохранителя (например `SafeGuard.prefab`); если не назначен — берётся `item.inspectionPrefab` |

**Inspector — Settings**

| Поле | Описание |
|---|---|
| `_terminalLayer` | LayerMask для Raycast по клеммам, рычагу и `Safeguardanchor` |
| `_lampDefaultColor` | Цвет лампы при вставленном предохранителе и неверных проводах |
| `_lampSolvedColor` | Цвет лампы при верном соединении всех проводов |
| `_blendDuration` | Длительность плавного перехода камеры (секунды) |

**Inspector — Events**

| Поле | Описание |
|---|---|
| `_onPuzzleSolved` | `UnityEvent` — срабатывает однократно при решении |

**Save ID:** `"electric_puzzle"`

**Что сохраняет:** флаг `isSolved`, флаг `wiresCorrect`, массив `connections[i]`, а также состояние предохранителя (`fuseInserted`, `fuseItemId`).

```json
{
  "isSolved": false,
  "wiresCorrect": false,
  "connections": [3, 5, -1, -1, -1, -1],
  "fuseInserted": true,
  "fuseItemId": "safeguard_01"
}
```

---

### `ElectricLever`

Размещается на объекте рычага (`pCube17`). Реализует `IInteractable`.

**Inspector**

| Поле | Описание |
|---|---|
| `_angleOnDelta` | Угол поворота рычага в нажатом положении (градусы) |
| `_rotationSpeed` | Скорость анимации поворота |
| `_pullClip` / `_pullVolume` | Звук нажатия рычага |
| `_interactText` | Текст подсказки при наведении |

**Публичный API**

| Метод / свойство | Описание |
|---|---|
| `SetInteractionEnabled(bool)` | Включает/выключает взаимодействие с рычагом из мирового режима. Вызывается `ElectricPuzzleController` при `Open()`/`Close()` |
| `CanInteract()` | `true` только если `_interactionEnabled && !_isPulled` |
| `Interact()` | Запускает анимацию нажатия |
| `Reset()` | Возвращает рычаг в исходное положение (после неверного нажатия) |
| `SetPulledQuiet()` | Мгновенно ставит рычаг в нажатое положение без анимации — при восстановлении из сохранения |
| `OnPulled` | `Action` — срабатывает только при завершении анимации нажатия (не при возврате) |

> **Рычаг заблокирован вне пазла.** `_interactionEnabled` по умолчанию `false`. До входа в режим пазла игрок не видит подсказку и не может нажать рычаг через мировую камеру.

---

### `ElectricTerminal`

Размещается на каждой из 12 клемм. Хранит состояние подключения, не реализует `IInteractable` — взаимодействие только через Raycast контроллера.

| Поле | Описание |
|---|---|
| `_terminalType` | `Colored` или `Neutral` |
| `_terminalIndex` | Индекс клеммы (0–5 в своей группе) |

---

### `ElectricWire`

Создаётся в рантайме через `_wirePrefab`. Симулирует провод между двумя точками с помощью Verlet-интеграции и рендерит через `LineRenderer`.

**Инициализация:** `Init(from, to, color, wireMaterial, settings)` — задаёт точки крепления, цвет и физические настройки.

Для корректного отображения цвета провод использует Unlit-шейдер. Если `wireMaterial` уже Unlit — делается его копия; иначе создаётся новый материал `Universal Render Pipeline/Unlit`.

---

### `ElectricPuzzleData`

ScriptableObject. Хранит решение и цвета проводов.

`Assets/Data/ElectricPuzzleData.asset`

| Поле | Описание |
|---|---|
| `_solution` | Массив из 6 int: `solution[i]` = индекс нейтральной клеммы для цветной клеммы `i` |
| `_wireColors` | Массив из 6 Color: цвет провода, индекс совпадает с цветной клеммой |

**Текущее решение:**

| Цветная клемма | Цвет | → Нейтральная клемма |
|---|---|---|
| 0 | Красный | 3 |
| 1 | Оранжевый | 5 |
| 2 | Зелёный | 1 |
| 3 | Белый | 4 |
| 4 | Синий | 0 |
| 5 | Тёмный (серый) | 2 |

---

## Иерархия в сцене

```
electric                               ← ElectricPuzzleController, BoxCollider, Interactable Layer
  ElectricCamera                       ← CinemachineCamera (_panelCamera)
  Point Light                          ← Light (_lampLight, находится автоматически)
  pCube17                              ← ElectricLever (_lever, находится автоматически)
  vfx_Sparks_01                        ← ParticleSystem (_wrongPullParticles), неактивен по умолчанию
  Safeguardanchor                      ← SphereCollider, Interactable Layer
  │                                       Назначить в _fuseAnchorCollider и _fuseAnchorTransform
  Contacts
    Terminal_Colored_0..5              ← ElectricTerminal (Type = Colored)
    Terminal_Neutral_0..5              ← ElectricTerminal (Type = Neutral)
  [Wire_0..5]                          ← ElectricWire, создаются в рантайме
```

---

## Как работает полный цикл

```
Старт сессии
  └─ SaveManager.Start() → LoadSaveData()
       └─ ElectricPuzzleController.ApplyPendingLoad()
            ├─ isSolved=true     → SetPulledQuiet(), _solvedObject.SetActive(true)
            ├─ connections[]     → пересоздаёт провода через JointPresettle (без физики)
            └─ fuseInserted=true → SpawnFuseVisual(), якорь отключается, лампа включается

Игрок кликает на щиток
  └─ ElectricPuzzleController.Interact()
       └─ Open()
            ├─ Cinemachine переходит на панельную камеру
            ├─ PuzzleInventoryBar.Instance.Show(this) — бар всегда виден в режиме пазла
            └─ ElectricLever.SetInteractionEnabled(true)

Игрок вставляет предохранитель (если ещё не вставлен)
  Перетащить Safeguard из PuzzleInventoryBar → бросить на Safeguardanchor
  └─ HandleDrop() — Raycast по _terminalLayer, проверка hit.collider == _fuseAnchorCollider
       └─ InsertFuse()
            ├─ _fuseInserted = true, _fuseItemId = item.ItemId
            ├─ SafeGuard.prefab инстанциируется на Safeguardanchor
            ├─ Коллайдер якоря отключается (больше не мешает Raycast)
            ├─ Лампа включается (красная)
            └─ Save()

Игрок тянет провод (только при _fuseInserted = true)
  ЛКМ на цветной клемме  →  StartDrag() — создаёт ElectricWire
  ЛКМ на нейтральной     →  ConnectActiveWire() — крепит провод, EvaluateWires()
  ЛКМ на занятой клемме  →  PickUpWire() / PickUpWireFromNeutral() — переподключение
  ПКМ                    →  CancelActiveDrag() — удаляет незакреплённый провод

EvaluateWires()
  └─ CheckSolution() — сравнивает connections[] с ElectricPuzzleData.Solution
       ├─ верно  → лампа зелёная, Save()
       └─ неверно → лампа остаётся красной

Игрок кликает рычаг (только при _fuseInserted = true)
  ├─ Неверные провода → искры, все провода удаляются мгновенно, рычаг анимируется и возвращается
  └─ Верные провода   → рычаг анимируется вниз
                         OnPulled → HandleLeverPulled() → пазл решён, Close(), Save()

Игрок выходит из пазла (ESC / WASD)
  └─ Close()
       ├─ ElectricLever.SetInteractionEnabled(false)
       ├─ PuzzleInventoryBar.Instance.Hide()
       └─ Cinemachine возвращается к мировой камере
```

---

## Ключевые принципы

**Предохранитель блокирует всё.** Без него лампа выключена (`enabled = false`), `TryInteractLever` возвращает `false`, провода можно двигать — но рычаг не реагирует.

**Рычаг заблокирован вне режима пазла.** `ElectricLever.CanInteract()` проверяет `_interactionEnabled && !_isPulled`. Флаг выставляется в `true` только в `Open()` и сбрасывается в `Close()`.

**Бар всегда виден в режиме пазла.** `PuzzleInventoryBar.Show(this)` вызывается безусловно при каждом `Open()`. Бар отображается даже если предохранитель уже вставлен или инвентарь пуст.

**Сброс проводов мгновенный.** `ResetAllWires()` вызывается в `TryInteractLever` на том же кадре — игрок видит исчезновение проводов немедленно, до завершения анимации рычага.

---

## Настройка с нуля

### 1 — ElectricPuzzleData

Создай ScriptableObject (`Create → Game → Electric Puzzle Data`):
- `_solution` — массив из 6 индексов
- `_wireColors` — массив из 6 цветов (в том же порядке, что и `Terminal_Colored_0..5`)

### 2 — Предохранитель

- Создай `ItemData` для предохранителя (`Create → Inventory → Item Data`): заполни `itemName`, `icon`, `ItemId`
- Добавь в `InventorySystem._allItems`
- В Inspector `ElectricPuzzleController` назначь в `_acceptedItems`
- `_fuseAnchorCollider` / `_fuseAnchorTransform` → `Safeguardanchor`
- `_fusePrefab` → `SafeGuard.prefab` (или оставь пустым, если у предмета заполнен `inspectionPrefab`)

### 3 — Клеммы (ElectricTerminal)

На каждом из 12 дочерних объектов в `Contacts`:
- Добавь `ElectricTerminal` и `Collider`
- Установи `_terminalType` и `_terminalIndex` (0–5 в каждой группе)
- Поставь на слой `_terminalLayer`

### 4 — Рычаг (ElectricLever)

На объекте рычага (`pCube17`) добавь компонент `ElectricLever` и `Collider`. Настрой `_angleOnDelta`. Компонент будет найден автоматически через `GetComponentInChildren`.

### 5 — Лампа и партикл

- Дочерний `Light` — находится автоматически. Настрой `_lampDefaultColor` и `_lampSolvedColor`
- Дочерний `ParticleSystem` → назначь в `_wrongPullParticles`. Деактивируй GameObject по умолчанию

---

## Часто встречающиеся ошибки

**Провода чёрные или не отображают цвет**
- Убедись что `Wire.mat` назначен в `_wirePrefab → ElectricWire._wireMaterial`. Контроллер автоматически переключается на `URP/Unlit`.

**Рычаг не реагирует на клик в режиме пазла**
- Убедись что `Open()` вызывает `_lever?.SetInteractionEnabled(true)`. Проверь что `_lever` найден (`GetComponentInChildren` в `Awake`).

**Рычаг доступен вне режима пазла**
- Убедись что `Close()` вызывает `_lever?.SetInteractionEnabled(false)`.

**Предохранитель не принимается при дропе**
- Проверь что `Safeguardanchor` находится на слое `_terminalLayer` и его `SphereCollider` назначен в `_fuseAnchorCollider`. Radius коллайдера должен перекрывать видимую зону дропа.

**Лампа не загорается после вставки предохранителя**
- Убедись что `_lampLight` найден. Проверь что `InsertFuse()` вызывает `UpdateLamp()`.

**Провода не восстанавливаются после перезагрузки**
- `ApplyPendingLoad()` вызывается в `Start()` — убедись что `SaveManager` зарегистрировал `ElectricPuzzleController` в `Awake()`.

**Партикл виден при старте сцены**
- GameObject `vfx_Sparks_01` должен быть неактивен по умолчанию в prefab.

---

## Компоненты системы

```
ElectricPuzzleController    ← на корневом объекте префаба (IInteractable, ISaveable)
ElectricLever               ← на объекте рычага (pCube17)
ElectricTerminal × 12       ← на каждой клемме в Contacts (6 цветных + 6 нейтральных)
ElectricWire                ← создаётся в рантайме контроллером
ElectricPuzzleData          ← ScriptableObject с решением и цветами проводов
```

---

## Компоненты подробно

### `ElectricPuzzleController`

Размещается на корневом объекте `electric`. Реализует `IInteractable` и `ISaveable`.

**Inspector — References**

| Поле | Описание |
|---|---|
| `_panelCamera` | `CinemachineCamera`, которая наводится на щиток |
| `_puzzleData` | `ElectricPuzzleData` — ScriptableObject с решением |
| `_wirePrefab` | Prefab `ElectricWire` — создаётся при каждом соединении |
| `_wrongPullParticles` | `ParticleSystem` искр при неверном нажатии рычага |
| `_solvedObject` | GameObject, активируется при решении (свет, эффект) |
| `_lever` | `ElectricLever` — находится автоматически через `GetComponentInChildren` |
| `_lampLight` | `Light` — лампочка индикатора, находится автоматически |

**Inspector — Settings**

| Поле | Описание |
|---|---|
| `_terminalLayer` | LayerMask для Raycast по клеммам и рычагу |
| `_lampDefaultColor` | Цвет лампы в обычном состоянии |
| `_lampSolvedColor` | Цвет лампы при верном соединении всех проводов |
| `_blendDuration` | Длительность плавного перехода камеры (секунды) |
| `_sideZoneWidth` | Ширина боковой зоны клика для закрытия панели |

**Inspector — Events**

| Поле | Описание |
|---|---|
| `_onPuzzleSolved` | `UnityEvent` — срабатывает однократно при решении |

**Save ID:** `"electric_puzzle"`

**Что сохраняет:** флаг `isSolved`, флаг `wiresCorrect`, массив `connections[i]` — индекс нейтральной клеммы для цветной клеммы `i`, или `-1` если не подключена.

```json
{ "isSolved": false, "wiresCorrect": false, "connections": [3, 5, -1, -1, -1, -1] }
```

---

### `ElectricLever`

Размещается на объекте рычага (`pCube17`). Управляет анимацией и одноразовым событием.

**Inspector**

| Поле | Описание |
|---|---|
| `_angleOnDelta` | Угол поворота рычага в нажатом положении (градусы) |
| `_animationSpeed` | Скорость анимации поворота |
| `_pullClip` / `_pullVolume` | Звук нажатия рычага |

**Публичный API**

| Метод / свойство | Описание |
|---|---|
| `CanInteract()` | `true` если рычаг ещё не был нажат (`!_isPulled`) |
| `Interact()` | Запускает анимацию нажатия |
| `Reset()` | Возвращает рычаг в исходное положение (после неверного нажатия) |
| `SetPulledQuiet()` | Мгновенно ставит рычаг в нажатое положение без анимации — при восстановлении из сохранения |
| `OnPulled` | `Action` — срабатывает только при завершении анимации нажатия (не при возврате) |

---

### `ElectricTerminal`

Размещается на каждой из 12 клемм. Хранит состояние подключения.

**Inspector**

| Поле | Описание |
|---|---|
| `_terminalType` | `Colored` или `Neutral` |
| `_terminalIndex` | Индекс клеммы (0–5 в своей группе) |

---

### `ElectricWire`

Создаётся в рантайме через `_wirePrefab`. Симулирует провод между двумя точками с помощью Verlet-интеграции и рендерит через `LineRenderer`.

**Инициализация:** `Init(from, to, color, wireMaterial, settings)` — задаёт точки крепления, цвет и физические настройки.

Для корректного отображения цвета провод использует Unlit-шейдер. Если `wireMaterial` уже Unlit — делается его копия; иначе создаётся новый материал `Universal Render Pipeline/Unlit`.

---

### `ElectricPuzzleData`

ScriptableObject. Хранит решение и цвета проводов.

`Assets/Data/ElectricPuzzleData.asset`

| Поле | Описание |
|---|---|
| `_solution` | Массив из 6 int: `solution[i]` = индекс нейтральной клеммы для цветной клеммы `i` |
| `_wireColors` | Массив из 6 Color: цвет провода, индекс совпадает с цветной клеммой |

**Текущее решение:**

| Цветная клемма | Цвет | → Нейтральная клемма |
|---|---|---|
| 0 | Красный | 3 |
| 1 | Оранжевый | 5 |
| 2 | Зелёный | 1 |
| 3 | Белый | 4 |
| 4 | Синий | 0 |
| 5 | Тёмный (серый) | 2 |

---

## Иерархия в сцене

```
electric                               ← ElectricPuzzleController, BoxCollider, Interactable Layer
  ElectricCamera                       ← CinemachineCamera (_panelCamera)
  Point Light                          ← Light (_lampLight, находится автоматически)
  pCube17                              ← ElectricLever (_lever, находится автоматически)
  vfx_Sparks_01                        ← ParticleSystem (_wrongPullParticles), неактивен по умолчанию
  Contacts
    Terminal_Colored_0..5              ← ElectricTerminal (Type = Colored)
    Terminal_Neutral_0..5              ← ElectricTerminal (Type = Neutral)
  [Wire_0..5]                          ← ElectricWire, создаются в рантайме
```

---

## Как работает полный цикл

```
Старт сессии
  └─ SaveManager.Start() → LoadSaveData()
       └─ ElectricPuzzleController.ApplyPendingLoad()
            ├─ isSolved=true  → SetPulledQuiet(), _solvedObject.SetActive(true)
            └─ connections[]  → пересоздаёт провода через JointPresettle (без физики)

Игрок кликает на щиток
  └─ ElectricPuzzleController.Interact()
       └─ Open() — Cinemachine переходит на панельную камеру, вход в режим взаимодействия

Игрок тянет провод
  ЛКМ на цветной клемме  →  StartDrag() — создаёт ElectricWire
  ЛКМ на нейтральной     →  ConnectActiveWire() — крепит провод, EvaluateWires()
  ЛКМ на занятой клемме  →  PickUpWire() / PickUpWireFromNeutral() — переподключение
  ПКМ                    →  CancelActiveDrag() — удаляет незакреплённый провод

EvaluateWires()
  └─ CheckSolution() — сравнивает connections[] с ElectricPuzzleData.Solution
       ├─ верно  → лампа зелёная, Save()
       └─ неверно → лампа обычная

Игрок кликает рычаг
  ├─ Неверные провода → искры, все провода удаляются мгновенно, рычаг анимируется и возвращается
  └─ Верные провода   → рычаг анимируется вниз
                         OnPulled → HandleLeverPulled() → пазл решён, Close(), Save()
```

---

## Настройка с нуля

### 1 — ElectricPuzzleData

Создай ScriptableObject (`Create → Game → Electric Puzzle Data`):
- `_solution` — массив из 6 индексов
- `_wireColors` — массив из 6 цветов (в том же порядке, что и `Terminal_Colored_0..5`)

### 2 — Клеммы (ElectricTerminal)

На каждом из 12 дочерних объектов в `Contacts`:
- Добавь `ElectricTerminal` и `Collider`
- Установи `_terminalType` и `_terminalIndex` (0–5 в каждой группе)
- Поставь на слой, указанный в `_terminalLayer`

### 3 — Рычаг (ElectricLever)

На объекте рычага (`pCube17`) добавь компонент `ElectricLever` и `Collider`. Настрой `_angleOnDelta` и `_animationSpeed` по размеру модели. Компонент будет найден автоматически через `GetComponentInChildren`.

### 4 — Лампа (Light)

Добавь любой дочерний `Light`-объект — контроллер найдёт его автоматически через `GetComponentInChildren`. Настрой `_lampDefaultColor` и `_lampSolvedColor` в Inspector контроллера.

### 5 — Партикл (vfx_Sparks_01)

Добавь дочерний `ParticleSystem` и назначь его в поле `_wrongPullParticles`. Деактивируй GameObject по умолчанию — контроллер активирует его сам при неверном нажатии и скрывает по завершении воспроизведения.

### 6 — ElectricPuzzleController

На корневом объекте:
- `_panelCamera` — CinemachineCamera щитка
- `_puzzleData` — созданный ElectricPuzzleData
- `_wirePrefab` — prefab с компонентом ElectricWire
- `_terminalLayer` — LayerMask клемм и рычага

---

## Часто встречающиеся ошибки

**Провода чёрные или не отображают цвет**
- Материал `Wire.mat` использует Lit-шейдер с высоким металликом. Контроллер автоматически переключается на `URP/Unlit` — убедись что `Wire.mat` назначен в `_wirePrefab → ElectricWire._wireMaterial`.

**Рычаг не реагирует на клик**
- `ElectricLever` не попадает под Raycast. Проверь, что объект рычага находится на слое `_terminalLayer` и имеет `Collider`.

**После неверного нажатия рычаг остаётся в нажатом положении**
- `HandleLeverPulled()` должен вызывать `_lever?.Reset()` при `!_wiresCorrect`. Убедись что `_lever` найден (`GetComponentInChildren` в `Awake`).

**Провода не восстанавливаются после перезагрузки**
- `ApplyPendingLoad()` вызывается в `Start()` — убедись что `SaveManager` зарегистрировал `ElectricPuzzleController` в `Awake()`.

**Пазл не помечается решённым после перезагрузки**
- `_solvedObject` не назначен, или его `SetActive(true)` не вызывается в `RefreshVisuals()` при `_isSolved=true`.

**Партикл виден при старте сцены**
- GameObject `vfx_Sparks_01` должен быть неактивен по умолчанию. `Awake()` контроллера принудительно вызывает `SetActive(false)` как страховку — но лучше выставить это в prefab через Override.
