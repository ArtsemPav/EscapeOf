## Nursery Lock Puzzle System

Загадка "Взлом замка отмычкой". Игрок заходит в режим загадки, перетаскивает отмычку из инвентаря на замок, видит ghost-превью, анимацию вставки отмычки, 2D мини-игру с концентрическими кольцами и анимацию открытия дверей шкафа.

### Структура префаба

```
NurseryLock                           # PuzzleInteractable (вход в режим загадки)
├── LockPick                          # Группа объектов замка
│   └── Lockpick                      # 3D-модель отмычки (MeshRenderer, управляется Animator)
├── MedRack_V2                        # Корпус шкафа
│   ├── MedRackDoor_L                 # DoorInteraction (левая дверь)
│   └── MedRackDoor_R                 # DoorInteraction (правая дверь)
└── (на корне)                        # PuzzleModeController + NurseryLockController
```

Панель мини-игры `LockPickMinigamePanel` живёт в Canvas сцены (не в префабе) и назначается в поле `_minigamePanel` контроллера.

### Animator Controller

Ассет: `Assets/Animation/NurseLock/NurseLock.controller`

```
Idle ──[InsertLockpick]──> Inserting ──[Has Exit Time]──> LockPickIdle ──[OpenLock]──> opening
```


| Параметр         | Тип     | Назначение                      |
| ---------------- | ------- | ------------------------------- |
| `InsertLockpick` | Trigger | Запуск анимации вставки отмычки |
| `OpenLock`       | Trigger | Запуск анимации открытия замка  |


**Важно:** `Idle.anim` анимирует `m_IsActive = 0` на `LockPick/Lockpick` — выключает GameObject отмычки каждый кадр. Клипы `Inserting.anim` и `LockPickIdle.anim` не содержат кривой `m_IsActive`, поэтому `NurseryLockController` в `LateUpdate` принудительно активирует отмычку через флаг `_forceLockpickVisible`.

### Логика работы

```
Игрок нажимает E на замок
  → PuzzleInteractable → PuzzleModeController.EnterPuzzleMode()
  → Камера переключается на крупный план
  → Открывается PuzzleInventoryBar с отмычкой

Игрок начинает перетаскивать отмычку
  → Создаётся ghost-копия отмычки (полупрозрачная, отдельный GameObject)
  → Ghost виден на протяжении всего перетаскивания
  → Ghost не подвластен Animator

Игрок отпускает отмычку на замке (рейкаст по коллайдеру)
  → HandleDrop() возвращает true (Save() с lockpickInserted=true)
  → Ghost скрывается
  → PuzzleInventoryBar скрывается
  → _forceLockpickVisible = true (LateUpdate держит отмычку видимой)
  → Animator.SetTrigger("InsertLockpick")
  → Wait Until Animator reaches Inserting state
  → Wait Until Animator transitions to LockPickIdle state
  → Запуск мини-игры

Мини-игра (LockPickMinigame)
  → Веерное появление колец (scale 0 → 1, от внешнего к внутреннему)
  → Стрелка-ориентир появляется последней
  → Вращение колец, игрок нажимает Space / ЛКМ
  → Попадание: кольцо блокируется (зелёный цвет)
  → Промах: кольцо краснеет и увеличивается, затем возвращается к норме
  → Промах на кольце N > 0: откат на кольцо N-1
  → Все кольца заблокированы:
      → Веерное сжатие колец (scale 1 → 0, от внешнего к внутреннему)
      → Стрелка исчезает первой
      → OnCompleted.Invoke()

OnCompleted → OpeningSequence()
  → _forceLockpickVisible = false (opening.anim сама управляет видимостью)
  → StopMinigame() + панель скрывается
  → AudioManager.PlaySFX(_openClip)
  → Animator.SetTrigger("OpenLock")
  → Wait Until Animator reaches "opening" state
  → Wait Until opening animation finishes (normalizedTime >= 1)
  → Расходуется отмычка (InventorySystem.RemoveItem)
  → DoorInteraction.UnlockAndOpen() на обеих дверях
  → _isSolved = true → SaveManager.Save() → SetSolved() — возврат управления игроку
```

### Мини-игра: кольца

3 концентрических кольца вращаются с разной скоростью. Задача — остановить каждое кольцо нажатием Space или ЛКМ, когда его засечка совпадает со стрелкой-ориентиром наверху (внешнее статичное кольцо `gearMain`).


