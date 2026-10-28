## Обзор

Головоломка «Метаморф» — физическая головоломка с вращающимися цилиндрами. На столе лежат декоративные цилиндры (Cylinder.001–.012), а четыре главных цилиндра-кнопки (Cylinder 1–4) игрок может поворачивать по 90°. При повороте кнопка «подхватывает» лежащие на ней декоративные цилиндры и переносит их вместе с собой. Задача — расположить каждый заданный декоративный цилиндр над своим триггером-наградой. Когда все пары «цилиндр → триггер» пересекаются, пазл решён.

**Проверка решения:** по мировым bounds коллайдеров — `rewardTrigger.bounds.Intersects(cylinderCollider.bounds)`. Никаких скриптов на самих декоративных цилиндрах для победы не нужно — только маркер `MetamorfDecorCylinder`.

---

## Скрипты

```
Assets/Scripts/Puzzle/Metamorf/
├── MetamorfPuzzleController.cs   — корневой контроллер условий, ISaveable
├── MetamorfCylinderButton.cs     — вращающаяся кнопка-цилиндр, IInteractable
└── MetamorfDecorCylinder.cs      — маркер декоративного цилиндра, кэш исходного трансформа
```

---

## MetamorfPuzzleController

**Файл:** `MetamorfPuzzleController.cs`

Корневой контроллер. Вешается на один GameObject-корень пазла, кнопки (`MetamorfCylinderButton`) находит автоматически в детях (включая неактивные). Реализует `ISaveable` — регистрируется в `SaveManager` в `Awake()`.

**Условие победы:** каждый указанный декоративный цилиндр должен пересекаться bounds-ами со своим назначенным триггером.

### Поля инспектора


| Поле                             | Тип                        | Default                       | Описание                                                                                             |
| -------------------------------- | -------------------------- | ----------------------------- | ---------------------------------------------------------------------------------------------------- |
| `Save Id`                        | string                     | `"metamorf_puzzle_unique_id"` | Уникальный ключ сохранения                                                                           |
| `Target Pairs`                   | `List<CylinderTargetPair>` | —                             | Пары «декоративный цилиндр → триггер-награда». Пустой список = предупреждение в консоль              |
| `On Puzzle Solved`               | UnityEvent                 | —                             | Выстреливает один раз при решении (визуалы/UI, звук)                                                 |
| `Colliders To Disable On Solved` | `Collider[]`               | —                             | Коллайдеры, отключаемые при решении (например, опорные плиты, блокирующие дальнейшее взаимодействие) |


### Структура пары

```csharp
[Serializable]
public struct CylinderTargetPair
{
    public MetamorfDecorCylinder cylinder;  // декоративный цилиндр (напр. Cylinder.001)
    public Collider rewardTrigger;          // триггерная зона для этого цилиндра
}
```

### Блок-схема

```
OnCylinderStateChanged()            ← вызывается каждой кнопкой после поворота
├── _isSolved? → выход
├── для каждой пары из Target Pairs:
│   ├── cylinder / rewardTrigger = null → allCorrect = false
│   └── rewardTrigger.bounds.Intersects(cylinder.bounds)?
│       └── нет → allCorrect = false
├── allCorrect == false → выход
└── РЕШЕНО:
    ├── _isSolved = true
    ├── DisableCollidersOnSolved()
    ├── OnPuzzleSolved.Invoke()
    ├── SaveManager.Save()
    └── ProgressLogger.Log("puzzle_solved", "metamorf_puzzle")
```

При загрузке сейва с `_isSolved = true` повторно стреляет только `OnPuzzleSolved` (визуалы) — автосейва и лога прогресса при загрузке нет.

---

## MetamorfCylinderButton

**Файл:** `MetamorfCylinderButton.cs`
**Компонент:** на каждом Cylinder 1–4. Требует `SphereCollider` (`RequireComponent`). Реализует `IInteractable`.

По взаимодействию: находит в сфере коллайдера декоративные цилиндры, «прикрепляет» их к себе (reparent), вращает сам цилиндр на 90° за указанное время, затем возвращает декор на исходных родителей — декорации остаются на новом месте в мировом пространстве.

