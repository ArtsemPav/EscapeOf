## Обзор

Финальный портал — чисто визуальный эффект-комплекс: спиральная воронка (funnel) из процедурных лент-ринбонов, «дымка»-конус, HDR-ядро с god-rays и кольцом горизонта событий, гравитационная линза, аккреционный диск, частицы-искры/пыль/обломки, молнии и кинематографичная активация (заряд → вспышка → ударная волна → разворачивание воронки). Поверх этого — воздействие на игрока: тряска камеры, FOV-punch, пост-обработка (bloom, хроматика, виньетка, линза), растущая при приближении, и белый флэш при входе в ядро.

Компоненты не требуют ничего назначать руками, кроме материалов — почти всё строится в рантайме. `PortalFunnelController.Activate()` вешается на решение финального пазла двери или триггер.

---

## Скрипты

```
Assets/Scripts/Effects/
├── PortalFunnelController.cs    — главный контроллер воронки: шейдер, частицы, свет
├── PortalFunnelMeshGenerator.cs — процедурный меш спиральных лент ([ExecuteAlways])
├── PortalFunnelSpawner.cs       — временные «волны»-спирали вокруг постоянного ядра
├── PortalFinaleEffects.cs       — финальный слой: ядро, линза, диск, вспышка, шоквейв
├── PortalEnergyParticles.cs     — искры, всасываемая пыль/обломки, молнии
├── PortalPlayerImpact.cs        — тряска, пост-обработка, вход в портал
├── PortalHazeMesh.cs            — процедурный конус-дымка ([ExecuteAlways])
└── PortalRippleMesh.cs          — процедурный диск «водяной ряби» у горла ([ExecuteAlways])

Assets/Shaders/
├── PortalFunnel.shader          — ленты воронки (_ScrollSpeed, _Opacity, _TimeOffset, _Reveal)
├── PortalCore.shader            — ядро (_GlowIntensity, _RayIntensity, _Ring*)
├── PortalAccretionDisk.shader   — аккреционный диск (_InnerRadius, _OuterRadius, _Horizon)
├── PortalArc.shader             — молнии (LineRenderer)
└── PortalHaze.shader            — конус-дымка
```

---

## Префаб

`Assets/Prefabs/Portal/PRF_Portal.prefab`

```
PRF_Portal
├── PortalFunnel     ← PortalFunnelMeshGenerator (10 слоёв лент), PortalFunnelController,
│                       PortalFunnelSpawner, PortalFinaleEffects, PortalEnergyParticles,
│                       PortalPlayerImpact  — все на одном объекте
├── PortalHaze       ← PortalHazeMesh (конус: start 2.9 → end 0.15, h=7)
├── PortalLight      ← Point Light (ссылка в _portalLight)
└── PortalRipple     ← PortalRippleMesh (диск r=0.95, «водяная рябь» у горла)
```

Воронка и дымка повёрнуты на бок (hint −90°/98°) и масштабированы 0.7 — ось воронки горизонтальна.

---

## PortalFunnelController

**Файл:** `PortalFunnelController.cs`
**Компонент:** на объекте воронки (`RequireComponent(MeshRenderer)`). Управляет шейдером воронки (`_ScrollSpeed`, `_Opacity`, `_TimeOffset`, `_Reveal` через MaterialPropertyBlock), вращением меша, частицами вдоль силуэта воронки и портальным светом.

### Поля инспектора


