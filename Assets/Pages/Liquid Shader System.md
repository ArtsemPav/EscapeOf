## Шейдер жидкости — `Custom/LiquidFlask`

Шейдер для отрисовки жидкости в колбах, раковинах, ваннах и других контейнерах. Поддерживает управление уровнем наполнения, покачивание (wobble), преломление фона, дисторшн и эффект линзы.

**Файл:** `/Assets/Shaders/LiquidFlask.shader`
**Контроллер:** `ChemicalPuzzle.LiquidWobble` (`/Assets/Scripts/Puzzle/ChemicalPuzzle/LiquidWobble.cs`)

---

## Свойства шейдера

### Уровень наполнения

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_FillAmount` | Range(0, 1) | 0.5 | Доля заполнения: 0 = пусто, 1 = полная. Управляется через `LiquidWobble.fillFraction` |
| `_LocalMeshMin` | Float | -0.5 | Локальная Y-координата дна меша. Кэшируется автоматически из `MeshFilter.sharedMesh.bounds` |
| `_LocalMeshMax` | Float | 0.5 | Локальная Y-координата верха меша |

Поверхность жидкости вычисляется как `lerp(_LocalMeshMin, _LocalMeshMax, _FillAmount)` в локальном пространстве, затем преобразуется в world space. Пиксели выше поверхности отсекаются через `clip()`.

### Цвет и поверхность

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_LiquidColor` | Color | (0.1, 0.5, 0.9, 1) | Основной цвет жидкости |
| `_SurfaceColor` | Color | (0.3, 0.7, 1.0, 1) | Цвет поверхности/пены |
| `_FoamWidth` | Range(0, 0.5) | 0.02 | Ширина полосы пены у поверхности |
| `_EmissionColor` | Color | (0, 0, 0, 1) | Цвет свечения жидкости |
| `_EmissionPower` | Range(0, 10) | 0.0 | Интенсивность свечения (аддитивное, не зависит от освещения) |
| `_Turbidity` | Range(0, 1) | 0.0 | Мутность: смешивает цвет с шумом для эффекта взвеси |
| `_NoiseScale` | Range(0.1, 10) | 1.0 | Масштаб шума мутности |
| `_NoiseSpeed` | Range(0, 5) | 0.5 | Скорость анимации шума мутности |

### Прозрачность и преломление

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_Opacity` | Range(0, 1) | 0.82 | Непрозрачность: 1 = только цвет жидкости, 0 = только фон |
| `_RefractionStrength` | Range(0, 0.2) | 0.03 | Сила преломления фона по нормалям (искажение краёв) |
| `_ChromaticAberration` | Range(0, 0.02) | 0.004 | Хроматическая аберрация — RGB-расщепление при преломлении |

### Дисторшн и линза

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_DistortionStrength` | Range(0, 0.3) | 0.08 | Сила искажения фона мульти-октавным шумом (центр жидкости) |
| `_DistortionSpeed` | Range(0, 5) | 1.0 | Скорость анимации искажения |
| `_LensStrength` | Range(0, 1) | 0.15 | Сила эффекта линзы — увеличение фона сквозь толщу жидкости |
| `_LensPower` | Range(0, 3) | 1.0 | Степень усиления линзы с глубиной (1 = линейно, 2 = квадратично) |

