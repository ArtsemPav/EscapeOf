## Обзор

Три механики «потянуть»:

1. **Физическое перетаскивание объектов** — `PhysicsDraggable` + `PhysicsGrabber` (пружинная физика, вес, drag/impact-звуки).
2. **Скриптовый drag ящиков и дверей** — `DrawerDrag` и `DoorInteraction` (реализуют `IDraggable`), маршрутизация через `FPSController` и интерфейсы `IDraggable`/`IInteractable`.
3. **Аниматорные двери** — `DoorAnimator` (только визуальная анимация).

```
FPSController
├── PhysicsGrabber.Update()          — SphereCast, ищет PhysicsDraggable (слой Draggable)
├── HandleInteractionDetection()     — raycast, ищет IDraggable + IInteractable
└── HandleDragInteraction()          — маршрутизирует дельту мыши в OnDrag()
        │
        ├── PhysicsDraggable         — физический проп (пружина)
        ├── DrawerDrag               — ящик (трансляция)
        └── DoorInteraction          — дверь (вращение)
```

---

## Физическое перетаскивание — `PhysicsDraggable` + `PhysicsGrabber`

**Скрипты:** `Assets/Scripts/Interaction/PhysicsDraggable.cs`, `Assets/Scripts/Player/PhysicsGrabber.cs`

Игрок удерживает ЛКМ и тянет объект — работает пружина-демпфер (F = k·dist − d·velocity, `ForceMode.Force` — тяжёлые тела ускоряются медленнее).

### Настройка объекта

1. Компонент `PhysicsDraggable` (`RequireComponent(Rigidbody)`)
2. Слой **Draggable** — иначе SphereCast не попадёт
3. `Static` флаг — **снять**
4. Для `MeshCollider` — включить **Convex**

### Параметры `PhysicsDraggable`

**Drag Behavior**


| Поле                | По умолчанию | Описание                                                                                           |
| ------------------- | ------------ | -------------------------------------------------------------------------------------------------- |
| `Drag Hint`         | `Тянуть`     | Текст подсказки при наведении                                                                      |
| `Prevent Tipping`   | `false`      | Запрещает опрокидывание: при захвате замораживаются ограничения вращения X/Z (`FreezeRotationX/Z`) |
| `Max Drag Distance` | `0`          | Персональная макс. дистанция захвата (0 = глобальная; может только сокращать её)                   |
| `Hold Distance`     | `0`          | Персональная дистанция удержания (0 = глобальная; используется меньшая из двух)                    |


**Drag Audio** — scrape-луп во время скольжения по поверхности: `_scrapeClips` (случайный клип на сессию), `_scrapeVolume 0.7`, `_scrapeFullVolumeSpeed 3`, `_scrapeFadeSpeed 6`, `_scrapeStartSpeed 0.05` (мин. тангенциальная скорость контакта для старта). Громкость и pitch (0.85–1.15) зависят от скорости скольжения.

**Impact Audio** — one-shot при столкновениях (в т.ч. при опускании): `_impactClips`, `_impactVolumeAtMin 0.15` / `_impactVolumeAtMax 1`, `_impactMaxSpeed 5`, `_impactMinSpeed 0.5`, `_impactCooldown 0.1` с, `_impactPitchVariation 0.05` (громче и ниже — для сильных ударов).

**Fall Safety** — страховка от провала сквозь пол: `_fallReturnDepth 10` (глубже стартовой позиции на столько метров → телепорт назад), `_fallReturnLift 0.5` (подъём над последней «заземлённой» позицией). Позиция последнего контакта с полом (нормаль Y ≥ 0.6) запоминается из коллизий.

### CCD pinning

CCD дорог — не включается на все ~80 пропов сразу:

1. Объекты, закреплённые в Inspector (например, консервная банка) — всегда.
2. Объект, который игрок тянет сейчас (`OnGrabBegin` → `CollisionDetectionMode.Continuous`).
3. Последняя перенесённая вещь (`PinCcdAfterCarry` — пин переживает отпускание).

`UnpinCcd()` возвращает inspector-режим, когда игрок берёт следующий объект. Плюс fall-safety как последняя сеть против туннелирования.

### Параметры `PhysicsGrabber` (на Player)