| Поле                     | Тип                 | Default | Описание                                                                          |
| ------------------------ | ------------------- | ------- | --------------------------------------------------------------------------------- |
| `Activate On Start`      | bool                | `true`  | Активировать портал в `Awake` (для теста без пазла)                               |
| `Idle Scroll Speed`      | float               | `0.3`   | Скорость скролла шейдера в простое                                                |
| `Active Scroll Speed`    | float               | `0.9`   | Скорость скролла в активном состоянии                                             |
| `Speed Lerp`             | float               | `1.5`   | Плавность перехода скорости                                                       |
| `Mesh Spin Speed`        | float               | `45`    | Вращение меша вокруг оси воронки, град/сек (в префабе 30)                         |
| `Particle Count`         | int                 | `350`   | Число частиц                                                                      |
| `Particle Start Radius`  | float               | `2.8`   | Радиус кольца эмиссии на широком конце (в префабе 1.4)                            |
| `Particle End Radius`    | float               | `0.1`   | Радиус схождения у горла                                                          |
| `Particle Height`        | float               | `6.5`   | Пройденный путь по оси (высота воронки — 7; в префабе 7)                          |
| `Particle Turns`         | float               | `4`     | Витков спирали на полном пути                                                     |
| `Particle Orbit Speed`   | float               | `0.2`   | Доп. вращение всей спирали вокруг оси, об/сек (в префабе 0.08)                    |
| `Particle Size`          | float               | `0.07`  | Базовый размер частицы (в префабе 0.12)                                           |
| `Particle Lifetime`      | float               | `3.5`   | Время полного пути частицы, сек (в префабе 10)                                    |
| `Particle Color`         | Gradient            | авто    | Градиент цвета за жизнь; без значения строится дефолтный (синий → белый → тёплый) |
| `Particle Material`      | Material            | —       | Материал частиц; без него создаётся URP/Unlit additive                            |
| `Portal Light`           | Light               | —       | Портальный свет (стартовая интенсивность 0)                                       |
| `Active Light Intensity` | float               | `2`     | Целевая интенсивность света в активном состоянии (в префабе 3)                    |
| `Finale`                 | PortalFinaleEffects | —       | Финальный слой; если пуст — берётся с этого же объекта                            |


### Публичный API

```csharp
public void Activate();               // ramp opacity/scroll/particles + свет
public void Deactivate();             // плавное выключение + finale.Stop()
public void SetReveal(float reveal);  // разворот воронки 0..1 (дёргает PortalFinaleEffects)
public void SetTimeOffset(float offset); // десинк волн (использует PortalFunnelSpawner)
public bool IsActive { get; }
public float CurrentOpacity { get; }  // волны синхронизируются с прозрачностью воронки
public float Reveal { get; }          // 0 = свернута, 1 = полностью развёрнута
```

### Частицы вдоль воронки

ParticleSystem создаётся в рантайме с фиксированным сидом (`12345`) — параметры спирали на частицу стабильны. Позиции частиц считаются в скрипте (`UpdateParticles`): прогресс `t` — из возраста частицы, угол — `phase(seed) + t * turns * 2π + swirl`, радиус — `lerp(start, end, t) * jitter(seed)`. Размер и цвет ведут нативные модули ColorOverLifetime / SizeOverLifetime — свежеспавненные частицы всегда корректны.

### Блок-схема

```
Activate()
├── _isActive = true, _targetOpacity = 1, _targetScrollSpeed = active
├── finale? → finale.PlayActivation(this)   // воронка стартует скрытой (_reveal = 0)
└── иначе _reveal = 1

Update():
├── вращение меша вокруг локальной Y
├── lerp scroll/opacity к целям (opacity за FadeDuration = 3 c)
├── ApplyMaterialProperties (MPB: _ScrollSpeed/_Opacity/_TimeOffset/_Reveal)
├── UpdateParticles()
└── свет: base = lerp(0 / activeLightIntensity) * finale.LightMultiplier
```

---

## PortalFinaleEffects

**Файл:** `PortalFinaleEffects.cs`
**Компонент:** на том же объекте, что `PortalFunnelController`. В `EnsureBuilt()` создаёт дочерние объекты: `PortalCore` (квад-ядро), `PortalLens` (гравитационная линза), `PortalAccretionDisk`, `PortalShockwave`, `PortalCoreLight` (Point Light). Все квады — общая runtime-мешь, билборд делается в шейдере, размеры в мировых единицах компенсируют родительский scale.

Центральное состояние — сглаженное `Power` (0..1): им управляется всё, включая `PortalEnergyParticles` и `PortalPlayerImpact`.

### Поля инспектора