### Освещение

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_DepthDarken` | Range(0, 1) | 0.5 | Затемнение к низу: 0 = равномерно, 1 = дно полностью чёрное. Модель Beer–Lambert, tilt-aware |
| `_MinLightFloor` | Range(0, 1) | 0.15 | Минимальная яркость в темноте. 0.15 = слабо видна в темноте (колбы). 0 = полностью чёрная без света (раковины, ванны) |

### Покачивание (Wobble)

| Свойство | Тип | Default | Описание |
|---|---|---|---|
| `_PivotWS` | Vector | (0,0,0,0) | Мировая позиция пивота объекта. Обновляется каждый кадр из `transform.position` |
| `_WobbleX` | Float | 0.0 | Смещение поверхности по X. Вычисляется в `LiquidWobble.Update()` из скорости движения |
| `_WobbleZ` | Float | 0.0 | Смещение поверхности по Z. Аналогично `_WobbleX` |

---

## Архитектура рендеринга

### Очередь и композитинг

- **Queue:** `Transparent-100` (2900) — рендерится после opaque, но до стекла (3000)
- **Blend:** `One Zero` — цвет композируется вручную в HLSL, не через alpha-blend
- **ZWrite:** Off
- **Cull:** Off — рисует и передние, и задние грани (задние = поверхность жидкости)
- **DisableBatching:** True — позиции меша должны оставаться в object space

### Источник фона

Шейдер сэмплирует `_CameraOpaqueTexture` — URP захватывает сцену до transparent pass. Свойство `_RefractionStrength` смещает UV сэмплинга для эффекта преломления.

### Освещение

- Основной directional light через `GetMainLight()` с half-Lambert
- Additional lights через `GetAdditionalLight()` (точечные/прожекторы)
- Ambient через `SampleSH()`
- Минимальный floor через `_MinLightFloor` (default 0.15) — жидкость тусклая, но не чёрная в темноте. Для раковин/ванн ставить 0
- Emission аддитивный — виден в полной темноте
- Rendering layer mask на рендерере управляет какими источниками света освещается жидкость (через `_renderingLayerMask` в LiquidWobble)

### VFACE для поверхности

- `facing > 0` (front faces) — тело жидкости + пена + Fresnel rim
- `facing < 0` (back faces) — поверхность жидкости (плоскость среза)

### Shadow caster

Отдельный pass для теней. Повторяет логику clip по `_FillAmount`, чтобы тень соответствовала фактическому уровню жидкости.

---

## Дисторшн и эффект линзы

### Multi-octave noise

Искажение фона использует 3 октавы value noise с разными масштабами и весами:

```
noiseX = octave1 * 0.5 + octave2 * 0.3 + octave3 * 0.2
```

- Октава 1: масштаб `_NoiseScale * 2.5`, вес 0.5 — крупные волны
- Октава 2: масштаб `* 2.1` от первой, вес 0.3 — средние детали
- Октава 3: масштаб `* 4.3` от первой, вес 0.2 — мелкая рябь

### Lens-эффект

`depthRatio` (0 на поверхности → 1 на дне) определяет толщину слоя жидкости между камерой и фоном. Возводится в `_LensPower` для нелинейного усиления:

```
lensFactor = pow(depthRatio, _LensPower)
```

**Два компонента линзы:**

1. **UV magnification** — смещение UV к центру экрана пропорционально `lensFactor * _LensStrength`
2. **Depth-amplified distortion** — шумовое искажение умножается на `lensFactor * 0.5`, усиливая эффект с глубиной

### Итоговое смещение UV

```
refractOffset = normalVS.xy * _RefractionStrength          // edge refraction
              + float2(noiseX, noiseY) * _DistortionStrength  // noise distortion
              + float2(noiseX, noiseY) * _DistortionStrength * lensFactor * 0.5  // lens distortion

