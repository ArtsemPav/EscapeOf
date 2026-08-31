## Механика слива жидкости — `LiquidDrainInteractable`

Игрок наводит мышку на жидкость в раковине/ванне и кликает ЛКМ. Вода плавно спускается, открывая предметы на дне. Опционально требует предмет в инвентаре (например, вантуз). Состояние сохраняется между сессиями.

---

## Как работает

1. Игрок наводит прицел на жидкость → видит подсказку "Слить воду"
2. Если задан `_requiredItem` и его нет в инвентаре → подсказка "Нужен вантуз", прицел `Locked`
3. Клик ЛКМ → запускается one-shot 3D-звук слива, `fillFraction` анимируется от `_initialFill` до 0 за `_drainDuration` секунд
4. По завершении анимации — `BoxCollider` жидкости отключается, предметы на дне доступны, вызывается `SaveManager.Save()`
5. Звук слива доигрывает до конца естественным образом — не обрывается вместе с анимацией

---

## Архитектура

```
LiquidDrainInteractable          ← IInteractable, ISaveable
├── LiquidWobble                 ← управляет fillFraction → _FillAmount в шейдере
├── MeshRenderer                 ← материал LiquidBath.mat (шейдер Custom/LiquidFlask)
└── Collider                     ← блокирует доступ к предметам под водой, отключается после слива
```

**Файл скрипта:** `/Assets/Scripts/Interaction/LiquidDrainInteractable.cs`
**Префаб:** `/Assets/Prefabs/liquidBath/liquidBath.prefab`
**Материал:** `/Assets/Materials/LiquidBath/LiquidBath.mat`

---

## Паттерны проекта

Механика следует существующим паттернам:

| Паттерн | Реализация |
|---|---|
| Взаимодействие | `IInteractable` — `UseLMBClick = true`, клик мышкой запускает слив (как `CodeLock`) |
| Звук | One-shot 3D `AudioSource` (loop = false), регистрируется в `AudioManager`. Доигрывает до конца после анимации |
| Условие предмета | `_requiredItem` + `InventorySystem.Instance.HasItem()` — только проверка наличия, не потребляется (как `DoorInteraction`) |
| Блокировка | `GetBlockedHint()` → `_missingItemHint`, `GetCrosshairMode()` → `Locked` при отсутствии предмета |
| Сохранение | `ISaveable` — `SaveId`, `GetSaveData()`, `LoadSaveData()`, регистрация в `SaveManager` |
| Жидкость | `LiquidWobble.AnimateFillTo(0, _drainDuration)` — плавная анимация уровня (см. Liquid Shader System.md) |

---

## Настройки компонента

| Поле | Тип | Default | Описание |
|---|---|---|---|
| `_drainDuration` | float | 5 | Время слива в секундах |
| `_initialFill` | Range(0, 1) | 0.8 | Начальный уровень жидкости |
| `_requiredItem` | ItemData | null | Предмет для слива (вантуз). Если null — без условия |
| `_missingItemHint` | string | "Нужен вантуз" | Подсказка при отсутствии предмета |
| `_drainClip` | AudioClip | null | One-shot звук слива (3D spatial). Доигрывает до конца после анимации |
| `_drainVolume` | Range(0, 1) | 0.8 | Громкость звука слива |
| `_drainSoundMinDistance` | float | 1 | Минимальная дистанция 3D-звука |
| `_drainSoundMaxDistance` | float | 10 | Максимальная дистанция 3D-звука |
| `_interactText` | string | "Слить воду" | Текст подсказки при наведении |
| `_saveId` | string | (GUID) | Уникальный ID для сохранения |

---

## Логика

### CanInteract()

Возвращает `true` только если жидкость не слита и не в процессе слива:

```csharp
public bool CanInteract() => !_isDrained && !_isDraining;
```

### Interact()

Проверяет наличие предмета, затем запускает слив:

```csharp
public void Interact()
{
    if (_isDrained || _isDraining) return;
    if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem)) return;
    StartDrain();
}
```

### StartDrain()

1. Устанавливает `_isDraining = true`
2. Создаёт one-shot 3D `AudioSource` (loop = false), регистрирует в `AudioManager`
3. Вызывает `LiquidWobble.AnimateFillTo(0, _drainDuration)`
4. Запускает `DrainCoroutine`

### DrainCoroutine()

1. Ждёт `_drainDuration` секунд (анимация слива)
2. Устанавливает `_isDrained = true`, `_isDraining = false`
3. Отключает `Collider` — предметы на дне доступны
4. Вызывает `SaveManager.Save()`
5. Ждёт окончания звука (`WaitWhile isPlaying`)
6. Уничтожает `AudioSource`, unregister из `AudioManager`

### Save / Load

```csharp
// Сохранение
{ "isDrained": true }

// Загрузка: если isDrained, сразу устанавливает fillFraction = 0 и отключает коллайдер
```

---

## Настройка префаба liquidBath

```
liquidBath                       # корень префаба, слой interactable (6)
├── MeshFilter                   # меш жидкости (liquidBath.fbx → subasset "Liquid")
├── MeshRenderer                 # материал LiquidBath.mat (Custom/LiquidFlask)
├── BoxCollider                  # блокирует предметы под водой, отключается после слива
├── LiquidWobble                 # fillFraction=0.8, дисторшн и линза включены, _minLightFloor=0
└── LiquidDrainInteractable      # _drainDuration=5, _initialFill=0.8, _saveId задан
```

### Параметры LiquidWobble для ванны

| Параметр | Значение | Описание |
|---|---|---|
| `fillFraction` | 0.8 | Стартовый уровень |
| `_opacity` | 0.85 | Мутная вода |
| `_refractionStrength` | 0.05 | Усиленное преломление краёв |
| `_distortionStrength` | 0.12 | Заметное искажение фона |
| `_distortionSpeed` | 1.2 | Скорость анимации |
| `_lensStrength` | 0.2 | Эффект линзы сквозь толщу |
| `_lensPower` | 1.5 | Квадратичное усиление с глубиной |
| `_depthDarken` | 0.5 | Затемнение к дну |
| `_minLightFloor` | 0 | Полностью чёрная в темноте (уважает лайт-группы) |
| `_renderingLayerMask` | 0 | Не трогать маску рендерера (настроить в Inspector) |

---

## Что нужно сделать вручную при размещении на новой сцене

1. Перетащить префаб `liquidBath.prefab` в сцену
2. Убедиться что объект на слое interactable (layer 6 в проекте)
3. Назначить `_drainClip` — AudioClip звука слива воды
4. Если нужен вантуз — назначить `_requiredItem` (ItemData SO)
5. Разместить pickable-предметы под жидкостью (предметы должны быть на interactable слое, чтобы `FPSController` их нашёл после отключения коллайдера жидкости)
6. Сгенерировать уникальный Save ID: правый клик на компоненте → "Generate Save ID"
7. Настроить `_renderingLayers` в LiquidWobble — добавить имена лайт-слоёв, на которых находятся источники света в комнате (например `Bathroom`, `flashLight`, `Room1`, `Corridor` и т.д.). Проверь маску света на источниках в Inspector — имена должны совпадать
8. Убедиться что `_minLightFloor = 0` — жидкость не должна светиться в темноте
9. Настроить `_initialFill` и `_drainDuration` под нужный темп
