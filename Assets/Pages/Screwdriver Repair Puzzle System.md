## Обзор

Загадка "Ремонт отвёрткой". Игрок в режиме загадки перетаскивает отвёртку из инвентаря на объект. Объект запускает зацикленную анимацию ремонта, а на экране появляется круговая QTE-мини-игра «Skill Check» (`ScrewdriverMinigamePanel` — общая с системой мини-игр, см. [@ id="/Pages/Private/Screwdriver Minigame System.md" label="Screwdriver Minigame System"]). При полном заполнении прогресса объект проигрывает финальную анимацию, отвёртка расходуется и загадка считается решённой. При превышении лимита промахов мини-игра проваливается (`OnFailed`) — ремонт прерывается и игрок может повторить попытку.

Структура повторяет [@ id="/Pages/Private/Nursery Lock Puzzle System.md" label="Nursery Lock Puzzle System"]: перетаскивание предмета из `PuzzleInventoryBar`, самодостаточный контроллер загадки, отдельная UI-панель мини-игры в Canvas сцены.

---

## Компоненты

### `ScrewdriverRepairController`

Файл: `Assets/Scripts/Puzzle/ScrewdriverRepair/ScrewdriverRepairController.cs`. Размещается на корневом объекте загадки. Требует `PuzzleModeController` на том же объекте. Реализует `IPuzzleDropHandler`, `IPuzzleDropTarget`, `IPuzzleExitGuard`, `ISaveable` (`_saveId = "screwdriver_repair_puzzle"`).

**Save**


| Поле      | Описание                                                                      |
| --------- | ----------------------------------------------------------------------------- |
| `Save ID` | Уникальный ID для системы сохранений (default: `"screwdriver_repair_puzzle"`) |


**Item**


| Поле            | Описание                                                |
| --------------- | ------------------------------------------------------- |
| `Required Item` | `ItemData` отвёртки, которую нужно перетащить на объект |


**Auto-Resolved** (переопределите только при нестандартной структуре)


| Поле              | Описание                                   |
| ----------------- | ------------------------------------------ |
| `Target Collider` | Collider объекта — цель рейкаста при дропе |
| `Target Animator` | Animator ремонтируемого объекта            |


**Ghost Preview**


| Поле                | Описание                                                                                            |
| ------------------- | --------------------------------------------------------------------------------------------------- |
| `Ghost Material`    | Материал ghost-превью. Если пуст — загружается `Materials/CardLock/CardLamp_Ghost.mat` из Resources |
| `Ghost Prefab`      | Prefab ghost-превью. Если пуст — используется `inspectionPrefab` из `Required Item`                 |
| `Ghost Spawn Point` | Точка спавна ghost. Если пуст — позиция `Target Collider`                                           |


**Drop Hint**


| Поле        | Описание                                                   |
| ----------- | ---------------------------------------------------------- |
| `Drop Hint` | Подсказка при наведении отвёртки («Использовать отвёртку») |


**Minigame**


| Поле             | Описание                                                                      |
| ---------------- | ----------------------------------------------------------------------------- |
| `Minigame Panel` | Панель UI мини-игры в Canvas сцены (с компонентом `ScrewdriverMinigamePanel`) |
| `Minigame`       | Компонент `ScrewdriverMinigamePanel` (если пуст — берётся с панели)           |


**Audio**: `Repair Start Clip` (также играет в полгромкости при каждом попадании в сектор) и `Repair Complete Clip` — с раздельной громкостью.

### `ScrewdriverMinigamePanel`

Общая мини-игра «Skill Check» — детали в [@ id="/Pages/Private/Screwdriver Minigame System.md" label="Screwdriver Minigame System"]. Контроллер подписывается на её события: `OnCompleted`, `OnHit`, `OnMiss`, `OnFailed`.

---

## Логика работы