refractUV = clamp(lensUV + refractOffset, 0.001, 0.999)
```

### Хроматическая аберрация

RGB-каналы сэмплируются с微小 смещением по X:

```
r = sample(refractUV + float2(+ca, 0))
g = sample(refractUV)
b = sample(refractUV + float2(-ca, 0))
```

---

## LiquidWobble — компонент-контроллер

**Namespace:** `ChemicalPuzzle`
**Файл:** `/Assets/Scripts/Puzzle/ChemicalPuzzle/LiquidWobble.cs`

### Назначение

`LiquidWobble` — мост между Inspector/кодом и шейдером. Управляет:
- Уровнем заполнения (`fillFraction`)
- Покачиванием от движения объекта
- Цветами, прозрачностью, преломлением, дисторшном и линзой
- Rendering layer mask (лайт-группы)
- Минимальной яркостью в темноте (`_minLightFloor`)
- Создаёт material instance (не `MaterialPropertyBlock` — SRP Batcher в URP игнорирует PropertyBlock для `UnityPerMaterial`)

### Ключевые поля

| Поле | Тип | Описание |
|---|---|---|
| `fillFraction` | float (0–1) | Публичное поле. Прямо записывается в `_FillAmount` каждый кадр |
| `_shader` | Shader | Ссылка на шейдер. Защита от stripping в билдах. Если null — `Shader.Find("Custom/LiquidFlask")` |
| `_distortionStrength` | float (0–0.3) | Сила шумового искажения фона |
| `_distortionSpeed` | float (0–5) | Скорость анимации искажения |
| `_lensStrength` | float (0–1) | Сила эффекта линзы |
| `_lensPower` | float (0–3) | Степень усиления линзы с глубиной |
| `_refractionStrength` | float (0–0.2) | Сила преломления по нормалям |
| `_renderingLayers` | string[] | Имена rendering layers из TagManager. `["Default"]` = только Default. Пустой массив = не трогать существующую маску рендерера. Доступные имена: `Default`, `Room1`–`Room7`, `Bathroom`, `flashLight`, `NurseryRoom`, `Corridor`, `Laboratory`, `Stairs`, `Procedural`, `MorrowOfice`, `temp`, `Bathroom1stFloor`, `Pantry2`, `elevator`, `Electric`, `GeneratorRoom` |
| `_minLightFloor` | float (0–1) | Минимальная яркость в темноте. 0.15 = колбы видны в темноте. 0 = ванны/раковины полностью чёрные без света |

### Ключевые методы

| Метод | Описание |
|---|---|
| `AnimateFillTo(float target, float duration)` | Плавная анимация `fillFraction` к target за duration секунд. Используется для слива/наполнения |
| `SetLiquidColor(Color liquid, Color surface, Color emission, float emissionPower)` | Полная установка цветов |
| `SetLiquidColor(Color color)` | Быстрая установка (liquid = surface = color, авто-эмиссия при `_autoEmissionMultiplier > 0`) |
| `SetTransparency(float opacity, float refraction, float chromaticAberration, float depthDarken)` | Установка прозрачности и преломления |

### Lifecycle

- `OnEnable` — кэширует Renderer, MeshFilter, mesh bounds; создаёт material instance; применяет rendering layer mask; применяет свойства
- `Update` — в Play Mode вычисляет wobble из velocity/angular velocity; применяет все свойства к материалу
- `OnDisable` — сбрасывает wobble; в Edit Mode возвращает оригинальный sharedMaterial
- `OnDestroy` — уничтожает только созданный instance, не sharedMaterial asset

---

## Где используется

| Объект | Префаб | Компонент |
|---|---|---|
| Миксер (химическая загадка) | `/Assets/Prefabs/puzzle/ChemicalPuzzle/ChemicalPuzzle.prefab` | `LiquidWobble` |
| Ванна (механика слива) | `/Assets/Prefabs/liquidBath/liquidBath.prefab` | `LiquidWobble` + `LiquidDrainInteractable` |

---

## Что нужно сделать вручную при использовании на новом объекте

1. Назначить материал с шейдером `Custom/LiquidFlask` на MeshRenderer
2. Добавить компонент `LiquidWobble`
3. Назначить `_shader` (перетащить `LiquidFlask.shader` в Inspector) — защита от stripping в билдах
4. Настроить `fillFraction` — стартовый уровень жидкости
5. Настроить цвета (`_liquidColor`, `_surfaceColor`) под тип жидкости
6. Для механики слива — добавить `LiquidDrainInteractable` (см. Liquid Drain System.md)
7. Убедиться что объект имеет Collider (нужен для взаимодействия и блокировки предметов под водой)
8. Настроить `_renderingLayers` в LiquidWobble — добавить имя лайт-слоя комнаты (например `Bathroom1stFloor`, `Room1`, `Corridor` и т.д.). Пустой массив = не трогать маску рендерера
9. Для раковин/ванн: поставить `_minLightFloor = 0` — жидкость не должна светиться в темноте