| Поле                                 | Тип                     | Default        | Описание                                                      |
| ------------------------------------ | ----------------------- | -------------- | ------------------------------------------------------------- |
| `Core Material` / `Lens Material`    | Material                | —              | Материалы ядра и линзы                                        |
| `Core Local Height`                  | float                   | `6.8`          | Позиция горла по оси воронки (высота 7)                       |
| `Core Size`                          | float                   | `4.5`          | Мировой размер квада ядра, м                                  |
| `Idle Glow` / `Idle Rays`            | float                   | `1.0` / `1.1`  | Базовая интенсивность глоу и god-rays (в префабе 0.45 / 0.35) |
| `Ring Intensity` / `Ring Radius`     | float / Range(0.05–0.5) | `1.6` / `0.16` | Кольцо горизонта событий                                      |
| `Heartbeat Amount`                   | Range(0–1)              | `0.35`         | Сила пульсации «сердцебиения» (0 = нет)                       |
| `Disk Material`                      | Material                | —              | Материал аккреционного диска                                  |
| `Disk Size`                          | float                   | `8`            | Мировой размер квада диска                                    |
| `Disk Inner/Outer Radius`            | float                   | `0.42` / `2.6` | Внутренний/внешний радиус диска (inner > радиуса горизонта)   |
| `Disk Intensity`                     | float                   | `2`            | Интенсивность диска                                           |
| `Lens Size`                          | float                   | `9`            | Размер квада линзы (в префабе 12)                             |
| `Lens Strength`                      | Range(0–3)              | `1`            | Сила искажения (профиль кольца Эйнштейна; в префабе 1.2)      |
| `Horizon Radius`                     | float                   | `0.26`         | Радиус чёрного горизонта событий в центре, м                  |
| `Core Light Color/Intensity`         | Color / float           | тёплый / `6`   | Тёплый «heartbeat»-свет ядра                                  |
| `Charge Duration`                    | float                   | `2.2`          | Фаза заряда: мерцание света и растущая искра перед вспышкой   |
| `Reveal Duration`                    | float                   | `3.2`          | Разворот воронки от горла к краю после вспышки                |
| `Flash Duration` / `Flash Peak Glow` | float                   | `0.9` / `14`   | Вспышка: длительность и пиковый глоу                          |
| `Shockwave Duration`                 | float                   | `1.8`          | Длительность ударной волны                                    |


### Публичный API / события

```csharp
public void PlayActivation(PortalFunnelController controller); // кинематографичная активация
public void Stop();                          // фейд финала (деактивация портала)
public float Power { get; }                  // 0..1, сглажено (read для PortalEnergyParticles/PortalPlayerImpact)
public float Flash { get; }                  // огибающая вспышки 0..1
public bool IsPlayingSequence { get; }
public float LightMultiplier { get; }        // heartbeat+flicker для портального света
public Vector3 CoreWorldPosition { get; }    // мировая позиция горла
public Vector3 AxisWorld { get; }            // ось воронки (transform.up)
public event Action ImpactOccurred;          // момент вспышки (тряска, FOV-punch)
```

### Последовательность активации

```
PlayActivation → ActivationRoutine:
├── Фаза 1 — ЗАРЯД (Charge Duration):
│   ├── _sparkGrowth = t²         ← растущая искра
│   └── шум Perlin: мерцание света учащается 6→28 Гц и темнеет к вспышке
├── Фаза 2 — ВСПЫШКА:
│   ├── _targetPower = 1, запуск шоквейва
│   └── ImpactOccurred.Invoke()   ← PortalPlayerImpact ловит (shake/FOV)
├── параллельно:
│   ├── Flash: резкая атака 8% времени, длинный квадратичный спад
│   ├── Shockwave: кольцо 0.04→0.92 радиуса, ease-out cubic
│   └── SetReveal(easedReveal)    ← воронка разворачивается 1−(1−r)^2.2
└── хвост: _flash = 0, SetReveal(1), idle Power = 1
```

В idle ядро «дышит»: двойной удар heartbeat (0.85 Гц) + шум мерцания; `LightMultiplier` умножает портальный свет. Аккреционный диск и линза включаются при `Power > 0.05`, горизонт событий открывается с порталом и «схлопывается» во время вспышки.

---

## PortalEnergyParticles

**Файл:** `PortalEnergyParticles.cs`
**Компонент:** на том же объекте, что `PortalFinaleEffects` (`RequireComponent`). Все эмиссии масштабируются `finale.Power`: искры по спирали воронки, всасываемая из комнаты пыль и обломки, молнии с поверхности воронки к ядру.

### Поля инспектора


