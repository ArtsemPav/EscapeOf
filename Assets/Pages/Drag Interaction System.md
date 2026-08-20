## Обзор

Система позволяет игроку физически открывать и закрывать выдвижные ящики и двери, удерживая ЛКМ и двигая мышью. Контроллер игрока не знает ничего о конкретном объекте — он работает через два интерфейса: `IDraggable` и `IInteractable`.

```
FPSController
├── HandleInteractionDetection()   — raycast, ищет IDraggable + IInteractable
└── HandleDragInteraction()        — маршрутизирует дельту мыши в OnDrag()
        │
        ├── DrawerDrag             — ящик (трансляция)
        └── DoorInteraction        — дверь (вращение)
```

---

## Raycast-логика обнаружения — критически важно

`HandleInteractionDetection()` использует **два последовательных рейкаста**.

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

Проверяет есть ли **между камерой и найденным объектом** геометрия на любом другом слое (стены, полки, закрытые шкафы — они должны быть на **Default**). Если что-то стоит на пути — взаимодействие блокируется.

> Это предотвращает взятие предметов сквозь стены и закрытые ящики.

### Разрешение компонента

После двух успешных лучей контроллер ищет скрипт в следующем порядке:


| Приоритет | Откуда берётся                                         | Зачем                                     |
| --------- | ------------------------------------------------------ | ----------------------------------------- |
| 1         | `GetComponent<IDraggable>()` на хитнутом объекте       | Ящики и двери — коллайдер прямо на них    |
| 2         | `TryGetComponent<IInteractable>()` на хитнутом объекте | `PickableItem`, кодовые замки и т. д.     |
| 3         | `GetComponentInParent<IInteractable>()`                | Рычаги и манометры с дочерним коллайдером |


**Критически важно:** `IDraggable` ищется **только на прямом объекте**, без `GetComponentInParent`. Если искать в родителях — `DrawerDrag` на ящике будет ошибочно перехватывать взаимодействие с предметами внутри ящика (например, `FlashLight`).

---

## Требования к слоям


| Объект                                 | Слой                   | Причина                                |
| -------------------------------------- | ---------------------- | -------------------------------------- |
| Ящики, двери, кнопки, рычаги, предметы | **Interactable Layer** | Луч 1 должен их видеть                 |
| Корпуса мебели, стены, полки           | **Default**            | Луч 2 должен их видеть как препятствия |
| Пустые триггеры, зоны                  | **Ignore Raycast**     | Оба луча их игнорируют                 |


> Если корпус мебели (например, тумбочки) поставить на **Interactable Layer** — Луч 2 его проигнорирует и игрок сможет взаимодействовать с предметами внутри сквозь закрытые стенки.

---

## Интерфейсы

### `IDraggable`

```csharp
void OnDragStart(Vector3 hitPoint); // ЛКМ нажата — передаётся мировая точка попадания рейкаста
void OnDrag(Vector2 mouseDelta);    // каждый кадр пока ЛКМ зажата
void OnDragEnd();                   // ЛКМ отпущена
```

`hitPoint` нужен для корректного вычисления направления открытия: объект знает куда именно игрок взялся и строит от этого геометрию.

### `IInteractable`

Обязательные методы для работы с `FPSController`:

```csharp
string GetInteractText();        // текст подсказки при прицеливании
bool   IsPickable();             // можно ли поднять объект
CrosshairMode GetCrosshairMode(); // иконка прицела
```

---

## FPSController — порядок вызовов за кадр

```
Update()
 ├── HandleInteractionDetection()   // raycast → _currentInteractable
 └── HandleDragInteraction()
       ├── ЛКМ нажата?   → OnDragStart()
       ├── ЛКМ зажата?   → OnDrag(mouse.delta)
       └── ЛКМ отпущена? → OnDragEnd()
```

При активном драге камера вращается с коэффициентом `_dragCameraSensitivityMultiplier` (по умолчанию 0.25) — мышь управляет объектом, а не видом.

---

## DrawerDrag — выдвижной ящик

**Скрипт:** `Assets/Scripts/Interaction/DrawerDrag.cs`
**Объект в сцене:** `/Env/FirstRoom/props/desk_A/CupBoardAnimator/cupboard_drawer`

### Принцип работы