| Кольцо | Скорость | Направление    |
| ------ | -------- | -------------- |
| R1     | 80 °/с   | по часовой     |
| R2     | 110 °/с  | против часовой |
| R3     | 140 °/с  | по часовой     |


Допустимая погрешность — 8°. Промах вызывает откат на 1 кольцо назад (кроме первого). Отмычка расходуется только при победе. Ввод: `Space` или ЛКМ (`Keyboard.current.spaceKey.wasPressedThisFrame` / `Mouse.current.leftButton.wasPressedThisFrame`).

### Веерные анимации

**Появление (`AnimateAppearance`):**

- Все кольца стартуют в `scale = 0`
- Кольца последовательно вырастают от 0 → 1, от внешнего (R1) к внутреннему (R3)
- Задержка между кольцами — `_ringStaggerDelay`
- Стрелка-ориентир появляется после всех колец
- Ввод заблокирован до завершения анимации

**Сжатие (`AnimateCompletion`):**

- Запускается при победе вместо мгновенного `OnCompleted`
- Стрелка исчезает первой
- Кольца последовательно сжимаются 1 → 0, от внешнего к внутреннему
- `OnCompleted` вызывается только после полного сжатия

**Реакция на промах (`FlashMissCoroutine`):**

- Кольцо плавно увеличивается (`_missScaleMultiplier`) и краснеет (`_missColor`)
- Первые 30% времени — нарастание, оставшиеся 70% — возврат к норме
- Кольца продолжают вращаться во время эффекта

Все анимации используют `Time.unscaledDeltaTime` — не зависят от `Time.timeScale`.

### Параметры инспектора

**Per-Instance Setup**


| Поле             | Описание                                              |
| ---------------- | ----------------------------------------------------- |
| `Save ID`        | Уникальный ID для системы сохранений (default: `"nursery_lock_puzzle"`) |
| `Required Item`  | `ItemData` отмычки, которую нужно перетащить на замок |
| `Minigame Panel` | Панель UI мини-игры в Canvas сцены                    |


**Auto-Resolved** (переопределите только при нестандартной структуре)


| Поле                | Описание                                 |
| ------------------- | ---------------------------------------- |
| `Lock Collider`     | Collider замка — цель рейкаста при дропе |
| `Lock Animator`     | Animator замка                           |
| `Lockpick Renderer` | MeshRenderer 3D-модели отмычки           |
| `Ghost Material`    | Material для ghost-превью отмычки (если null — создаётся программно) |
| `Minigame`          | Компонент `LockPickMinigame` на панели (если null — берётся с `_minigamePanel`) |
| `Door Left/Right`   | DoorInteraction дверей шкафа             |


**Audio**


| Поле            | Описание                 |
| --------------- | ------------------------ |
| `Insert Clip`   | Звук вставки отмычки     |
| `Open Clip`     | Звук открытия замка      |
| `Insert Volume` | Громкость вставки (0–1)  |
| `Open Volume`   | Громкость открытия (0–1) |


**Minigame — Ring Configuration**

Каждое кольцо настраивается отдельно (`RingConfig`, массив `_ringConfigs`; default — 3 кольца):


| Поле            | Описание                                                            |
| --------------- | ------------------------------------------------------------------- |
| `Speed`         | Скорость вращения в градусах в секунду (default: 80 / 110 / 140)    |
| `Clockwise`     | Направление: true — по часовой (default: по часовой, против, по часовой) |
| `Sprite`        | Спрайт кольца с засечкой в верхней части                            |
| `Relative Size` | Размер кольца относительно контейнера (0.05–2; default: 0.72 / 0.52 / 0.35) |
| `Locked Color`  | Тинт кольца после успешной блокировки (default: светло-зелёный)     |


**Minigame — Decorative Gears**

Декоративные шестерёнки по бокам панели вращаются для атмосферы (`DecorGearConfig`, массив `_decorGears`, default — 3 шт): `Sprite`, `Speed` (40/55/70 °/с), `Clockwise`, `Anchored Position` (пиксели относительно центра) и `Relative Size`. Создаются/анимируются автоматически.

**Minigame — Settings**


| Поле             | Описание                                   |
| ---------------- | ------------------------------------------ |
| `Tolerance`      | Допустимая погрешность попадания (default: 8°) |
| `Container Size` | Размер области колец (пиксели, default: 400) |
| `Active Color`   | Тинт активного кольца (default: тёплый жёлтый) |