| Поле                     | По умолчанию | Описание                                                   |
| ------------------------ | ------------ | ---------------------------------------------------------- |
| `Camera Transform`       | —            | Ссылка на камеру персонажа                                 |
| `Grab Distance`          | `3`          | Дистанция захвата (м)                                      |
| `Detection Radius`       | `0.08`       | Радиус SphereCast                                          |
| `Draggable Layer`        | —            | Маска слоя Draggable                                       |
| `Hold Distance`          | `2`          | Расстояние удержания перед камерой (м)                     |
| `Spring Strength`        | `200`        | Жёсткость пружины                                          |
| `Spring Damping`         | `20`         | Затухание (больше — меньше раскачки)                       |
| `Angular Damping Factor` | `8`          | Гашение угловой скорости при удержании (против кувыркания) |
| `Max Velocity`           | `8`          | Ограничение скорости объекта (м/с)                         |
| `Grab Linear Drag`       | `5`          | Drag во время удержания (восстанавливается на релизе)      |
| `Reference Heavy Mass`   | `20`         | Масса, при которой скорость = `Min Speed Multiplier`       |
| `Min Speed Multiplier`   | `0.4`        | Минимальный множитель скорости игрока с тяжёлым объектом   |
| `Acceptable Gap`         | `0.6`        | Зазор, при котором нет доп. замедления                     |
| `Max Gap`                | `1.5`        | Зазор, при котором игрок останавливается полностью         |


Скорость игрока: `massMultiplier × gapFactor`; massMultiplier фиксируется при захвате (`Lerp(1.0, minSpeedMultiplier, mass/referenceHeavyMass)`), gapFactor динамический. При отпускании — мгновенно 1.0.

Дополнительно: проверка линии видимости (тонкий ray до объекта — сквозь стены не схватить); захват запрещён при беге (начал бежать — объект отпускается) и при свободном курсоре (`Cursor.lockState == None`).

---

## Raycast-логика обнаружения — критически важно

`HandleInteractionDetection()` (FPSController) использует **два последовательных рейкаста**.

### Луч 1 — поиск интерактивного объекта

```csharp
Physics.Raycast(ray, out RaycastHit interactHit, interactDistance,
                interactableLayer, QueryTriggerInteraction.Ignore)
```

Ищет только на слое **Interactable Layer**. Если ничего не найдено — подсказка гасится.

### Луч 2 — проверка препятствий

```csharp
int obstacleMask = ~interactableLayer.value & ~(1 << 2); // все кроме Interactable Layer и IgnoreRaycast
Physics.Raycast(ray, out RaycastHit _, interactHit.distance, obstacleMask, ...)
```

Проверяет, есть ли **между камерой и найденным объектом** геометрия на любом другом слое (стены, полки, закрытые шкафы — они должны быть на **Default**). Если что-то стоит на пути — взаимодействие блокируется.

### Разрешение компонента


| Приоритет | Откуда берётся                                         | Зачем                                     |
| --------- | ------------------------------------------------------ | ----------------------------------------- |
| 1         | `GetComponent<IDraggable>()` на хитнутом объекте       | Ящики и двери — коллайдер прямо на них    |
| 2         | `TryGetComponent<IInteractable>()` на хитнутом объекте | `PickableItem`, кодовые замки и т. д.     |
| 3         | `GetComponentInParent<IInteractable>()`                | Рычаги и манометры с дочерним коллайдером |


**Критически важно:** `IDraggable` ищется **только на прямом объекте**, без `GetComponentInParent`. Иначе `DrawerDrag` на ящике будет перехватывать взаимодействие с предметами внутри ящика.

---

## Требования к слоям


| Объект                                 | Слой                   | Причина                                |
| -------------------------------------- | ---------------------- | -------------------------------------- |
| Ящики, двери, кнопки, рычаги, предметы | **Interactable Layer** | Луч 1 должен их видеть                 |
| Пропы для физического drag'а           | **Draggable**          | SphereCast PhysicsGrabber'а            |
| Корпуса мебели, стены, полки           | **Default**            | Луч 2 должен их видеть как препятствия |
| Пустые триггеры, зоны                  | **Ignore Raycast**     | Оба луча их игнорируют                 |


---

## Интерфейсы

### `IDraggable`

```csharp
void OnDragStart(Vector3 hitPoint, Camera cam); // ЛКМ нажата: точка попадания + камера для проекций
void OnDrag(Vector2 mouseDelta);                // каждый кадр пока ЛКМ зажата
void OnDragEnd();                               // ЛКМ отпущена
```

### `IInteractable`