Позиция ящика описывается одним числом `_openAmount` от `0.0` (закрыт) до `1.0` (открыт). Каждый кадр это число переводится в `localPosition`:

```csharp
// ApplyPosition()
Vector3 openLocalPos = _closedLocalPosition + _openDirection.normalized * _openDistance;
transform.localPosition = Vector3.Lerp(_closedLocalPosition, openLocalPos, _openAmount);
```

### OnDragStart — вычисление направления

При нажатии ЛКМ скрипт проецирует мировые координаты закрытой и открытой позиции ящика на экран. Разница — вектор `_screenOpenDir` в пикселях, он показывает в какую сторону двигать мышь чтобы открыть ящик.

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

`_computedSensitivity` обеспечивает **1:1 трекинг**: если ящик занимает 200 пикселей на экране, надо сдвинуть мышь на 200 px чтобы полностью его открыть (без учёта `_dragSensitivity`).

Если ящик смотрит прямо в камеру (длина проекции < 1 px) — используется запасной вектор `Vector2.right` с фиксированной чувствительностью `_dragSensitivity * 0.003`.

Аниматор родительского объекта (`CupBoardAnimator`) отключается на время ручного управления.

### OnDrag — каждый кадр

```csharp
float input = Vector2.Dot(mouseDelta, _screenOpenDir);
if (_invertAxis) input = -input;
_openAmount = Mathf.Clamp01(_openAmount + input * _computedSensitivity);
```

Dot-произведение извлекает только ту составляющую движения мыши, которая совпадает с направлением открытия. Движения перпендикулярно оси игнорируются.

### OnDragEnd — снап

```csharp
_targetOpenAmount = _openAmount >= _snapThreshold ? 1f : 0f;
```

После отпускания ЛКМ ящик досылается до конца или возвращается назад через `Mathf.Lerp` в `Update()`.

### Параметры инспектора


| Поле               | Описание                   | Значение на cupboard_drawer |
| ------------------ | -------------------------- | --------------------------- |
| `_openDirection`   | Локальная ось выдвижения   | `(0, 0, -0.9)`              |
| `_openDistance`    | Дистанция в метрах         | `0.65`                      |
| `_dragSensitivity` | Множитель чувствительности | `2`                         |
| `_invertAxis`      | Инвертировать ось          | `false`                     |
| `_snapSpeed`       | Скорость досыла            | `8`                         |
| `_snapThreshold`   | Порог срабатывания снапа   | `0.25`                      |


---

## DoorInteraction — дверь

**Скрипт:** `Assets/Scripts/Interaction/DoorInteraction.cs`
**Примеры в сцене:** двери шкафчика `locker_A`, ящик стола `desk_A`

### Принцип работы

Состояние двери описывается числом `_openFraction` от `0.0` (закрыта) до `1.0` (открыта). Угол пивота применяется каждый кадр:

```csharp
// ApplyAngle()
e.y = _closedLocalEulerY + _openFraction * _maxOpenAngle;
pivot.localEulerAngles = e;
```

Система работает в **Phasmophobia-стиле**: во время удержания ЛКМ дверь следует за мышью напрямую (1:1). После отпускания — продолжает движение по инерции с затуханием через `_friction`.

```
OnDrag  →  _openFraction += deltaFraction  →  ApplyAngle()   (прямой трекинг)
OnDragEnd  →  Update() применяет _velocity с затуханием     (инерция)
```

---

### OnDragStart — захват точки

```csharp
Vector3 offset = hitPoint - _pivot.position;
offset.y = 0f;
float currentOpenAngle = _dragStartFraction * _maxOpenAngle;
_grabOffsetWorld = Quaternion.AngleAxis(-currentOpenAngle, Vector3.up) * offset;
```

`_grabOffsetWorld` хранится в **системе координат закрытой двери** (pivot = 0°). Это критично: если бы offset хранился в текущем положении, `OnDrag` вращал бы его дважды → инверсия направления при открытой двери.

---

### OnDrag — вычисление направления и sensitivity

**Шаг 1 — текущая мировая позиция точки захвата:**

```csharp
float   openedAngle = _openFraction * _maxOpenAngle;
Vector3 grabWorld   = Quaternion.AngleAxis(openedAngle, Vector3.up) * _grabOffsetWorld;
```

**Шаг 2 — аналитический тангент дуги вращения:**