```
Игрок перетаскивает отвёртку из PuzzleInventoryBar на объект
  → HandleDrop() — рейкаст по Target Collider; replacement = item (не расходуется до победы)
  → PuzzleInventoryBar скрывается
  → RepairSequence():
      → звук ремонта, Animator.SetBool("IsRepairing", true) — цикл анимации
      → панель мини-игры активируется, через кадр StartMinigame()

Мини-игра (ScrewdriverMinigamePanel — каждый чек)
  → Стрелка вращается со случайной скоростью/направлением
  → Space/ЛКМ:
      попадание в белую зону → обычный бонус прогресса (прогресс-бар вспыхивает белым)
      попадание в серую зону → меньший бонус
      промах (мимо сектора или полный оборот) → _misses++ (опциональный откат прогресса)
  → Прогресс достиг цели → OnCompleted
  → Промахов больше лимита (_maxMisses) → OnFailed

OnFailed → FailureSequence()
  → StopMinigame(), панель скрывается
  → Animator.SetBool("IsRepairing", false)
  → _isProcessing = false, PuzzleInventoryBar.Show(this)
  → игрок может перетащить отвёртку снова

OnCompleted → CompletionSequence()
  → StopMinigame(), панель скрывается
  → звук завершения
  → Animator.SetBool("IsRepairing", false) + SetTrigger("RepairComplete")
  → Ждём входа в стейт "Repaired" и normalizedTime >= 1
  → _isSolved = true; отвёртка расходуется (InventorySystem.RemoveItem)
  → SaveManager.Save() → SetSolved() — возврат управления игроку
```

---

## Animator Controller

Ремонтируемый объект должен иметь `Animator` со стейтами и параметрами:


| Параметр         | Тип     | Назначение                                     |
| ---------------- | ------- | ---------------------------------------------- |
| `IsRepairing`    | Bool    | Включает цикл анимации ремонта (looping state) |
| `RepairComplete` | Trigger | Запускает финальную анимацию                   |


Финальный стейт должен называться `**Repaired**` — контроллер ждёт входа в этот стейт и `normalizedTime >= 1` перед тем как пометить загадку решённой.

---

## Быстрая настройка в сцене

1. Разместите объект с `PuzzleInteractable` + `PuzzleModeController` + `ScrewdriverRepairController` на корне загадки.
2. Настройте `Animator` ремонтируемого объекта: параметры `IsRepairing` (Bool), `RepairComplete` (Trigger), стейт `Repaired`.
3. Панель `ScrewdriverMinigamePanel` в Canvas сцены (`Game.unity`) назначьте в `Minigame Panel`.
4. На `ScrewdriverRepairController` назначьте **Required Item** — `ItemData` отвёртки.
5. Установите уникальный **Save ID** на `PuzzleModeController` и на `ScrewdriverRepairController`.
6. На `ScrewdriverMinigamePanel` настройте параметры мини-игры (скорость стрелки, сектор, прогресс, лимит промахов).
7. (Опционально) Назначьте звуки ремонта/завершения.

Остальные ссылки (Collider, Animator, ghost) находятся автоматически среди дочерних объектов.

---

## Важные детали

- Ghost-превью отвёртки создаётся из `_ghostPrefab` (или `inspectionPrefab` предмета), коллайдеры отключаются, материал — `CardLamp_Ghost.mat`.
- Отвёртка **расходуется при успехе**: в `HandleDrop` возвращается через `replacement` (не расходуется во время мини-игры), но в `CompletionSequence` удаляется из инвентаря через `InventorySystem.RemoveItem`.
- При провале (`OnFailed`) ремонт прерывается (`FailureSequence`), инвентарь-бар возвращается — попытку можно повторить.
- Панель мини-игры не создаётся и не уничтожается — активируется/деактивируется через `SetActive`.
- Выход из режима загадки: разрешён во время мини-игры (Esc прерывает ремонт — `HandlePuzzleExited` гасит `IsRepairing`, останавливает мини-игру и корутины), но **заблокирован** во время финальной анимации (`CanExitPuzzle()` возвращает `!_isCompleting`).
- Сохраняется только `_isSolved` — прогресс текущей попытки ремонта не персистентен между сессиями.
- При загрузке решённой загадки (`RestoreSolvedState`) аниматор перематывается в конец `Repaired` (`Play(state, 0, 1f)`).
