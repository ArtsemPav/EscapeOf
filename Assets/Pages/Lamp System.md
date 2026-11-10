<!-- version: 2 -->

## Overview

Компонент `Lamp` — единая точка управления лампой: зона, все источники света и (опционально) эмиссия мешей. Заменяет собой бывшие `LightZone` (файл `LightGroup.cs`) и `ZoneEmission.cs`, которые объединены и удалены. Все сцены и префабы мигрированы.

Ключевая идея: **один компонент на лампу, ноль ручных ссылок**. Света и рендереры находятся автоматически по месту установки.

См. также [@ id="/Pages/Private/Lighting System.md" label="Lighting System"] — питание, зоны, выключатели, щиток.

---

## Файлы

```
Assets/Scripts/Lighting/
├── Lamp.cs        — компонент Lamp + enum FlickerMode, LampEmissionMode
└── ILampZone.cs   — интерфейс для LightingSystem, RoomController, MriUvCutscene
```

---

## Размещение — три сценария

Компонент сам определяет, чем управлять, по месту установки.

### 1. Лампа с мешем и дочерними светами (неон, GreenLamp)

```
NeonLamp (Lamp)                # Emission Mode = SyncWithLight
├── mesh                       # эмиссия контролируется
├── Point light (28)           # найден автоматически
└── Point light (29)           # найден автоматически
```

### 2. Отдельный свет-подсветка детали комнаты

```
FillLight (Light — Lamp)       # Emission Mode = None
```

`Lamp` вешается прямо на объект света. Эмиссия не трогается вообще.

### 3. Свет вне иерархии лампы

Перечисляется в `Extra Lights` — он **дополняет** автопоиск, а не заменяет его:

```
DeskLamp (Lamp)                # дети находятся всегда автоматически
├── Point light (28)           # автопоиск
└── ...
CeilingSpot (чужой свет)       # указать в Extra Lights → управляется тоже
```

---

## Поля

| Поле | Описание |
|------|----------|
| `Zone Id` | Зона, напр. `"Corridor"`, `"Room1"`. Регистр важен. Совпадает с `LightSwitch.Zone Id`. |
| `Extra Lights` | Чужие `Light` вне иерархии, добавляются к автопоиску детей. Дубликаты отсекаются. |
| `Flicker Mode` | `None` / `Constant` / `Occasional` |
| `Flicker Min Multiplier` | Глубина провалов мерцания (0 = блэкаут, 0.5 = пол-яркости) |
| `Flicker Frequency` | Случайных смен яркости в секунду (2–50 Гц) |
| `Occasional Min/Max Interval` | Пауза между вспышками дрожи (сек) |
| `Occasional Min/Max Duration` | Длительность вспышки (сек) |
| `Emission Mode` | `None` — материалы не трогаются. `SyncWithLight` — свечение зеркалит свет (состояние, fade, мерцание). |
| `Renderers` | Цели эмиссии. Пусто → автопоиск на этом объекте (`GetComponent<Renderer>`). |
| `Material Indices` | Слот материала на рендерер (элемент i → renderers[i]). Вне диапазона → слот 0. |
| `Emission Color` | Цвет свечения, умножается на emissive-карту. |
| `Emission Intensity` | HDR-интенсивность. Больше 1 — bloom при наличии Volume. |

---

## Эмиссия (SyncWithLight)

- Каждому рендереру инстанцируется материал — лампы с общим `NeonLamp.mat` светятся независимо.
- Переключается `_EMISSION` keyword + `_EmissionColor` (URP/Lit).
- Множитель эмиссии = множителю света: включение/выключение, fade и каждый провал мерцания синхронны (бывшее событие `IntensityMultiplierChanged` стало внутренним).
- Может работать **без своих светов** (emission-only лампа): `Lamp` на объекте без `Light` — эмиссия следует за состоянием зоны. Пример: `Antique Light Switch` в Laboratory включает свечение 4 ламп `lamp_A` (слоты `[1,1,1,1]`, интенсивность 4).

---

## Публичный API (ILampZone)

```csharp
lamp.SetActive(bool on);               // вкл/выкл + мерцание (вместо Light.enabled)
lamp.SetIntensityMultiplier(float m);  // fade, 0..1 от OriginalIntensity
lamp.StopFlicker();                    // стоп мерцания, вернуть исходную яркость

lamp.ZoneId;                           // имя зоны
lamp.IsOn;                             // логически включена
lamp.OriginalIntensity;                // макс. исходная интенсивность среди светов
```

---

## Интеграция с LightingSystem

- `Lamp` регистрируется в `Awake` через `RegisterZone(ILampZone)`, снимается в `OnDestroy`.
- Несколько `Lamp` в одной зоне управляются совместно; у каждой свои настройки мерцания.
- Fade длительностью `LightingSystem.Fade Duration`, performance suppression зон и сейв/лоад — как раньше, `zoneId` не менялись, сейвы совместимы.
- `LightingSystem` пропускает избыточные фейды: щелчок выключателя при выключенном питании не трогает лампы (раньше это давало вспышку на 0.3 с — фейд-аут сбрасывал интенсивность на полную и включал свет заново).
- `RoomController.CollectZoneIds()` и `MriUvCutscene` работают через `ILampZone` (`GetComponentInParent<ILampZone>`).

---

## Миграция (выполнена)

- Сцены `Game.unity`, `SampleScene.unity` и 24 префаба (`PRF_*`).
- Неоновые лампы → `Lamp` на корне, `SyncWithLight`; одиночные подсветки → `Lamp` на свете, `None`.
- Дубликаты `Lamp` (префабный + локальный) удалены — на объекте ровно один.
- Настройки мерцания перенесены 1:1 (напр. `Corridor2stF/NeonLamp (5)`: Constant, min 0.08, 18 Гц).

## Известные нюансы

- У `Laboratory/ceiling/NeonLamp (1)/Point light (28)` было два `LightZone` (зоны `Laboratory` и `Room7`) — расценено как копипаст-ошибка, оставлена зона `Laboratory`. Проверить, не нужна ли лампа зоне `Room7`.
- Неоновые лампы `Corridor2stF (1)–(4)`, GreenLamp'ы и MRI-лампы изначально без эмиссии — у них `EmissionMode=None`. При желании свечения переключить на `SyncWithLight`.
- `EmergencyLamp`, `FireLightFlicker`, `NeonLightFlicker`, `EmissionTrigger`, `EmissionRampOnEnable` не входят в систему зон и работают как раньше.
