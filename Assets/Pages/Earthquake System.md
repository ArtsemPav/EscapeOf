<!-- version: 1 -->

Эффект землетрясения после решения химической загадки и физическая реакция предметов комнаты. Скрипты: `Assets/Scripts/Effects/EarthquakeFOVPulse.cs`, `Assets/Scripts/Effects/EarthquakePhysicsProp.cs`.

---

## Как это работает

```
Химическая загадка решена
  └─ UnityEvent OnPuzzleSolved (PuzzleModeController)
       └─ EarthquakeFOVPulse.Play()
            ├─ защита «один раз за сейв» (ISaveable, chemical_puzzle_earthquake)
            ├─ звук землетрясения (_earthquakeAudio)
            ├─ ShakeStarted (duration, fadeIn, fadeOut)  ← статическое событие
            ├─ тряска камеры (Cinemachine Perlin) + пульс FOV
            └─ по завершении / прерывании: ShakeEnded
                     └─ все EarthquakePhysicsProp снимают физику
```

`EarthquakeFOVPulse` — источник правды о землетрясении: держит тайминги, огибающую (fade-in/out), звук и защиту от повторного проигрывания. Все остальные реагенты подписываются на его статические события — отдельная настройка и синхронизация не нужны.

---

## EarthquakeFOVPulse (эффект на камере)

Подробное описание полей — в шапке скрипта. Ключевое:

- **Shake (Perlin):** `_shakeAmplitude`, `_shakeFrequency` — множители шума `CinemachineBasicMultiChannelPerlin` на `PlayerCamera`.
- **FOV Pulse:** `_fovAmplitude`, `_pulseFrequency` — пульс поля зрения для дезориентации.
- **Timing:** `_duration` (default 10 с), `_fadeIn` (0.5 с), `_fadeOut` (1.5 с) — общая огибающая эффектов.
- **Audio:** `_earthquakeAudio` — если не задан, берётся `AudioSource` с этого же объекта; роутинг на SFX-шину через `AudioManager`.

Методы: `Play()` — запуск с защитой «один раз»; `PlayForced()` — отладочный запуск без защиты; `Play(float duration)` — с кастомной длительностью.

**Защита при загрузке сейва:** если пазл уже решён в сохранении, эффект считается отыгранным — при повторе UnityEvent `OnPuzzleSolved` во время загрузки `Play()` съедает вызов молча (проверка `SaveManager.InstanceHasSettled`). Предметы поэтому повторно не трясутся.

### Статические события

| Событие                                      | Когда стреляет                                          | Кто слушает                            |
| -------------------------------------------- | ------------------------------------------------------- | -------------------------------------- |
| `ShakeStarted(duration, fadeIn, fadeOut)`    | В начале `PlayInternal()` — вместе со стартом тряски    | `EarthquakePhysicsProp` на предметах   |
| `ShakeEnded`                                 | По завершении корутины или при `StopEffect()` (в т.ч. `OnDisable`) | `EarthquakePhysicsProp` |

---

## EarthquakePhysicsProp (реакция предмета)

Компонент на **каждом** предмете, который должен реагировать (колбы, бутылки, инструменты). Никаких ссылок в Inspector не требует — подписывается на статические события `EarthquakeFOVPulse` сам.

**Поведение:**

```
ShakeStarted(duration, fadeIn, fadeOut)
  ├─ Rigidbody: найден — используется как есть (не изменяется, не удаляется);
  │             нет — добавляется (_mass, _useGravity, Interpolate)
  ├─ Коллайдера нет — добавляется BoxCollider по габаритам всех Renderer-ов
  └─ Цикл толчков: каждые _impulseInterval (±30%) случайный импульс
       └─ сила = _impulseForce × огибающая землетрясения (fade-in/out синхронно с камерой)
ShakeEnded
  └─ добавленные Rigidbody и BoxCollider удаляются
     предмет остаётся там, куда его привела физика (позиции НЕ восстанавливаются)
```

- Предметы с **только** триггерными коллайдерами считаются «без коллайдера» — создаётся fallback BoxCollider, иначе предмет провалился бы сквозь пол.
- Если у предмета нет ни коллайдера, ни рендереров — в консоль пишется warning, предмет пропускается.
- Толчок: случайное горизонтальное направление + вертикальная составляющая `_upwardBias` (предмет подпрыгивает, а не вдавливается), плюс крутящий момент `_torqueForce`.

### Поля Inspector

| Поле               | Default | Описание                                                              |
| ------------------ | ------- | --------------------------------------------------------------------- |
| `_impulseForce`    | 0.5     | Максимальная сила одного импульса (при полной огибающей)              |
| `_impulseInterval` | 0.2 с   | Средняя пауза между толчками                                          |
| `_upwardBias`      | 0.4     | Вертикальная составляющая: 0 — только горизонтальные толчки           |
| `_torqueForce`     | 0.15    | Закручивание предмета                                                 |
| `_mass`            | 1       | Масса добавляемого Rigidbody                                          |
| `_useGravity`      | true    | Гравитация добавляемого Rigidbody                                     |

---

## Где включено в сцене

- `EarthquakeFOVPulse` — на объекте `/Env/1stFloor/Laboratory/Puzzle/ChemicalPuzzle` (рядом `ChemicalSynthesisController`), запускается его UnityEvent при решении.
- `EarthquakePhysicsProp` — повешен на `Flask01` в префабе `Assets/Prefabs/rooms/PRF_Laboratory.prefab` (`Props/Flask01`). Остальные предметы комнаты вешаются вручную по необходимости.

---

## Как добавить реагирующий предмет

1. Выбери предмет декора (желательно небольшой: колба, банка, инструмент).
2. Добавь компонент `EarthquakePhysicsProp`.
3. При необходимости подстрой силу: для тяжёлых предметов поднимай `_mass`, для «нервных» — `_impulseForce`/`_impulseInterval`.

Ничего подключать к `EarthquakeFOVPulse` не нужно — реакция придёт через статические события.

> **Нюанс:** физика позиций не восстанавливается — предмет после землетрясения лежит там, куда скатился (может укатиться под мебель). Это осознанный выбор. Если на плейтесте окажется проблемой — можно добавить опцию возврата на исходную позицию.

---

## Взаимодействие с другими системами

| Система                                                                                          | Связь                                                                                                                    |
| ------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| [@ id="/Pages/Private/Chemical Synthesis Puzzle.md" label="Chemical Synthesis Puzzle"]           | Источник запуска: UnityEvent при решении пазла вызывает `Play()`                                                          |
| [@ id="/Pages/Private/Save System.md" label="Save System"]                                       | `EarthquakeFOVPulse` — `ISaveable` (`chemical_puzzle_earthquake`): сохраняется только факт проигрывания                   |
| [@ id="/Pages/Private/Game Events.md" label="Game Events"]                                       | Статические события `ShakeStarted`/`ShakeEnded` — C#-альтернатива GameEvent-ассетам, подписка из кода без Inspector-настройки |
