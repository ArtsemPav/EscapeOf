<!-- version: 1 -->

# Progress Logger System

Локальная телеметрия для альфа-теста: события игры пишутся в **человекочитаемый txt-файл**. **Никакой сети** — файл собирается с тестеров вручную. Смертей в игре нет, «спотыкания» читаются по времени и неудачным попыткам.

---

## Формат лога

Одна строка = одно событие, `ЧЧ:мм:сс | событие | детали`:

```
14:02:11 | session_start | 0.1.0-alpha
14:02:12 | scene_loaded | Game
14:03:41 | room_enter | Room 3
14:04:02 | puzzle_failed | code_lock_room3: код 3719
14:04:38 | puzzle_exit_unsolved | code_lock_room3
14:05:19 | room_enter | Room 3
14:06:47 | puzzle_solved | code_lock_room3
14:06:47 | inventory_changed | Ключ, Фонарик, руны: красная
14:07:30 | game_saved | сохранение
```

Читается Блокнотом, парсинг не нужен.

## Размещение

- Скрипт: `Assets/Scripts/Core/ProgressLogger.cs`
- В сцене: компонент на `GameManager` в `Game.unity` (DontDestroyOnLoad)
- Файл: `progress_log.txt`; ротация: при превышении 2 МБ → `progress_log.old.txt`
- **В билде** — рядом с `.exe` (в папке игры). Если папка игры запрещена к записи (Program Files) — fallback в системную папку.
- **В редакторе** — в `persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Escape\`).
- Точный путь печатается в консоль при старте: `[ProgressLogger] Log file: ...` (виден в `Player.log`).

---

## Производительность

Все записи — **в фоновом потоке**:

- Пути (`persistentDataPath`) резолвятся **один раз в `Awake` на главном потоке** — из фонового потока Unity API вызывать нельзя (иначе каждая запись падает: `get_persistentDataPath can only be called from the main thread`).
- Игровой поток: формат строки в `StringBuilder` + вставка в `BlockingCollection` (512 слотов).
- Диск: выделенный поток `ProgressLogger.Writer` пишет и ротирует файл сам.
- Переполнение очереди → событие выбрасывается, игра НЕ фризит.
- При выходе: потоку 100 мс на дозапись, затем бросается (`IsBackground`).

---

## События


| Событие                | Когда                                    | Детали                                             |
| ---------------------- | ---------------------------------------- | -------------------------------------------------- |
| `session_start`        | запуск игры                              | версия                                             |
| `scene_loaded`         | каждая загрузка сцены                    | имя сцены                                          |
| `room_enter`           | игрок физически вошёл в комнату          | имя комнаты                                        |
| `inventory_changed`    | изменение инвентаря                      | имена предметов + руны                             |
| `puzzle_solved`        | решение паззла                           | SaveId                                             |
| `puzzle_failed`        | неудачная попытка                        | SaveId + ввод (код и т.п.)                         |
| `puzzle_exit_unsolved` | закрыл паззл, не решив                   | SaveId                                             |
| `drag_end`             | отпустил драгбл-объект — чем закончилось | `ящик/дверь <имя>: открыт/закрыт/промежуточно (%)` |
| `drag_locked`          | тянет запертый ящик/дверь                | имя объекта                                        |
| `game_saved`           | игрок сохранился                         | —                                                  |


## Как читать спотыкания

- **Dwell-время комнаты** — интервал между `room_enter` и следующим `puzzle_solved`/`room_enter`.
- **Серия `puzzle_failed**` с разными кодами — перебор без понимания.
- `**puzzle_exit_unsolved` + возврат** — ушёл искать подсказку; **без возврата** — паззл-стена, игра закончилась здесь.

## Источники

- **Комнаты (визиты)** — `RoomVisibilityManager.CurrentRoomChanged` (физическое положение игрока).
- **Инвентарь** — `InventorySystem.OnInventoryStateChanged` (все точки изменения, включая руны; дубликаты отфильтрованы).
- **Сохранения** — `SaveManager.OnSaved` → `game_saved` (маркер на таймлайне).
- **puzzle_solved** — через `PuzzleModeController` (все базовые паззлы) + автономные: Board → `board_puzzle`, Fifteen → `_saveId`, Metamorf → `metamorf_puzzle`, финальная дверь → `final_door_phase1_medallions`, `final_door_phase2_scull_complete`.
- **puzzle_failed** — `CodeLockUI.ErrorRoutine` (неверный код), `ElectricPuzzleController` (рывок рычага на неверной схеме).
- **puzzle_exit_unsolved** — `PuzzleModeController.ExitPuzzleMode` при `!_isSolved`.
- **drag_end / drag_locked** — `DrawerDrag.OnDragEnd` / `DoorInteraction` (`OnDragEnd`, `ToggleClick`): открылся ли объект, попытки открыть запертое.

### Новый паззл — чек-лист

1. На `PuzzleModeController` — логирование решения бесплатное (`_saveId` попадает в лог).
2. Автономный паззл — добавить `ProgressLogger.Log("puzzle_solved", "<id>")` в точку решения; при наличии «неверных попыток» — `puzzle_failed`.
3. Точка закрытия паззла не решив — `puzzle_exit_unsolved` (или общий выход, если паззл на контроллере).

---

## Сброс при отладке

`Tools → Escape → Reset Save Progress` (Editor, вне Play Mode) удаляет сейв слота 0 с бэкапами и `progress_log.txt` / `progress_log.old.txt`.

---

## Связанные системы

- **Idle-музыка** (`IdleMusicTrigger` на `GameManager`): если игрок `_idleThreshold` секунд (480 = 8 минут) не решил ни одной загадки и не открыл ни одной двери, просит `MusicDirector` включить подсказывающий трек (`Idle_Hint` = `S_Abbys.aif`); первый же прогресс возвращает дефолтный трек. Подписки: `ProgressLogger.PuzzleSolved` (событие над `puzzle_solved`) и `DoorInteraction.DoorOpened` (статическое событие, поднимается один раз на открытие двери, перезаряжается при закрытии). Cue включается только поверх дефолтного трека; если при истечении порога играет другая музыка — таймер перезапускается; срабатывает один раз за сессию; вход в паззл-режим (`PuzzleModeController.PuzzleModeStarted/Ended`) уводит cue и обнуляет отсчёт. Таймер на unscaled-времени, на паузе не тикает. Интервал меняется полем `_idleThreshold` в инспекторе.