| Поле                               | Тип         | Default                      | Описание                                                                  |
| ---------------------------------- | ----------- | ---------------------------- | ------------------------------------------------------------------------- |
| `Glow Particle Material`           | Material    | —                            | Аддитивный soft-glow материал частиц (как у частиц воронки)               |
| `Debris Material` / `Arc Material` | Material    | —                            | Материалы обломков и молний                                               |
| `Max Sparks` / `Spark Rate`        | int / float | `500` / `120`                | Спиральные искры: максимум и эмиссия/сек (rate в префабе 90)              |
| `Spark Mouth Radius`               | float       | `2.5`                        | Радиус кольца спавна искр                                                 |
| `Spark Travel/Orbit/Radial Speed`  | float       | `2.3` / `3.2` / `−0.7`       | Скорость по оси, орбитальная (VelocityOverLifetime), радиальное схождение |
| `Spark Size`                       | Vector2     | `(0.05, 0.13)`               | Диапазон размеров искр                                                    |
| `Spark Color A/B`                  | Color (HDR) | голубой / тёплый             | Градиент цвета искр (в префабе: `(3,1.3,0.35)` → `(2.6,2.2,1.8)`)         |
| `Max Dust` / `Dust Rate`           | int / float | `300` / `60`                 | Пыль: максимум и эмиссия/сек                                              |
| `Dust Spawn Radius`                | float       | `6`                          | Радиус сферы спавна вокруг середины воронки                               |
| `Dust Pull Speed` / `Dust Swirl`   | float       | `1.1` / `1.4`                | Скорость всасывания и закрутка                                            |
| `Max Debris` / `Debris Rate`       | int / float | `40` / `5`                   | Летающие обломки (paper-подобные двусторонние квады, tumble)              |
| `Debris Pull Speed`                | float       | `0.6`                        | Скорость всасывания обломков                                              |
| `Arc Count`                        | int         | `6`                          | Число молний (LineRenderer); **в префабе 0 — отключены**                  |
| `Arc Jaggedness`                   | float       | `0.4`                        | Изломанность болта (ноль на концах, максимум в середине)                  |
| `Arc Idle Time` / `Arc Life Time`  | Vector2     | `(0.15,1.1)` / `(0.12,0.32)` | Пауза между вспышками и длительность жизни молнии                         |


### Механика всасывания

Пыль и обломки спавнятся в сфере вокруг точки на 35% пути «устье → ядро». В `PullParticles` каждая живая частица получает скорость к ядру с ускорением у горла (`speed = pull * (1 + 4/(d+1))`) и тангенциальную закрутку (спираль сужается ближе к горлу). На расстоянии < `PullKillDistance` (0.35 м, для обломков — масштаб ×0.5) частица «проглатывается» ядром (`remainingLifetime = 0`).

Молнии: старт на поверхности воронки (случайный угол/высота до 70%), конец — ядро с джиттером ±0.08 м; болт из 14 точек перегенерируется каждые 0.045 с («треск»), частота вспышек растёт с Power.

---

## PortalFunnelMeshGenerator

**Файл:** `PortalFunnelMeshGenerator.cs`
**Компонент:** на объекте с MeshFilter (`[ExecuteAlways]`). Генерирует спиральную ленту (helical ribbon); несколько слоёв создают слоистый вид торнадо/водоворота. UV: U — вдоль спирали (0 = широкий конец, 1 = горло), V — поперёк ленты.

### Поля инспектора


| Поле                | Тип             | Default     | Описание                     |
| ------------------- | --------------- | ----------- | ---------------------------- |
| `Height`            | float           | `8`         | Высота воронки (в префабе 7) |
| `Segments Per Turn` | int (8–256)     | `96`        | Сегментов на виток спирали   |
| `Layers`            | `RibbonLayer[]` | 3 дефолтных | Слои лент (в префабе 10)     |


Структура `RibbonLayer` (сериализуется):


| Поле                        | Тип          | Описание                          |
| --------------------------- | ------------ | --------------------------------- |
| `turns`                     | int (1–8)    | Витков спирали                    |
| `startRadius` / `endRadius` | float        | Радиус у широкого конца / у горла |
| `width`                     | float        | Ширина ленты                      |
| `angleOffsetDegrees`        | float        | Угловой сдвиг слоя                |
| `heightFactor`              | Range(0.1–1) | Доля высоты воронки               |
| `startVFade`                | Range(0–1)   | Затухание V у старта              |


### Публичный API

```csharp
public void GenerateMesh();                  // регенерация из текущих слоёв
public void SetLayers(RibbonLayer[] layers); // замена слоёв + регенерация
```

