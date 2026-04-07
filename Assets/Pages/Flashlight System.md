## Flashlight System

Система фонарика поддерживает переключение режимов через сменные линзы (синяя, красная, UV).
Каждый режим меняет цвет и параметры света. Скрытые надписи на стенах (`HiddenWallSign`)
видны только в луче фонарика и только в нужном режиме.

---

## Структура файлов

```
Assets/Scripts/Flashlight/
├── FlashlightConfig.cs      # ScriptableObject — все параметры фонарика
├── FlashlightController.cs  # MonoBehaviour — логика включения и режимов
└── HiddenWallSign.cs        # MonoBehaviour — скрытая надпись на стене

Assets/Shaders/
└── HiddenWallSign.shader    # URP Unlit шейдер — конус + фейды + HDR emission
```

---

## FlashlightConfig (ScriptableObject)

Создаётся через **Assets > Create > Game > Flashlight Config**.

### FlashlightState

Параметры света для одного состояния (on/off).

| Поле         | Описание                  |
|---|---|
| `Intensity`  | Яркость источника света   |
| `Range`      | Радиус действия (метры)   |
| `Spot Angle` | Угол конуса (градусы)     |
| `Color`      | Цвет света                |

### FlashlightModeConfig

Описывает один режим (одну линзу).

| Поле            | Описание                                                                          |
|---|---|
| `Mode`          | Идентификатор: `Normal`, `Blue`, `Red`, `UV`                                      |
| `Required Item` | `InventoryCondition` — условие наличия линзы в инвентаре. `null` = всегда доступен |
| `On State`      | `FlashlightState` когда фонарик включён в этом режиме                             |

### Параметры FlashlightConfig

| Поле                  | Описание                                                                           |
|---|---|
| `Operating Condition` | Условие работы фонарика вообще (например, заряженная батарейка)                    |
| `Modes`               | Массив режимов в порядке переключения. Первый элемент — режим по умолчанию         |
| `Off State`           | Параметры света когда фонарик выключен (общий для всех режимов)                    |
| `Transition Speed`    | Скорость плавного изменения яркости при включении/выключении (ед/сек)              |

---

## FlashlightController (MonoBehaviour)

Компонент на GameObject'е фонарика. Требует `Light` на том же объекте.

### Управление

| Клавиша | Действие                                                          |
|---|---|
| `F`     | Включить / выключить фонарик                                      |
| `R`     | Переключить на следующий доступный режим (только когда включён)   |

Режимы с невыполненным `requiredItem` пропускаются при переключении.

### Публичные члены

| Член                                       | Описание                                            |
|---|---|
| `FlashlightMode CurrentMode`               | Текущий активный режим                              |
| `bool IsOn`                                | Включён ли фонарик                                  |
| `event Action<FlashlightMode> OnModeChanged` | Срабатывает при включении, выключении и смене режима |

### Параметры Inspector

| Поле                 | Описание                                                                                  |
|---|---|
| `Config`             | Ссылка на `FlashlightConfig` asset                                                        |
| `Toggle Clip`        | Звук включения / выключения                                                               |
| `Mode Switch Clip`   | Звук смены линзы                                                                          |
| `Toggle Volume`      | Громкость переключения (`0–1`)                                                            |
| `Mode Switch Volume` | Громкость смены режима (`0–1`)                                                            |
| `Sound Condition`    | `InventoryCondition` для звука. Если `null` — звук только когда фонарик реально включается |

Если в рантайме `operatingCondition` перестаёт выполняться — фонарик автоматически выключается.

---

## HiddenWallSign (MonoBehaviour)

Компонент на GameObject'е со `SpriteRenderer`. Надпись невидима по умолчанию.
Появляется только когда фонарик включён и активен нужный режим.

### Параметры Inspector

**Основные**

| Поле             | По умолчанию | Описание                                     |
|---|---|---|
| `Visible In Mode` | `Blue`      | Режим фонарика при котором надпись видна      |
| `Flashlight`      | —           | Ссылка на `FlashlightController` в сцене      |

**Beam Shape**

| Поле             | По умолчанию | Описание                                                             |
|---|---|---|
| `Edge Softness`  | `0.05`       | Мягкость границы конуса. `0` = жёсткий обрез, `0.12` = очень мягкий |
| `Radial Falloff` | `1.5`        | Затухание от центра к краю. `1` = линейное, `2+` = яркое пятно в центре |

**Distance Fade**

| Поле                   | По умолчанию | Описание                                                       |
|---|---|---|
| `Max Visible Distance` | `2` м        | Дистанция, на которой надпись становится едва заметной         |
| `Min Dist Alpha`       | `0.04`       | Минимальная прозрачность за `Max Visible Distance`. `0` = невидима |

**Emission / Glow**

| Поле                 | По умолчанию    | Описание                                                          |
|---|---|---|
| `Emission Color`     | Яркий синий (HDR) | Цвет свечения. HDR-значения выше `1.0` активируют Bloom         |
| `Emission Intensity` | `2`             | Множитель яркости. `4–6` = выраженный ореол                      |

### Как работает шейдер

```
Для каждого пикселя спрайта:

1. coneMask   — smooth fade у границы конуса (0 снаружи, 1 внутри)
2. radialFade — 0 на краю конуса, 1 в центре луча (pow кривая)
3. distFade   — 1 вблизи, minDistAlpha на максимальной дистанции

alpha = texAlpha  × coneMask × radialFade × distFade
rgb   = texColor  × EmissionColor × EmissionIntensity × radialFade × distFade
```

Данные фонарика (позиция, направление, угол) передаются через `MaterialPropertyBlock`
каждый `LateUpdate` — без аллокаций и инстансинга материалов.

### Bloom

Свечение подхватывается Bloom из Volume Profile сцены (`Assets/Settings/SampleSceneProfile`).

| Параметр Bloom | Что делать                                         |
|---|---|
| `Threshold`    | Опусти до `0.8–1.0` чтобы Bloom ловил тусклые пиксели |
| `Intensity`    | Увеличь если ореол слабый                          |
| `Scatter`      | Уменьши если ореол слишком размазан                |

---

## Как добавить новую линзу

1. Добавь значение в `enum FlashlightMode` в `FlashlightConfig.cs`
2. В `FlashlightConfig` asset добавь элемент в массив `Modes`:
   - `Mode` = новое значение
   - `Required Item` = `InventoryCondition` с предметом-линзой
   - `On State` = цвет и параметры света
3. Создай `ItemData` для линзы, размести `PickableItem` в сцене

## Как добавить скрытую надпись

1. Создай GameObject на стене
2. Добавь `SpriteRenderer`, назначь спрайт
3. Добавь компонент `HiddenWallSign`
4. Заполни: `Visible In Mode`, `Flashlight`
5. Настрой `Emission Color` и `Emission Intensity`

```
Wall (MeshRenderer)
└── SignObject    # SpriteRenderer + HiddenWallSign
```

## Как добавить режим без предмета (всегда доступный)

В элементе массива `Modes` оставь `Required Item` пустым (`null`).
