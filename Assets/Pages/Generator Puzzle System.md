## Обзор

Загадка «Генератор». Игрок устанавливает два предмета (свечу зажигания и канистру с топливом) из инвентарного бара на свои якоря генератора: свеча появляется статично, канистра проигрывает анимацию заливки. После установки обоих предметов нажатие кнопки (Cylinder) запускает timing-мини-игру: ползунок ходит по полоске, и нужно заданное число раз подряд попасть ЛКМ в зелёную зону. Успех включает генератор: VFX дыма, постоянная тряска, зацикленный звук двигателя и питание (`LightingSystem.SetGeneratorReady(true)`).

## Компоненты

### `GeneratorPuzzleController`

Файл: `Assets/Scripts/Puzzle/Generator/GeneratorPuzzleController.cs`. Размещается на корневом объекте загадки. Реализует `IPuzzleDropHandler`, `IPuzzleDropTarget`, `IPuzzleExitGuard`, `ISaveable`. `[DefaultExecutionOrder(-7)]`.

**Save Settings**

| Поле      | Описание                                                                       | Default              |
| --------- | ---------------------------------------------------------------------------------- | ---------------------- |
| `Save ID` | Стабильный уникальный ID сохранения. Не менять после назначения                     | `"generator_puzzle"`   |

**References**

| Поле             | Описание                                                          | Default                  |
| ---------------- | ----------------------------------------------------------------------- | -------------------------- |
| `Controller`     | `PuzzleModeController` режима загадки                                    | `GetComponent` в Awake     |
| `Minigame`       | Компонент `GeneratorTimingMinigame`                                       | —                          |
| `Minigame Panel` | Корневой UI мини-игры в Canvas сцены (выключен по умолчанию)              | —                          |

**Hit VFX**

| Поле     | Описание                                             | Default |
| -------- | ---------------------------------------------------------- | ------- |
| `Hit Vfx`| Particle System при каждом попадании в зелёную зону        | —       |

**Audio**

| Полe                   | Описание                                                  | Default |
| ------------------------ | --------------------------------------------------------------- | ------- |
| `Success Clip`           | Звук попадания в зелёную зону                                   | —       |
| `Fail Clip`              | Звук промаха                                                    | —       |
| `Generator Start Clip`   | Звук старта генератора (проигрывается после мини-игры)          | —       |
| `Success Volume`         | Громкость попадания (0–1)                                        | `1`     |
| `Fail Volume`            | Громкость промаха (0–1)                                          | `1`     |
| `Generator Start Volume` | Громкость старта (0–1)                                           | `1`     |

**Completion Effects**

| Полe                 | Описание                                                                | Default |
| ---------------------- | ------------------------------------------------------------------------------- | ------- |
| `Completion Vfx`       | Particle System работающего генератора (дым); включается при победе              | —       |
| `Generator Shake`      | `ObjectShake` генератора — постоянное дрожание после победы                      | —       |
| `Generator Loop Audio` | `LoopAudioController` — зацикленный звук двигателя после звука старта            | —       |

**Hit Shake** — амплитуды импульсной тряски генератора при попадании (`Position` `0.05` м / `Rotation` `2°`, длительность 2 с) и при промахе (`0.08` м / `3°`, длительность 0.5 с).

**Start Button**

| Поле           | Описание                                                                                      | Default |
| ---------------- | --------------------------------------------------------------------------------------------------- | ------- |
| `Start Button`   | Кнопка запуска мини-игры (Cylinder с `ButtonPressAnimation`). Активна только когда все предметы установлены | —       |

**Drop Slots** — массив слотов `GeneratorDropSlot`:

| Поле                    | Описание                                                                              | Default     |
| ------------------------- | --------------------------------------------------------------------------------------------- | ------------- |
| `Item`                    | Предмет для слота (`ItemData` — свеча, канистра)                                                | —             |
| `Anchor Collider`         | Collider якоря — цель рейкаста при отпускании предмета                                          | —             |
| `Anchor Transform`        | Точка спавна визуала / ghost-превью                                                             | —             |
| `Pour Spawn Transform`    | Отдельная точка для анимации заливки; если пусто — `Anchor Transform`                           | —             |
| `Placed Prefab`           | Prefab вставленного предмета; если пусто — `inspectionPrefab` предмета                          | —             |
| `Insert Clip` / `Volume`  | Звук установки предмета и громкость                                                             | — / `1`       |
| `Play Pour Animation`     | Включить анимацию заливки через Animator вместо статичного спавна                               | `false`       |
| `Pour Anim Trigger`       | Имя trigger-параметра Animator для запуска заливки                                              | `"Pour"`      |
| `Pour State Name`         | Имя стейта анимации заливки (для отслеживания завершения)                                       | `"Pour"`      |
| `Pour Exit State Name`    | Имя пост-стейта; если задано — ждём его завершения перед удалением/заморозкой                   | пусто         |
| `Keep After Pour`         | `true` — предмет остаётся на якоре; `false` — удаляется (топливо израсходовано)                 | `false`       |
| `Pour Clip` / `Volume`    | Звук заливки и громкость (играет в начале анимации)                                             | — / `1`       |
| `Pour Effect`             | `GasolinePourEffect` — струя бензина, синхронизирована с анимацией                              | —             |