```csharp
string GetInteractText();         // текст подсказки при прицеливании
bool   IsPickable();              // можно ли поднять объект
CrosshairMode GetCrosshairMode(); // иконка прицела
string GetBlockedHint();          // подсказка при блокировке взаимодействия (опционально)
```

---

## DrawerDrag — выдвижной ящик

**Скрипт:** `Assets/Scripts/Interaction/DrawerDrag.cs`

### Принцип работы

Позиция ящика описывается числом `_openAmount` от `0.0` (закрыт) до `1.0` (открыт). Каждый кадр это число переводится в `localPosition` (позиция ящика **относительно родителя**):

```csharp
// ApplyPosition()
Vector3 openLocalPos = _closedLocalPosition + _openDirection.normalized * _openDistance;
transform.localPosition = Vector3.Lerp(_closedLocalPosition, openLocalPos, _openAmount);
```

Закрытая позиция захватывается автоматически из `localPosition` при `Start` (`EnsureClosedPosition`).

### OnDragStart — вычисление направления

Мировые координаты закрытой и открытой позиций вычисляются через `parent.TransformPoint` (корректно для нестандартных поворотов FBX-импорта) и проецируются на экран. Разница — вектор `_screenOpenDir` в пикселях.

```
closedWorld ──────────────────► openWorld
      │                               │
  cam.WorldToScreenPoint         cam.WorldToScreenPoint
      │                               │
  screenA  ────── screenDelta ──► screenB
                        │
                _screenOpenDir = нормализован
                _computedSensitivity = dragSensitivity / screenLength
```

`_computedSensitivity` обеспечивает **1:1 трекинг**: если ящик занимает 200 пикселей на экране, надо сдвинуть мышь на 200 px, чтобы полностью его открыть (при `_dragSensitivity = 1`).

Если ящик смотрит прямо в камеру (длина проекции < 1 px) — используется запасной вектор `Vector2.right` с фиксированной чувствительностью `_dragSensitivity * 0.003`.

Аниматор родительского объекта отключается на время ручного управления. Звук: `_openClip` при старте тяги; при смене направления тяги (> 0.5 по входу) звук перезапускается с кулдауном 0.4 с.

### OnDrag — каждый кадр

```csharp
float input = Vector2.Dot(mouseDelta, _screenOpenDir);
if (_invertAxis) input = -input;
float maxAmount = _isLockedDrag ? _lockedJiggleAmount : 1f;
_openAmount = Mathf.Clamp(_openAmount + input * _computedSensitivity, 0f, maxAmount);
```

Dot-произведение извлекает только составляющую движения мыши вдоль направления открытия.

### OnDragEnd — снап

```csharp
if (_snapOnRelease)
    _targetOpenAmount = _openAmount >= _snapThreshold ? 1f : 0f;
else
    _targetOpenAmount = _openAmount; // остаётся как оставил игрок
```

После отпускания ЛКМ ящик досылается через `Mathf.Lerp` в `Update()` со скоростью `_snapSpeed`.

### Запертый ящик

При `_isLocked` тяга дёргает ящик только до `_lockedJiggleAmount` (0.05), играет `_lockedClip`, подсказка — `_lockedHint` («Заперто»), иконка прицела `CrosshairMode.Locked`. После отпускания плавно возвращается (`_lockedSnapBackSpeed 10`).

### Публичный API


| Метод                      | Описание                                                        |
| -------------------------- | --------------------------------------------------------------- |
| `Unlock()` / `Lock()`      | Программная разблокировка/блокировка (`_unlockClip` при Unlock) |
| `IsLocked`                 | Заперт ли ящик                                                  |
| `AutoOpen()`               | Разблокировать и плавно открыть (скорость `1/_snapSpeed`)       |
| `AutoOpen(float duration)` | То же за заданное время (для катсцен)                           |
| `SnapOpen()`               | Мгновенно открыть без анимации (для восстановления из сейва)    |
| `GetBlockedHint()`         | `_lockedHint` для запертого, иначе пустая строка                |


### Параметры инспектора