`SetLayers` используют `PortalFunnelSpawner` (ядро) и свои временные волны. Runtime-мешь удаляется в `OnDestroy`.

---

## PortalFunnelSpawner

**Файл:** `PortalFunnelSpawner.cs`
**Компонент:** на объекте воронки. Спавнит временные «волны» — дочерние объекты со своей лентой (`PortalFunnelMeshGenerator`) и общим материалом воронки (вид через MaterialPropertyBlock). Волна: fade-in → дрейф к узкому концу → fade-out → респавн со случайной формой и фазой шейдера — эффект постоянно втягиваемых в портал потоков.

В `Start()` уменьшает постоянное ядро до `_coreLayerCount` слоёв, чтобы волны выделялись.

### Поля инспектора


| Поле                   | Тип             | Default        | Описание                                         |
| ---------------------- | --------------- | -------------- | ------------------------------------------------ |
| `Core Layer Count`     | int             | `4`            | Слоёв постоянного ядра (не гаснут)               |
| `Wave Count`           | int             | `3`            | Одновременных волн                               |
| `Wave Lifetime`        | float           | `6`            | Время жизни волны, сек                           |
| `Spawn Interval`       | float           | `2`            | Задержка между спавнами (и стартовый разброс)    |
| `Fade In/Out Fraction` | Range(0.05–0.5) | `0.3` / `0.4`  | Доли жизни на fade-in / fade-out                 |
| `Layers Per Wave`      | int             | `3`            | Лент в одной волне                               |
| `Drift Distance`       | float           | `2.5`          | Насколько волна уползает к узкому концу за жизнь |
| `Wave Scroll Boost`    | float           | `0.4`          | Доп. скорость скролла шейдера у волн             |
| `Turns Range`          | Vector2         | `(3, 5)`       | Витков спирали у волн                            |
| `Start Radius Range`   | Vector2         | `(2.2, 2.9)`   | Стартовый радиус                                 |
| `Width Range`          | Vector2         | `(0.25, 0.45)` | Ширина лент                                      |


Волны умножают свою альфу на `controller.CurrentOpacity * SmoothStep(controller.Reveal)` — не появляются раньше открытия портала. `TimeOffset` каждой волны — `(index+1) * 37.317` (десинк паттернов).

---

## PortalHazeMesh

**Файл:** `PortalHazeMesh.cs`
**Компонент:** на объекте `PortalHaze` (`[ExecuteAlways]`, RequireComponent MeshFilter). Генерирует открытый конус без крышек, повторяющий силуэт воронки — полупрозрачный слой «дымки», за которым ничего не видно отчётливо. UV: U — вокруг оси, V — по высоте.


| Поле                          | Тип         | Default        | Описание                     |
| ----------------------------- | ----------- | -------------- | ---------------------------- |
| `Height`                      | float       | `7`            | Высота конуса                |
| `Start Radius` / `End Radius` | float       | `2.9` / `0.15` | Радиус у основания / у горла |
| `Radial Segments`             | int (8–128) | `64`           | Сегментов по окружности      |
| `Height Segments`             | int (1–64)  | `16`           | Сегментов по высоте          |


`GenerateMesh()` вызывается из `Awake` и `OnValidate` (живая правка в инспекторе). Регенерация публичная: `GenerateMesh()`.

---

## PortalRippleMesh

**Файл:** `PortalRippleMesh.cs`
**Компонент:** на объекте `PortalRipple` (`[ExecuteAlways]`, RequireComponent MeshFilter). Генерирует плоский круг (веерная триангуляция) — поверхность «водяной ряби» у горла портала. UV центрированы: (0.5, 0.5) в центре, rim на радиусе 0.5.


| Поле       | Тип         | Default | Описание                |
| ---------- | ----------- | ------- | ----------------------- |
| `Radius`   | float       | `0.95`  | Радиус диска            |
| `Segments` | int (8–128) | `64`    | Сегментов по окружности |


---

## PortalPlayerImpact

**Файл:** `PortalPlayerImpact.cs`
**Компонент:** на объекте воронки (`RequireComponent(PortalFinaleEffects)`, `[DefaultExecutionOrder(-1000)]` — раньше скриптов камеры игрока).

### Что делает

