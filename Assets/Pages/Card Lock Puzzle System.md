## Card Lock Puzzle System

Система карт-ридера для дверей. Игрок перетаскивает ID-карту из инвентаря на ридер, видит анимацию слайда, лампочка меняет цвет и дверь отпирается.

### Структура префаба

```
CardLock                          # PuzzleInteractable (вход в режим загадки)
├── CardLock                      # CinemachineCamera (камера крупного плана)
├── cardLock                      # BoxCollider + PuzzleModeController + CardLockPuzzleController
│   ├── CardLock                  # 3D-модель корпуса ридера
│   └── LockLamp                  # MeshRenderer лампочки (меняет материал)
└── IdCard                        # Анимационная карточка (появляется при свайпе)
    └── card                      # 3D-модель карточки
```

### Быстрая настройка в сцене

1. Перетащите префаб `Assets/Prefabs/puzzle/CardLock/CardLock.prefab` в сцену.
2. На объекте `cardLock` в компоненте `CardLockPuzzleController` назначьте два поля:
  - **Target Door** — `DoorInteraction` двери, которая должна открыться.
  - **Required Card** — `ItemData` карточки, которой нужно провести по ридеру.
3. (Опционально) Вставьте звуки в поля **Card Slide Clip** и **Door Unlock Clip**.
4. (Опционально) На `PuzzleModeController` измените **Save ID** на уникальный для каждого экземпляра.

Все остальные ссылки (лампочка, ридер, камера, материалы) находятся автоматически.

### Логика работы

```
Игрок нажимает на замок
  → PuzzleInteractable → PuzzleModeController.EnterPuzzleMode()
  → Камера переключается на крупный план
  → Открывается PuzzleInventoryBar

Игрок перетаскивает карточку на ридер
  → Появляется прозрачное превью (ghost) карточки
  → При отпускании запускается корутина ProcessCardSwipe()

ProcessCardSwipe()
  1. Звук слайда карточки
  2. Анимация слайда (Lerp позиции сверху вниз)
  3. Проверка питания через LightingSystem.Instance.IsPowered
  4a. Если питание есть:
      → Лампочка загорается зелёной
      → Пауза (_delayBeforeReturnControl, 1.5 сек)
      → SetSolved() — возврат управления игроку, камера возвращается
      → Пауза (_delayBeforeDoorUnlock, 0.5 сек)
      → Звук отпирания
      → DoorInteraction.UnlockAndOpen()
  4b. Если питания нет:
      → Лампочка остаётся чёрной
      → Дверь не открывается
```

### Три состояния лампочки


| Состояние | Материал             | Условие                         |
| --------- | -------------------- | ------------------------------- |
| Красная   | `CardLamp_Red.mat`   | Питание есть, загадка не решена |
| Зелёная   | `CardLamp_Green.mat` | Питание есть, загадка решена    |
| Чёрная    | `CardLamp_Black.mat  | Нет электричества               |


Материалы загружаются автоматически из `Resources/Materials/CardLock/`.

### Параметры инспектора

**Per-Instance Setup**

- `Target Door` — дверь для отпирания.
- `Required Card` — карточка-ключ.

**Audio**

- `Card Slide Clip` — звук слайда.
- `Door Unlock Clip` — звук отпирания.
- `Card Slide Volume` / `Door Unlock Volume` — громкость (0–1).

**Timing**

- `Delay Before Return Control` — пауза после зелёной лампочки до возврата управления (1.5 сек).
- `Delay Before Door Unlock` — пауза после возврата управления до отпирания двери (0.5 сек).

**Advanced** (автозаполнение, переопределите только при нестандартной структуре)

- `Puzzle Mode` — контроллер режима загадки.
- `Animated Card` — Transform анимационной карточки.
- `Drop Zone` — коллайдер зоны сброса.
- `Lamp Renderer` — MeshRenderer лампочки.
- `Red/Green/Black/Ghost Material` — материалы состояний.
- `Slide Duration` / `Slide Offset` — параметры анимации слайда.

### Важные детали

- Карточка **расходуется** — после успешного свайпа она удаляется из инвентаря.
- При отсутствии электричества дроп блокируется — карточка остаётся в инвентаре, показывается подсказка «Нужно включить свет».
- Защитная проверка питания внутри `ProcessCardSwipe` остаётся на случай, если питание пропадёт во время анимации слайда.
- Скрипт реализует `IPuzzleDropHandler` и `IPuzzleDropTarget` для интеграции с `PuzzleInventoryBar`.
- `CardLockPuzzleController` должен находиться на том же объекте, что и `PuzzleModeController` (или в его дочерних объектах), чтобы `GetComponentInChildren` нашёл его.