| Поле                          | По умолчанию | Описание                                            |
| ----------------------------- | ------------ | --------------------------------------------------- |
| `_openDirection`              | `(0, 0, 1)`  | Локальная ось выдвижения                            |
| `_openDistance`               | `0.4`        | Дистанция выдвижения (м)                            |
| `_dragSensitivity`            | `1`          | Множитель чувствительности (1 = курсор 1:1)         |
| `_invertAxis`                 | `false`      | Инвертировать ось                                   |
| `_snapOnRelease`              | `true`       | Снап к 0/1 на релизе; false — остаётся где оставили |
| `_snapSpeed`                  | `8`          | Скорость досыла                                     |
| `_snapThreshold`              | `0.5`        | Порог срабатывания снапа                            |
| `_isLocked`                   | `false`      | Заперт ли ящик                                      |
| `_lockedHint`                 | `Заперто`    | Подсказка запертого ящика                           |
| `_lockedJiggleAmount`         | `0.05`       | Насколько поддаётся (0–0.15)                        |
| `_lockedSnapBackSpeed`        | `10`         | Скорость возврата после рывка                       |
| `_lockedClip` / `_unlockClip` | —            | Звуки запертого ящика и разблокировки               |
| `_openClip` / `_openVolume`   | — / `0.8`    | Звук движения ящика                                 |
| `_interactText`               | `Потянуть`   | Подсказка при наведении                             |


---

## DoorInteraction — дверь

**Скрипт:** `Assets/Scripts/Interaction/DoorInteraction.cs` (namespace `Escape.Core`)
Реализует `IInteractable`, `IDraggable`, `ISaveable` (isOpen / isLocked / openFraction / wasUnlocked).

### Режимы открытия (`DoorOpenMode`)

- **Drag** — перетаскивание мышью с инерцией.
- **Click** — одиночный клик ЛКМ плавно открывает/закрывает дверь за `_openDuration` (1 с).

Состояние двери — `_openFraction` от 0 (закрыта) до 1 (открыта). Ось вращения настраивается (`DoorRotationAxis`: X/Y/Z, по умолчанию Y), пивот задаётся полем `_pivot`:

```csharp
// ApplyAngle()
e.y = _closedLocalEulerY + _openFraction * _maxOpenAngle;  // (для оси Y)
pivot.localEulerAngles = e;
```

### OnDragStart — захват точки

```csharp
Vector3 offset = hitPoint - _pivot.position;
offset = FlattenOffsetForAxis(offset); // обнуляет компонент вдоль оси вращения
float currentOpenAngle = _dragStartFraction * _maxOpenAngle;
_grabOffsetWorld = Quaternion.AngleAxis(-currentOpenAngle, axisWorld) * offset;
```

`_grabOffsetWorld` хранится в **мировых координатах в системе закрытой двери** (pivot = 0°). Если бы offset хранился в текущем положении, `OnDrag` вращал бы его дважды → инверсия направления при открытой двери. Мир (а не transform меша) — из-за отрицательных localScale при FBX-импорте.

Запертая и закрытая дверь не двигается — только звук `_lockedClip` и лог. Анимация `UnlockAndOpen` приоткрывает дверь до `_unlockAjarFraction` со скоростью `_unlockAjarSpeed` (0.6 fraction/сек) — новые драги игнорируются до её окончания.

### OnDrag — вычисление delta

**Шаг 1 — текущая мировая позиция точки захвата:**

```csharp
float   openedAngle = _openFraction * _maxOpenAngle;
Vector3 grabWorld   = Quaternion.AngleAxis(openedAngle, axisWorld) * _grabOffsetWorld;
```

**Шаг 2 — аналитический тангент дуги вращения:**

```csharp
Vector3 swingTangent = Vector3.Cross(axisWorld, grabWorld / grabDist)
                       * Mathf.Sign(_maxOpenAngle);
```

`cross(axis, grabDir)` — направление движения точки захвата при открытии; `sign(maxOpenAngle)` корректирует знак для дверей с отрицательным углом.

**Шаг 3 — проекция на экран:**

```csharp
Vector2 screenGrab  = cam.WorldToScreenPoint(grabWorldPos);
Vector2 screenAhead = cam.WorldToScreenPoint(grabWorldPos + swingTangent * 0.5f);
Vector2 openDir     = screenAhead - screenGrab;   // куда на экране двигает дверь
```

**Шаг 4 — deltaFraction:**

```csharp
float input = Vector2.Dot(mouseDelta, openDir / openDirMag);
float deltaFraction = Mathf.Clamp(
    input * _dragSensitivity / Mathf.Max(Screen.height * 0.5f, 1f),
    -MaxDeltaFractionPerFrame, MaxDeltaFractionPerFrame); // ±0.15 за кадр
```