- **Активация:** тряска камеры и FOV-punch по событию `PortalFinaleEffects.ImpactOccurred` (сила зависит от дистанции).
- **Приближение:** низкочастотный rumble + пост-обработка (глобальный Volume с Bloom / ChromaticAberration / Vignette / LensDistortion, создаются рантаймом, priority 50), растущие при приближении к ядру.
- **Вход в ядро:** растяжка FOV + белый флэш через полноэкранный Canvas-оверлей (sortingOrder 5000), в пике — `On Player Entered` (например, загрузка концовки).
- Смещения камеры применяются аддитивно в `LateUpdate` и снимаются первым делом в следующем `Update` — скрипты камеры игрока никогда не перезаписываются.
- **Line of sight:** рейкаст с камеры к ядру по `_blockingMask` (стены, закрытые двери). Нет прямой видимости → пост-обработка/тряска/румбл плавно гаснут (0.4 с) — эффект не «протекает» сквозь геометрию.

### Поля инспектора


| Поле                                  | Тип                | Default        | Описание                                               |
| ------------------------------------- | ------------------ | -------------- | ------------------------------------------------------ |
| `Effect Range`                        | float              | `16`           | Дистанция, с которой портал начинает влиять на игрока  |
| `Full Effect Distance`                | float              | `3`            | Дистанция полной силы эффектов                         |
| `Impact Shake Amplitude`              | float              | `0.14`         | Амплитуда тряски от удара активации                    |
| `Rumble Amplitude`                    | float              | `0.012`        | Постоянный румбл вблизи                                |
| `Impact FOV Kick`                     | float              | `7`            | FOV-punch при активации                                |
| `Bloom Intensity` / `Bloom Threshold` | float / Range(0–1) | `1.6` / `0.85` | Параметры bloom при полной силе (в префабе 1.1 / 0.85) |
| `Chromatic Aberration`                | Range(0–1)         | `0.45`         | Хроматическая аберрация (в префабе 0.2)                |
| `Vignette`                            | Range(0–1)         | `0.32`         | Виньетка (в префабе 0.4)                               |
| `Lens Distortion`                     | Range(−1–0)        | `−0.22`        | Дисторсия линзы                                        |
| `Blocking Mask`                       | LayerMask          | `Everything`   | Слои, блокирующие эффекты при occlusion ядра           |
| `Enter Radius`                        | float              | `1.6`          | Радиус входа в портал                                  |
| `Enter FOV Stretch`                   | float              | `28`           | Растяжка FOV при входе                                 |
| `On Player Entered`                   | UnityEvent         | —              | Выстреливает в пике белого флэша при входе в ядро      |


### Блок-схема

```
LateUpdate():
├── distance = |camera − CoreWorldPosition|
├── proximity = 1 − InverseLerp(fullEffectDistance, effectRange, distance)
├── UpdateVisibility(): raycast к ядру по BlockingMask → _visibility (сглажена 0.4 c)
├── proximity *= _visibility
├── UpdateVolume(power, proximity)  ← вес Volume = power * lerp(0.45, 1, proximity) * _visibility
├── UpdateEnterTrigger: power > 0.8 && distance <= enterRadius
│   └── EnterRoutine: FOV + белый флэш 1.1 c → OnPlayerEntered → hold 0.6 c → затухание 1.2 c
└── ApplyCameraEffects: Perlin-тряска (trauma² * impact + power * proximity² * rumble)
```

---

## Интеграция

- **Активация:** `PortalFunnelController.Activate()` — повесить на решение финального пазла или триггер; либо `Activate On Start = true` для теста.
- **Конец игры:** `PortalPlayerImpact` → `On Player Entered` (UnityEvent) — загрузка финальной сцены.
- Связанные триггер-утилиты (`Assets/Scripts/Core/`):
  - `EmissionTrigger.cs` — включает emission на материале цели (URP/Lit, `_EmissionColor` + keyword `_EMISSION`, инстанс материала) при входе игрока в триггер-зону. Используется в префабе финальной двери (`PRF_CoridorFDoor`, объект `EmissionTriggerZone`) как визуальная подсказка подхода к финалу.
  - `ParticleZoneTrigger.cs` — включает/выключает ParticleSystem по входу/выходу игрока (рендерер скрыт до входа). Самостоятельная утилита, к префабу портала напрямую не подключена.