**Common**

| Полe            | Описание                                                          | Default                      |
| ----------------- | ----------------------------------------------------------------------- | ------------------------------ |
| `Anchor Layer`    | LayerMask якорей дропа для Raycast                                       | —                              |
| `Ghost Material`  | Материал ghost-превью; если пуст — `Materials/CardLock/CardLamp_Ghost.mat` из Resources | автозагрузка |
| `Drop Hint`       | Подсказка при наведении предмета на якорь                                | `"Установить в генератор"`     |

**Missing Item Hints** — подсказки по нажатию кнопки при неполной установке: `Hint No Items` («Установите оба предмета в генератор.»), `Hint Only First Item` / `Hint Only Second Item` — в зависимости от того, какой слот заполнен.

### `GeneratorTimingMinigame`

Файл: `Assets/Scripts/Puzzle/Generator/GeneratorTimingMinigame.cs`. Timing-мини-игра «поймай зону». Использует `Time.unscaledDeltaTime` (не зависит от `Time.timeScale`).

**Green Zone Shrink**

| Полe                  | Описание                                                                  | Default |
| ----------------------- | --------------------------------------------------------------------------------- | ------- |
| `Shrink Factor`         | Множитель ширины зелёной зоны после каждого попадания (0.8 = −20%)                 | `0.8`   |
| `Min Green Zone Width`  | Минимальная ширина зоны в пикселях, ниже которой она не уменьшается                | `20`    |

**Green Zone Error Color** — цвета зоны: `Green Zone Error Color` (красный при промахе), `Green Zone Warning Color` (оранжевый, переходный), `Green Zone Normal Color` (зелёный), `Green Zone Transition Duration` (полный переход красный → оранжевый → зелёный, `0.6` с).

**UI (дети полоски _track)**

| Полe          | Описание                                                                    | Default |
| --------------- | --------------------------------------------------------------------------------- | ------- |
| `Track`         | Фон полоски. Pivot должен быть (0.5, 0.5)                                          | —       |
| `Green Zone`    | Зона, спозиционированная через anchoredPosition относительно центра Track          | —       |
| `Handle`        | Движущийся ползунок                                                                | —       |

**Settings**

| Полe                 | Описание                                             | Default |
| ----------------------- | ------------------------------------------------------------- | ------- |
| `Speed`                 | Полных проходов ползунка по полоске в секунду                 | `0.8`   |
| `Required Successes`    | Сколько попаданий **подряд** нужно для завершения             | `3`     |

**Feedback — Progress Lamps**: `Progress Lamps` (массив `Image`) — лампочки загораются по одной за каждое попадание подряд и гаснут при промахе; цвета `Lamp On Color` (зелёный) / `Lamp Off Color` (тёмно-серый).

**Feedback — Handle Flash**: `Handle Image` ползунка вспыхивает `Handle Hit Color` (зелёный) при попадании или `Handle Miss Color` (красный) при промахе и возвращается к `Handle Default Color` за `Flash Duration` (`0.2` с).

События: `OnCompleted`, `OnHit`, `OnMiss`. Свойства `SuccessStreak` и `RequiredSuccesses` — для UI-индикации.

---

## Логика работы