Чувствительность нормирована на **половину высоты экрана**: `_dragSensitivity 1.0` = полный ход двери за пол-экрана мыши, независимо от дистанции камеры, угла двери и DPI мыши.

### Инерция и fling

- Скорость (`_velocity`, fraction/сек) вычисляется каждый кадр, клампится в `±_maxVelocity` (1.2).
- После отпускания дверь движется по инерции с затуханием `_friction` (5/сек).
- **Fling:** при резком отпускании (`|velocity| >= _flingThreshold 0.9`) дверь досылается до конца (`_flingTarget 1/0`) со скоростью `_flingSpeed 2.0` fraction/сек. Микродвижения (< `MinDragFraction 0.04` и скорости < 0.08) ничего не делают.
- Итог: `_isOpen = _openFraction > 0.5`, вызывается `Save()`.

### Звук


| Клип             | Когда играет                                                      |
| ---------------- | ----------------------------------------------------------------- |
| `_openLoopClip`  | Луп, пока дверь движется в сторону открытия (`_motionVolume 0.7`) |
| `_closeLoopClip` | Луп, пока движется в сторону закрытия                             |
| `_latchClip`     | Удар при достижении полностью закрытого положения                 |
| `_lockedClip`    | Подёргивание запертой двери                                       |
| `_unlockClip`    | Щелчок замка при `UnlockAndOpen` (вводе правильного кода)         |


### Occlusion и события

- `_occlusionPortal` — синхронизирует `OcclusionPortal.open` с открытостью двери (baked occlusion culling не прячет объекты за открытой дверью).
- UnityEvents: `_onDoorClosed` (полностью закрыта), `_onLocked`, `_onUnlocked` (при смене `_isLocked`).

### Публичный API


| Метод                                                | Описание                                                         |
| ---------------------------------------------------- | ---------------------------------------------------------------- |
| `Unlock()` / `Lock()`                                | Смена блокировки без движения + события                          |
| `IsOpen` / `IsLocked` / `IsFullyClosed`              | Состояния (fully closed = fraction ≈ 0)                          |
| `MaxOpenAngle`                                       | Абсолютный угол открытия                                         |
| `UnlockAndOpen()`                                    | Разблокировать и плавно приоткрыть (wire на CodeLock.OnUnlocked) |
| `SetUnlockAjarFraction(f)` / `SetUnlockAjarSpeed(s)` | Настройка приоткрытия из UnityEvents                             |
| `Open()` / `Close()`                                 | Полное открытие/закрытие (wire на рычаги)                        |
| `CloseAndLock()`                                     | Закрыть и запереть                                               |
| `GetInteractText()`                                  | `_closeText`/`_openText` («Открыть дверь»/«Закрыть дверь»)       |
| `GetCrosshairMode()`                                 | `ItemDrag`; для запертой: `Unlocked` (есть ключ) / `Locked`      |


E-взаимодействие (`Interact`): если заперта и есть `_requiredKey` в инвентаре — ключ расходуется при `consumeOnUse`, дверь приоткрывается, снимок сейва берётся после обоих изменений.

### Установка в сцене

```
locker_A (MeshRenderer + Collider, НЕ трогаем)
└── door_pivot_top_left          ← пустой GameObject, LocalPos у петли
    └── locker_door_B.L          ← DoorInteraction здесь, _pivot = door_pivot_top_left
```

Объект с `DoorInteraction` должен быть на слое **Interactable**.

---

## DoorAnimator

**Скрипт:** `Assets/Scripts/Interaction/DoorAnimator.cs` (namespace `Escape.Core`)

`RequireComponent(Animator)`. Простая обёртка для аниматорных дверей: `SetInitialState(bool)` и `PlayAnimation(bool)` ставят bool-параметр `IsOpen`. Никакой логики состояния — только визуал.

---

## Добавление нового объекта

Чтобы добавить новый перетаскиваемый объект:

1. Создать скрипт, реализующий `MonoBehaviour`, `IInteractable`, `IDraggable`
2. Поставить **только сам интерактивный объект** на слой **Interactable Layer**
3. Корпус или контейнер, внутри которого находится объект, оставить на слое **Default**
4. Добавить коллайдер
5. Реализовать `OnDragStart` / `OnDrag` / `OnDragEnd`

`FPSController` подхватит объект автоматически — никаких изменений в контроллере не нужно.