**Minigame — Static Sprites / Visuals (автосоздание)**


| Поле                            | Описание                                                        |
| ------------------------------- | --------------------------------------------------------------- |
| `Gear Main Sprite`              | Внешнее статичное кольцо со стрелкой-ориентиром (default size 0.95) |
| `Center Sprite`                 | Центральный статичный элемент (default size 0.18)               |
| `Ring Container` / `Gear Main` / `Center` / `Ring Transforms` / `Images` | RectTransform'ы и Image'и. Если не назначены — создаются автоматически в рантайме |


**Minigame — Miss Feedback**


| Поле                    | Описание                             |
| ----------------------- | ------------------------------------ |
| `Miss Flash Duration`   | Длительность реакции на промах (сек) |
| `Miss Scale Multiplier` | Во сколько раз увеличивается кольцо  |
| `Miss Color`            | Цвет кольца при промахе              |


**Minigame — Appearance / Completion Animation**


| Поле                 | Описание                                           |
| -------------------- | -------------------------------------------------- |
| `Ring Anim Duration` | Длительность масштабирования одного кольца (сек)   |
| `Ring Stagger Delay` | Веерная задержка между кольцами (сек)              |
| `Ring Anim Curve`    | Кривая плавности анимации (EaseInOut по умолчанию) |


**Minigame — Audio**


| Поле            | Описание                               |
| --------------- | -------------------------------------- |
| `Success Clip`  | Звук успешной блокировки кольца        |
| `Fail Clip`     | Звук промаха                           |
| `Complete Clip` | Звук победы (все кольца заблокированы) |
| `Volume`        | Громкость (0–1)                        |


### Быстрая настройка в сцене

1. Перетащите префаб `Assets/Prefabs/puzzle/NurseryLock/PRF_NurseryLock.prefab` в сцену.
2. На корневом объекте в `NurseryLockController` назначьте:
  - **Required Item** — `ItemData` отмычки.
  - **Minigame Panel** — панель `LockPickMinigamePanel` из Canvas сцены.
3. На `PuzzleModeController` измените **Save ID** на уникальный для каждого экземпляра.
4. На `NurseryLockController` измените **Save ID** на уникальный.
5. (Опционально) Вставьте звуки в поля **Insert Clip**, **Open Clip**.
6. (Опционально) На компоненте `LockPickMinigame` панели назначьте **Success Clip**, **Fail Clip**, **Complete Clip**.

Все остальные ссылки (Animator, Collider, MeshRenderer отмычки, двери) находятся автоматически из дочерних объектов.

### Важные детали

- Отмычка **не расходуется** до победы — `HandleDrop` возвращает её через `replacement` (и только при успехе удаляется в `OpeningSequence`).
- Дроп отклоняется, если мини-игра недоступна (`_minigame == null || _minigamePanel == null`) — защита от софтлока; в этом же случае `CanExitPuzzle` принудительно возвращает `true`.
- При загрузке сейва со вставленной отмычкой (`lockpickInserted = true`, не решено): аниматор перематывается в `LockPickIdle`, при входе в режим загадки мини-игра запускается сразу.
- Ghost-превью — отдельный клонированный GameObject `LockpickGhost`, не подвластный Animator. Создаётся в `CreateGhostObject()`, деактивируется при отпускании.
- `Idle.anim` выключает GameObject отмычки через `m_IsActive = 0`. `LateUpdate` с флагом `_forceLockpickVisible` перебивает это значение, поскольку выполняется после `Animator.Update`.
- `opening.anim` сама управляет `m_IsActive` (1 → 0) — флаг `_forceLockpickVisible` снимается перед триггером `OpenLock`.
- Панель мини-игры не создаётся и не уничтожается — она живёт в Canvas сцены и активируется/деактивируется через `SetActive`. Если ссылка на панель потеряна — ищется единственная `LockPickMinigame` в сцене (включая неактивные) с предупреждением в консоль.
- Контроллер реализует `IPuzzleDropHandler`, `IPuzzleExitGuard`, `ISaveable`.
- Сохраняются: `_isSolved` и `_isLockpickInserted`. При загрузке решённой загадки аниматор перематывается в конец `opening`, двери открываются. При загрузке с вставленной отмычкой — мини-игра запускается сразу.
- Выход из режима загадки заблокирован во время анимации вставки и мини-игры (`CanExitPuzzle` возвращает `!_isProcessing`).