```
Установка предмета
  → Игрок перетаскивает предмет из PuzzleInventoryBar
  → Update(): пока предмет тащится — ghost-превью на якоре при наведении
  → HandleDrop(): рейкаст по Anchor Layer — попадание в Anchor Collider нужного слота?
      → предмет помечается установленным ДО анимации → SaveManager.Save()
      → слот без анимации заливки (свеча): SpawnPlacedVisual + звук установки
      → слот с заливкой (канистра): PourAnimationRoutine()
          → спавн канистры (коллайдеры отключены) → Animator.SetTrigger("Pour")
          → звук установки → звук заливки + GasolinePourEffect
          → ожидание завершения стейта Pour (и пост-стейта, если задан)
          → Keep After Pour? остаётся : Destroy (канистра израсходована)
  → предмет удаляется из инвентаря (replacement = null)
  → все предметы на местах → _allItemsReady = true

Нажатие кнопки Cylinder (ButtonPressAnimation.OnPressed)
  → все предметы установлены?
      → да: панель мини-игры показывается → StartMinigame()
      → нет: PopupMessageSystem показывает подсказку (какой предмет не хватает)

Мини-игра (GeneratorTimingMinigame)
  → ползунок PingPong по полоске со скоростью _speed
  → ЛКМ → Evaluate():
      попадание в зелёную зону → серия++, UpdateLamps, вспышка ползунка,
          ShrinkGreenZone (× Shrink Factor) + RelocateGreenZone (случайная позиция)
          → серия достигла _requiredSuccesses → OnCompleted
      промах → серия = 0, ширина зоны восстанавливается, зона перемещается,
          лампа гаснет, красная вспышка + переход цвета, OnMiss

OnCompleted → HandleMinigameCompleted()
  → панель мини-игры скрывается, _isSolved = true
  → Completion Vfx (дым) включается, постоянная тряска генератора
  → LightingSystem.SetGeneratorReady(true)
  → _controller.SetSolved() — выход из режима загадки + сохранение
  → StartGeneratorAudioSequence(): ждём Success Clip → Generator Start Clip
      → LoopAudioController.StartLoop() — звук работающего двигателя

OnHit / OnMiss (во время мини-игры)
  → звуки Success Clip / Fail Clip (PlaySFXExclusive)
  → Hit Vfx при попадании
  → импульсная тряска генератора с настройками Hit/Miss Shake
```

---

## Быстрая настройка в сцене

1. Соберите генератор: корень с `PuzzleInteractable` + `PuzzleModeController` + `GeneratorPuzzleController`.
2. Создайте два слота в **Drop Slots**: свеча (`Play Pour Animation` выключен) и канистра (`Play Pour Animation` включен, `Keep After Pour` выключен). Для каждого назначьте **Item**, **Anchor Collider**, **Anchor Transform** и prefab-визуал.
3. Для канистры настройте Animator: trigger `Pour`, стейт `Pour` (и опционально пост-стейт в `Pour Exit State Name`), назначьте **Pour Clip** и **Pour Effect** (`GasolinePourEffect`).
4. Создайте кнопку запуска (Cylinder) с `ButtonPressAnimation` и назначьте её в **Start Button**.
5. Добавьте панель мини-игры в Canvas сцены: **Track** (pivot 0.5, 0.5) с детьми **Green Zone** и **Handle**, назначьте всё в `GeneratorTimingMinigame`, а компонент и панель — в **Minigame** / **Minigame Panel** контроллера. Панель должна быть выключена по умолчанию.
6. Настройте лампоочки прогресса (**Progress Lamps**) и цвета вспышек/зоны.
7. Назначьте звуки (**Success Clip**, **Fail Clip**, **Generator Start Clip**) и эффекты (**Hit Vfx**, **Completion Vfx**, **Generator Shake**, **Generator Loop Audio**).
8. Установите уникальный **Save ID**. Слой якорей должен входить в **Anchor Layer**.

---

## Важные детали

- Предмет помечается установленным **до** анимации заливки и сразу сохраняется — сбой посреди анимации не теряет прогресс; при загрузке визуал восстанавливается только для статичных слотов и слотов с `Keep After Pour` (канистра считается израсходованной).
- Выход из режима загадки **блокируется** во время анимации заливки (`CanExitPuzzle` → `!_isProcessing`, интерфейс `IPuzzleExitGuard`).
- Мини-игра запускается **только** через кнопку; повторный запуск после победы или во время обработки блокирован (`_allItemsReady` сбрасывается после старта).
- Попадания требуют **серии без промахов**: любой промах обнуляет счётчик и возвращает зоне исходную ширину, но зона перемещается в обоих случаях.
- Мини-игра движется на `unscaledDeltaTime` — работает корректно при остановленном `Time.timeScale`.
- Звуковая цепочка победы строго последовательна: Success Clip → Generator Start Clip → зацикленный Loop Audio (каждый шаг ждёт окончания предыдущего клипа).
- `LightingSystem.SetGeneratorReady(true)` — точка интеграции с системой освещения: после победы генератор считается источником питания.
- Аудио попадания/промаха играется через `PlaySFXExclusive` — новый звук перебивает предыдущий, звуки не наслаиваются.
- Ghost-превью пересоздаётся при смене перетаскиваемого предмета и скрывается при выходе из режима загадки.
- Существует редакторный чит `Assets/Scripts/Editor/PuzzleCheats/GeneratorPuzzleUnlockTool.cs` для мгновенного решения.
