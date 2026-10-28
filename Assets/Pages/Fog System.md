## Обзор

Система напольного тумана: полупрозрачный квад на полу, шейдер которого сам вычисляет прямоугольные границы тумана в мировом пространстве и освещение от фонарика игрока. Границы берутся прямо из Transform'а (позиция + масштаб), а параметры фонарика передаются в материал каждый кадр — это позволяет обойти per-pixel additional lights URP и получить управляемый прожекторный свет на тумане.

**Файлы шейдера:** `FloorFog` (свойства `_Bounds*`, `_Flashlight*`).

---

## Скрипты

```
Assets/Scripts/Fog/
├── FogBoundsController.cs   — границы тумана из Transform → материал
├── FogFlashlightReceiver.cs — параметры фонарика → материал
└── FogPlaneMesh.cs          — runtime-генерация квада + rendering layer mask
```

Все три компонента требуют рендерер на том же объекте (`RequireComponent(MeshRenderer)`; `FogPlaneMesh` — также `MeshFilter`).

Типовой объект: один GameObject с `MeshFilter` + `MeshRenderer` (материал FloorFog) + все три компонента.

---

## FogPlaneMesh

**Файл:** `FogPlaneMesh.cs`
`[ExecuteAlways]`. В `Awake()` генерирует плоский квад (4 вершины, 2 треугольника, нормали вверх) в плоскости XZ и применяет rendering layer mask.

### Поля инспектора


| Поле               | Тип        | Default            | Описание                                                                                                                                          |
| ------------------ | ---------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Rendering Layers` | `string[]` | `{ "flashLight" }` | Имена rendering layers (TagManager > Rendering Layers), свет которых влияет на туман. Туман получает свет **только** от источников с этими слоями |


Квад лежит в диапазоне −0.5..0.5 по локальным X и Z — итоговый размер задаётся масштабом Transform'а (см. `FogBoundsController`).

Rendering layer mask строится из захардкоженной таблицы имён проекта (`Default`, `Room1`–`Room7`, `flashLight`, `NurseryRoom`, `Corridor` и т.д. — 22 слоя). Неизвестное имя даёт `LogWarning`. Итог: туман освещается только фонариком (слой `flashLight`), а не комнатными светильниками.

---

## FogBoundsController

**Файл:** `FogBoundsController.cs`

Каждый `LateUpdate()` (при `_autoBounds`) пересчитывает мировые границы из Transform'а и пишет их в материал:

- `_BoundsEnabled = 1`
- `_BoundsMin = (pos.x − scale.x/2, 0, pos.z − scale.z/2)`
- `_BoundsMax = (pos.x + scale.x/2, 0, pos.z + scale.z/2)`
- `_BoundsFalloff = _falloff`

То есть прямоугольник тумана = XZ-габарит объекта, туман заполняет его с плавным затуханием от краёв.

### Поля инспектора


| Поле          | Тип   | Default | Описание                                                                                                                      |
| ------------- | ----- | ------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `Auto Bounds` | bool  | `true`  | Пересчитывать границы из Transform каждый кадр. При `false` пишет `_BoundsEnabled = 0` — границы задаются вручную в материале |
| `Falloff`     | float | `2.0`   | На сколько мировых единиц от края туман начинает затухать                                                                     |


### Публичный API

```csharp
public bool AutoBounds { get; set; }
public float Falloff { get; set; }
```

Материал берётся как инстанс (`renderer.material`) — общие материалы не затрагиваются.

---

## FogFlashlightReceiver

**Файл:** `FogFlashlightReceiver.cs`

Каждый `LateUpdate()` передаёт параметры фонарика в материал, чтобы шейдер вычислял прожекторное освещение вручную — в обход per-pixel additional lights URP.

### Поля инспектора


| Поле              | Тип    | Default         | Описание                                                                      |
| ----------------- | ------ | --------------- | ----------------------------------------------------------------------------- |
| `Flashlight Path` | string | `"/Flashlight"` | Путь к объекту фонарика в иерархии (ищется через `GameObject.Find` в `Start`) |


Если Light по пути не найден — `LogWarning`, обновления прекращаются.

### Передаваемые свойства шейдера


| Свойство                | Источник                            |
| ----------------------- | ----------------------------------- |
| `_FlashlightPos`        | `light.transform.position`          |
| `_FlashlightDir`        | `light.transform.forward`           |
| `_FlashlightColor`      | `light.color` (linear)              |
| `_FlashlightIntensity`  | `light.intensity`                   |
| `_FlashlightRange`      | `light.range`                       |
| `_FlashlightSpotAngle`  | `light.spotAngle`                   |
| `_FlashlightInnerAngle` | `light.innerSpotAngle`              |
| `_FlashlightEnabled`    | `light.enabled && intensity > 0.01` |


---

## Блок-схема кадра

```
Awake/Start:
├── FogPlaneMesh        — генерирует квад, применяет rendering layer mask
├── FogBoundsController — инстанс материала, первый UpdateBounds
└── FogFlashlightReceiver — инстанс материала, поиск Light по пути

LateUpdate (каждый кадр):
├── FogBoundsController
│   ├── _autoBounds → границы из Transform → _BoundsMin/_BoundsMax/_BoundsFalloff
│   └── !_autoBounds → _BoundsEnabled = 0
└── FogFlashlightReceiver
    └── позиция/направление/цвет/интенсивность/углы фонарика → материал
        (_FlashlightEnabled = 0, если фонарик выключен или intensity ≈ 0)
```

---

## Настройка

1. Создать GameObject, добавить `FogPlaneMesh` (MeshFilter+MeshRenderer создадутся), назначить материал с шейдером `FloorFog`.
2. Масштаб Transform'а по X/Z задаёт площадь тумана; высота (Y-позиция) — уровень над полом.
3. Добавить `FogBoundsController` (Auto Bounds = true) и `FogFlashlightReceiver` (путь к фонарику).
4. Убедиться, что у фонарика игрока Rendering Layer = `flashLight` — иначе туман его не увидит.

Связанные системы: фонарик — см. [@ id="/Pages/Private/Flashlight System.md" label="Flashlight System"].