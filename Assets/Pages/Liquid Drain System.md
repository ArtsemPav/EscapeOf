## Механика слива жидкости — `LiquidDrainInteractable`

Игрок наводит мышку на жидкость в раковине/ванне и кликает ЛКМ. Вода плавно спускается, открывая предметы на дне. Опционально требует предмет в инвентаре (например, вантуз). Состояние сохраняется между сессиями.

---

## Как работает

1. Игрок наводит прицел на жидкость → видит подсказку "Слить воду"
2. Если задан `_requiredItem` и его нет в инвентаре → подсказка "Нужен вантуз", прицел `Locked`
3. Клик ЛКМ → запускается one-shot 3D-звук слива, уровень воды анимируется от `_initialFill` до 0 за `_drainDuration` секунд (`BathLiquidController.SetRuntimeFill` / `AnimateFillTo`)
4. По завершении анимации — `BoxCollider` жидкости отключается, предметы на дне доступны, вызывается `SaveManager.Save()`
5. Звук слива доигрывает до конца естественным образом — не обрывается вместе с анимацией

---

## Архитектура

```
LiquidDrainInteractable          ← IInteractable, ISaveable
├── BathLiquidController         ← управляет уровнем жидкости (шейдер Custom/LiquidBath)
├── MeshRenderer                 ← материал LiquidBath.mat (шейдер Custom/LiquidBath)
└── Collider                     ← блокирует доступ к предметам под водой, отключается после слива
```

**Файл скрипта:** `/Assets/Scripts/Interaction/LiquidDrainInteractable.cs`
**Контроллер жидкости:** `/Assets/Scripts/Interaction/BathLiquidController.cs` (компонент обязателен — `[RequireComponent]`)
**Префаб:** `/Assets/Prefabs/liquidBath/liquidBath.prefab`
**Материал:** `/Assets/Materials/LiquidBath/LiquidBath.mat`

---

## Паттерны проекта

Механика следует существующим паттернам:


| Паттерн          | Реализация                                                                                                                |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Взаимодействие   | `IInteractable` — `UseLMBClick = true`, клик мышкой запускает слив (как `CodeLock`)                                       |
| Звук             | One-shot 3D `AudioSource` (loop = false), регистрируется в `AudioManager`. Доигрывает до конца после анимации             |
| Условие предмета | `_requiredItem` + `InventorySystem.Instance.HasItem()` — только проверка наличия, не потребляется (как `DoorInteraction`) |
| Блокировка       | `GetBlockedHint()` → `_missingItemHint`, `GetCrosshairMode()` → `Locked` при отсутствии предмета                          |
| Сохранение       | `ISaveable` — `SaveId`, `GetSaveData()`, `LoadSaveData()`, регистрация в `SaveManager`                                    |
| Жидкость         | `BathLiquidController.AnimateFillTo(0, _drainDuration)` — плавная анимация уровня (см. Liquid Shader System.md)           |


---

## Настройки компонента


| Поле                     | Тип         | Default        | Описание                                                             |
| ------------------------ | ----------- | -------------- | -------------------------------------------------------------------- |
| `_drainDuration`         | float       | 5              | Время слива в секундах                                               |
| `_initialFill`           | Range(0, 1) | 0.8            | Начальный уровень жидкости                                           |
| `_requiredItem`          | ItemData    | null           | Предмет для слива (вантуз). Если null — без условия                  |
| `_missingItemHint`       | string      | "Нужен вантуз" | Подсказка при отсутствии предмета                                    |
| `_drainClip`             | AudioClip   | null           | One-shot звук слива (3D spatial). Доигрывает до конца после анимации |
| `_drainVolume`           | Range(0, 1) | 0.8            | Громкость звука слива                                                |
| `_drainSoundMinDistance` | float       | 1              | Минимальная дистанция 3D-звука                                       |
| `_drainSoundMaxDistance` | float       | 10             | Максимальная дистанция 3D-звука                                      |
| `_interactText`          | string      | "Слить воду"   | Текст подсказки при наведении                                        |
| `_saveId`                | string      | (GUID)         | Уникальный ID для сохранения                                         |


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
2. Создаёт one-shot 3D `AudioSource` (loop = false), направляет в SFX-группу микшера через `AudioManager.RouteSourceToSfxGroup()`
3. Вызывает `BathLiquidController.AnimateFillTo(0, _drainDuration)`
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

// Загрузка: если isDrained — SetRuntimeFill(0) у BathLiquidController и отключение коллайдера
```

---

## Настройка префаба liquidBath

```
liquidBath                       # корень префаба, слой interactable (6)
├── MeshFilter                   # меш жидкости (liquidBath.fbx → subasset "Liquid")
├── MeshRenderer                 # материал LiquidBath.mat (Custom/LiquidBath)
├── BoxCollider                  # блокирует предметы под водой, отключается после слива
└── LiquidDrainInteractable      # _drainDuration=5, _initialFill=0.8, _saveId задан
```

### Параметры BathLiquidController для ванны

Параметры настраиваются в компоненте `BathLiquidController` (шейдер `Custom/LiquidBath`): цвета, мутность, преломление, дисторшн/линза, `_depthDarken`. Уровень воды анимируется равномерным масштабированием трансформа (меш настраивается на полный уровень, затем правый клик по компоненту → «Capture Full Transform»). Полное описание полей — см. Liquid Shader System.md.

Уровень заполнения: `fillFraction` (Edit Mode) / runtime-значение (`SetRuntimeFill`, Play Mode). Стартовый уровень задаётся через `_initialFill` в `LiquidDrainInteractable` (`Start()` вызывает `SetRuntimeFill(_initialFill)`).

---

## Что нужно сделать вручную при размещении на новой сцене

1. Перетащить префаб `liquidBath.prefab` в сцену
2. Убедиться что объект на слое interactable (layer 6 в проекте)
3. Назначить `_drainClip` — AudioClip звука слива воды
4. Если нужен вантуз — назначить `_requiredItem` (ItemData SO)
5. Разместить pickable-предметы под жидкостью (предметы должны быть на interactable слое, чтобы `FPSController` их нашёл после отключения коллайдера жидкости)
6. Сгенерировать уникальный Save ID: правый клик на компоненте → "Generate Save ID"
7. Настроить визуал жидкости в `BathLiquidController` (цвета, мутность, дисторшн)
8. Настроить `_initialFill` и `_drainDuration` под нужный темп