```csharp
Vector3 swingTangent = Vector3.Cross(Vector3.up, grabWorld / grabDist)
                       * Mathf.Sign(_maxOpenAngle);
```

`cross(up, grabDir)` — производная вращения вокруг Y. Это направление, в котором движется точка захвата при открытии двери. Умножение на `sign(maxOpenAngle)` корректирует знак для дверей с отрицательным углом.

> Почему не `cross(grabDir, up)`? Потому что это противоположный вектор — движение в сторону **закрытия**, а не открытия.

**Шаг 3 — проекция на экран:**

```csharp
Vector2 screenGrab  = cam.WorldToScreenPoint(grabWorldPos);
Vector2 screenAhead = cam.WorldToScreenPoint(grabWorldPos + swingTangent * 0.5f);
Vector2 openDir     = screenAhead - screenGrab;   // куда на экране двигает дверь
```

**Шаг 4 — вычисление deltaFraction:**

```csharp
float screenPerFraction = Mathf.Abs(_maxOpenAngle) * Mathf.Deg2Rad * (openDirMag / 0.5f);
float input             = Vector2.Dot(mouseDelta, openDir / openDirMag);
float deltaFraction     = input * _dragSensitivity / screenPerFraction;
```

`screenPerFraction` — сколько пикселей нужно пройти мышью для поворота на 1 единицу `_openFraction`. `grabDist` намеренно **не включён**: одинаковый свайп мышью = одинаковый угол поворота независимо от места захвата.

---

### Состояния двери


| Флаг                    | Поведение                                                      |
| ----------------------- | -------------------------------------------------------------- |
| `_isLocked && !_isOpen` | Дёргается на `_lockedJiggleFraction`, затем возвращается назад |
| `_isLocked && _isOpen`  | Можно закрыть (дверь уже открыта)                              |
| `!_isLocked`            | Полное свободное перетаскивание                                |


Запертая дверь не снапится — она плавно лерпает обратно к 0 через `_lockedSnapBackSpeed`.

---

### Установка в сцене

```
locker_A (MeshRenderer + Collider, НЕ трогаем)
└── dor_pivot_top_left          ← пустой GameObject, LocalPos у петли
    └── locker_door_B.L         ← DoorInteraction здесь, _pivot = dor_pivot_top_left
```

Объект с `DoorInteraction` должен быть на слое **Draggable**. `_pivot` — родительский пустой объект, расположенный точно на оси петли.

> Если у меша `localScale` с отрицательными компонентами (FBX-импорт), `InverseTransformDirection` даёт неверный результат. Поэтому `_grabOffsetWorld` хранится в **мировых координатах**, минуя transform меша.

---

### Параметры инспектора


| Поле                    | Описание                                                                                                 |
| ----------------------- | -------------------------------------------------------------------------------------------------------- |
| `_maxOpenAngle`         | Угол поворота в градусах. Отрицательный = петля с другой стороны                                         |
| `_dragSensitivity`      | Общий множитель скорости. `1.0` — один свайп через `maxAngle_rad × screenScale` пикселей открывает дверь |
| `_friction`             | Коэффициент затухания инерции после отпускания                                                           |
| `_maxVelocity`          | Ограничение скорости (защита от резких рывков при быстрых движениях мышью)                               |
| `_lockedSnapBackSpeed`  | Скорость возврата запертой двери обратно                                                                 |
| `_lockedJiggleFraction` | Насколько дверь поддаётся при попытке открыть запертую (0–0.15)                                          |
| `_unlockAjarFraction`   | На сколько дверь приоткрывается при `UnlockAndOpen()`                                                    |
| `_requiredKey`          | `ItemData` ключа. Оставь пустым если дверь не требует ключа                                              |


---

## Добавление нового объекта

Чтобы добавить новый перетаскиваемый объект:

1. Создать скрипт, реализующий `MonoBehaviour`, `IInteractable`, `IDraggable`
2. Поставить **только сам интерактивный объект** на слой **Interactable Layer**
3. Корпус или контейнер, внутри которого находится объект, оставить на слое **Default**
4. Добавить `BoxCollider` (или любой другой коллайдер)
5. Реализовать `OnDragStart` / `OnDrag` / `OnDragEnd`

`FPSController` подхватит объект автоматически — никаких изменений в контроллере не нужно.