### Интерфейс IInteractable


| Метод                | Значение              |
| -------------------- | --------------------- |
| `GetInteractText()`  | `"Повернуть"`         |
| `GetCrosshairMode()` | `CrosshairMode.Point` |
| `UseLMBClick`        | `true`                |
| `CanInteract()`      | `!_isAnimating`       |
| `IsPickable()`       | `false`               |


### Поля инспектора


| Поле                    | Тип       | Default   | Описание                                                        |
| ----------------------- | --------- | --------- | --------------------------------------------------------------- |
| `Detectable Layer Mask` | LayerMask | —         | Слой декоративных цилиндров (Cylinder.001–.012), обычно Default |
| `Rotation Duration`     | float     | `0.4`     | Длительность одного поворота, сек                               |
| `Step Angle`            | float     | `90`      | Угол одного такта, градусы                                      |
| `Rotation Axis`         | Vector3   | `(0,1,0)` | Локальная ось вращения                                          |
| `Rotation Sound`        | AudioClip | —         | Звук поворота (через `AudioManager.PlaySFX`)                    |


### Публичный API

```csharp
public event Action OnStateChanged;  // после каждого завершённого поворота
public bool IsCollapsed { get; }    // флаг, инвертируется на каждом такте
```

### Блок-схема поворота

```
Interact()
├── _isAnimating? → выход
└── RotateStepRoutine():
    ├── _isAnimating = true, PlaySFX(_rotationSound)
    ├── CaptureDecors()   ← OverlapSphere по центру/радиусу SphereCollider
    │   └── все hit с MetamorfDecorCylinder → SetParent(this, worldPositionStays: true)
    ├── Slerp localRotation += StepAngle вокруг RotationAxis за RotationDuration
    ├── ReleaseDecors()   ← все MetamorfDecorCylinder в детях → SetParent(OriginalParent, true)
    ├── _isCollapsed = !_isCollapsed
    ├── _isAnimating = false
    └── OnStateChanged?.Invoke()  ← контроллер проверяет условие победы
```

Радиус захвата считается в мировых единицах: `SphereCollider.radius * max(lossyScale)`. `OnDisable()` сбрасывает `_isAnimating`, чтобы корутина не «залипала» при выключении объекта.

---

## MetamorfDecorCylinder

**Файл:** `MetamorfDecorCylinder.cs`
**Компонент:** на каждом декоративном цилиндре (Cylinder.001–.012). Маркер-компонент: поле инспектора нет.

Кэширует в `Awake()` исходный `parent`, `localRotation` и `localPosition`. `MetamorfCylinderButton` ищет этот компонент при захвате (`CaptureDecors`) и отпускает (`ReleaseDecors`), возвращая объект к `OriginalParent` с сохранением мировых позиций — декор «остаётся лежать» там, куда его донёс поворот.

### Публичный API

```csharp
public Transform OriginalParent { get; }
public Quaternion OriginalLocalRotation { get; }
public Vector3 OriginalLocalPosition { get; }
public void ResetToOriginal();  // мгновенный сброс к стартовому состоянию
```

---

## Сцена и ассеты

```
Assets/Models/Metamorf/metamorf.fbx, metamorf 1.fbx   — модели пазла
Assets/Materials/decals/M_Slides.mat                  — декали
Assets/Animations/MetamorfPuzzle/                     — ACMetamorfPuzzle.controller,
                                                        Idle1 / Opening1 / PuzzleSolved.anim
```

### Настройка

1. На корень пазла — `MetamorfPuzzleController`, заполнить `Target Pairs` (каждому Cylinder.001–.012 — свой reward-триггер), при необходимости — `Colliders To Disable On Solved`.
2. На каждом Cylinder 1–4 — `MetamorfCylinderButton` (+ SphereCollider создаётся автоматически), слой детекта — Default.
3. На каждом декоративном цилиндре — `MetamorfDecorCylinder`.
4. На `On Puzzle Solved` повесить открытие двери / анимацию `PuzzleSolved`.