# Система перетаскивания (Drag Interaction)

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

## Интерфейсы

### `IDraggable`
```csharp
void OnDragStart();              // ЛКМ нажата
void OnDrag(Vector2 mouseDelta); // каждый кадр пока ЛКМ зажата
void OnDragEnd();                // ЛКМ отпущена
```

### `IInteractable`
Обязательные методы для работы с `FPSController`:
```csharp
string GetInteractText();         // текст подсказки при прицеливании
bool   IsPickable();              // можно ли поднять объект
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

| Поле | Описание | Значение на cupboard_drawer |
|---|---|---|
| `_openDirection` | Локальная ось выдвижения | `(0, 0, -0.9)` |
| `_openDistance` | Дистанция в метрах | `0.65` |
| `_dragSensitivity` | Множитель чувствительности | `2` |
| `_invertAxis` | Инвертировать ось | `false` |
| `_snapSpeed` | Скорость досыла | `8` |
| `_snapThreshold` | Порог срабатывания снапа | `0.25` |

---

## DoorInteraction — дверь

**Скрипты:** `Assets/Scripts/Interaction/DoorInteraction.cs`, `Assets/Scripts/Interaction/DoorAnimator.cs`
**Объект в сцене:** `/Env/FirstRoom/dors/doors_A`

### Принцип работы

Дверь тоже описывается числом `_openFraction` от `0.0` до `1.0`, но движение **инерционное**: мышь добавляет угловую скорость `_velocity`, которая затухает от трения.

```csharp
// Update() — физика затухания
_openFraction = Mathf.Clamp01(_openFraction + _velocity * Time.deltaTime);
_velocity    *= Mathf.Clamp01(1f - _friction * Time.deltaTime);
```

Это даёт ощущение веса — дверь «качается» и останавливается, а не обрывается мгновенно.

Угол применяется через `localEulerAngles.y`:
```csharp
// ApplyAngle()
e.y = _closedLocalEulerY + _openFraction * _maxOpenAngle;
pivot.localEulerAngles = e;
```

### OnDragStart — вычисление оси

Тангент дуги двери (`Vector3.forward` в локальном пространстве шарнира) проецируется на экран — аналогично ящику, но для вращения.

### OnDrag — каждый кадр

```csharp
float input = Vector2.Dot(mouseDelta, _screenSwingDir);
_velocity  += input * _dragSensitivity;
_velocity   = Mathf.Clamp(_velocity, -_maxVelocity, _maxVelocity);
```

Мышь накапливает скорость, а не напрямую задаёт позицию.

### Блокировка и ключи

```csharp
// OnDragStart / OnDrag
if (_isLocked && !_isOpen) return;
```

Заблокированная закрытая дверь игнорирует все попытки потянуть. Разблокировка — через `Unlock()` или `UnlockAndOpen()` (например, от `CodeLock`).

### Параметры инспектора

| Поле | Описание |
|---|---|
| `_maxOpenAngle` | Максимальный угол поворота в градусах |
| `_dragSensitivity` | Насколько быстро мышь разгоняет дверь |
| `_friction` | Коэффициент торможения (больше = резче останавливается) |
| `_maxVelocity` | Предел скорости (защита от резких рывков) |
| `_snapSpeed` | Скорость досыла после отпускания ЛКМ |
| `_snapThreshold` | Порог: выше — открыть, ниже — закрыть |
| `_isLocked` | Заблокирована ли дверь |
| `_requiredKey` | `ItemData` ключа (если нужен) |

---

## Добавление нового объекта

Чтобы добавить новый перетаскиваемый объект:

1. Создать скрипт, реализующий `MonoBehaviour`, `IInteractable`, `IDraggable`
2. Поставить объект на слой **Interactable Layer**
3. Добавить `BoxCollider` (или любой другой коллайдер)
4. Реализовать `OnDragStart` / `OnDrag` / `OnDragEnd`

`FPSController` подхватит объект автоматически — никаких изменений в контроллере не нужно.
