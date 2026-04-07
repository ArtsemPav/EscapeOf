## Horror System

Система хоррор-событий. Каждое событие — отдельный `HorrorEvent` компонент: определяет условие срабатывания, эффект и опциональные Unity-коллбэки. `HorrorSystem` — координатор, подписывается на `InventorySystem` и `GameManager`.

---

## Файлы

```
Assets/Scripts/Core/
├── HorrorSystem.cs   # Singleton-координатор
├── HorrorEvent.cs    # Одно хоррор-событие (MonoBehaviour + ISaveable)
└── HorrorTrigger.cs  # Устарел — заменён HorrorEvent. Не использовать.
```

---

## HorrorSystem

Синглтон. Не хранит собственного состояния событий — только реестр `HorrorEvent`.

| Метод | Описание |
|---|---|
| `Register(HorrorEvent)` | Вызывается из `HorrorEvent.Start()` автоматически |
| `Unregister(HorrorEvent)` | Вызывается из `HorrorEvent.OnDestroy()` автоматически |
| `Trigger(string eventId)` | Ручной вызов события по ID из любого скрипта |

### Автоматические триггеры

- `OnInventoryChanged` → срабатывает все `OnItemPickup` события у которых `RequiredItem` уже есть в инвентаре.
- `OnRoomChanged(int)` → срабатывает все `OnRoomEnter` события с нужным `RequiredRoomIndex`.

---

## HorrorEvent

Компонент на любом всегда-активном GameObject. `_target` — объект который показывается/скрывается.

### Типы триггеров (`HorrorTriggerType`)

| Значение | Когда срабатывает |
|---|---|
| `OnItemPickup` | Игрок подобрал `RequiredItem` |
| `OnRoomEnter` | Игрок перешёл в комнату `RequiredRoomIndex` |
| `OnManual` | Только через `HorrorSystem.Instance.Trigger(eventId)` |
| `OnPlayerEnterZone` | Игрок вошёл в Box Trigger этого GameObject |

Для `OnPlayerEnterZone` нужен `BoxCollider` с `Is Trigger = true` на том же объекте.

### Типы эффектов (`HorrorEffectType`)

| Значение | Поведение |
|---|---|
| `AppearAndStay` | `_target` появляется и остаётся до ручного `Deactivate()` |
| `AppearThenDisappearOnLookAway` | Появляется; исчезает после того как игрок посмотрел и отвернулся |
| `AppearThenDisappearAfterDelay` | Появляется; исчезает через `_disappearDelay` секунд |
| `DisappearOnTrigger` | `_target` изначально видим; исчезает при срабатывании триггера |

### Параметры Inspector

**Identity**

| Поле | Описание |
|---|---|
| `Event Id` | Уникальная строка. Используется как ключ для `HorrorSystem.Trigger()` и `ISaveable.SaveId` |

**Trigger**

| Поле | Описание |
|---|---|
| `Trigger Type` | `HorrorTriggerType` |
| `Required Item` | `ItemData` (только `OnItemPickup`) |
| `Required Room Index` | Индекс комнаты (только `OnRoomEnter`) |
| `Player Tag` | Тег входящего коллайдера (только `OnPlayerEnterZone`). По умолчанию `"Player"` |
| `Activation Delay` | Секунд паузы между срабатыванием триггера и началом эффекта |

**Effect**

| Поле | Описание |
|---|---|
| `Effect Type` | `HorrorEffectType` |
| `Target` | GameObject который показывается/скрывается |
| `Disappear Delay` | Время до скрытия (только `AppearThenDisappearAfterDelay`) |

**Look Detection** (только `AppearThenDisappearOnLookAway`)

| Поле | По умолчанию | Описание |
|---|---|---|
| `Player Camera` | `Camera.main` | Камера для проверки направления взгляда |
| `Look At Threshold` | `0.7` | Dot-порог «смотрю на цель» (0.7 ≈ 45°) |
| `Look Away Threshold` | `0` | Dot-порог «отвернулся» (0 = 90° от оси) |

**Callbacks**

| Поле | Описание |
|---|---|
| `On Activated` | `UnityEvent` — срабатывает при начале эффекта (после задержки) |
| `On Deactivated` | `UnityEvent` — срабатывает при скрытии цели |

### Публичный API

| Метод / Свойство | Описание |
|---|---|
| `bool HasFired` | True если событие уже сработало |
| `void Activate()` | Запускает эффект вручную (идемпотентен — повторный вызов игнорируется) |
| `void Deactivate()` | Скрывает `_target`, стреляет `OnDeactivated` |

### Сохранение

`SaveId = "horror_" + _eventId`. Сохраняет только `hasFired`.

- Для `AppearAndStay`: если `hasFired = true`, таргет остаётся видимым после загрузки.
- Для остальных типов: повторного показа не будет, таргет при загрузке скрыт.

---

## Как добавить новое событие

1. Создай пустой GameObject в сцене (всегда активный).
2. Добавь компонент `HorrorEvent`.
3. Заполни `Event Id` — уникальная строка.
4. Выбери `Trigger Type` и заполни соответствующее условие.
5. Выбери `Effect Type`, назначь `Target`.
6. Подключи `On Activated` / `On Deactivated` (звук, анимацию и т.д.).
7. Если используется `OnPlayerEnterZone` — добавь `BoxCollider` с `Is Trigger = true`.

---

## HorrorTrigger (устарел)

`HorrorTrigger.cs` оставлен в проекте для обратной совместимости, но **не используется** в новых событиях. Заменён `HorrorEvent` + `HorrorSystem`. Не добавлять на новые объекты.
