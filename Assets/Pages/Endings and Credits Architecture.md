<!-- version: 1 -->

# Архитектура концовок и титров

## Решение на текущий момент (до альфа-билда)

В игре **одна концовка** — финальная дверь. Архитектура сознательно не абстрагирована: выносить `GameFlowController` до выяснения, сколько концовок нужно, — рефакторинг в вакууме.

## Реализованная схема

```
FinalDoorPuzzleController.OnSolved
  └── EndGameSequence              (кинематик: doorOpen → прогулка через дверь → взгляд на Death/Angel → fade)
    │   InputManager input off, InteractionUI.SetVisible(false) (HUD/прицел скрыт), курсор залочен
    └── EndGameCreditsView.Show()
          ├── чёрный overlay fade-in (2s)
          ├── CreditsEntry[] по очереди (fade 0.8s → hold 3s → fade out)
          ├── «Спасибо за игру» (hold 5s)
          ├── курсор возвращён (unlock + visible)
          ├── уничтожение всех DDOL-синглтонов (как LoadSaveBtn)
          └── SaveManager.ResetActiveSlot() + DeleteSave(0) → LoadScene("Start")
```

### Компоненты

- `**CreditsEntry**` (`Assets/Scripts/Puzzle/FinalDoor/CreditsEntry.cs`) — ScriptableObject
(Create → EscapeOf → Credits Entry): `_title` (опциональный заголовок) + `_text`.
Тестовые ассеты: `Assets/Data/Credits/Entry_Story|Entry_Designer|Entry_Thanks.asset`.
- `**EndGameCreditsView**` — на `/Canvas/EndGameCredits` в сцене `Game.unity`.
Вся логика на `unscaledDeltaTime` (работает при timeScale=0).
Поля: `_entries` (порядок показа), `_creditsLabel` (общий TMP для записей и финального текста),
`_thanksText`, `_thanksHoldDuration`, `_startSceneName` ("Start"), `_deleteSaveOnFinish` (true).
Панель — последний ребёнок `/Canvas` (рендерится поверх ScreenFader), RectTransform
растянут на весь канвас (sizeDelta 0,0 — иначе текст сжимается в узкую колонку).

### Почему синглтоны уничтожаются перед выходом в меню

`InputManager`, `GameManager` и др. — DontDestroyOnLoad. `EndGameSequence` отключает
карты ввода InputManager'а на время финального кинематика и не возвращает их. Если
просто загрузить Start, DDOL InputManager продолжит держать карты выключенными —
мышь в меню не работает. Поэтому `EndGameCreditsView` перед `LoadScene("Start")`
уничтожает все DDOL-синглтоны (паттерн `LoadSaveBtn`); меню работает на статических
методах `SaveManager` и не требует инстансов, а новая сцена Game создаёт всё заново.

### Поворот доктора (Morrow) — клип TurnFull + синхронизация корня

Клип `MorrowTurnFull.anim` (извлечён из `morowturn.fbx`, `mixamo.com`) содержит
полный поворот бёдер 95°→289° за ~1.6 с. Контроллер `Morrow.controller`:
Turn → (кроссфейд 0.4 s) → Walk. Раньше поворот делал корень (`RootTurnRoutine`),
а из клипа вращение вырезали — фигура замирала в позе и «крутилась статуей».

Финальная схема: клип сам вращает скелет; в момент триггера Walk (кроссфейд
Turn→Walk «разматывает» поворот бёдер) корень твинится синхронно
(`FlipRootDuringWalkRoutine(0.4)` — длительность = кроссфейд): мировое
направление фигуры остаётся постоянным, а корень уже смотрит по направлению
шага. Скорость шага `MorrowWalk`: `_shadowWalkSpeed` (1.45) —paced под темп
клипа; `WalkPace` масштабирует клип так, чтобы доктор дошёл до портала ровно
к финальному аккорду (`_finalChordTime`). Ходьба заканчивается по часам
музыки (или по бюджету `timeToChord` без музыки) — титры не ждут таймаут
`_shadowMaxWalkDuration`.

### Титры: порядок финальных шагов (критичен!)

`FinalFadeAndCreditsRoutine` обязан идти в этой последовательности — иначе
видно сцену сквозь полупрозрачные титры, а камера «доворачивается» на глазах:

1. ScreenFader → полная чернота (`FadeIn`).
2. Под чёрным — непрозрачный фон титров: `EndGameCreditsView.FadeOverlayIn()`
  (фейд на unscaledDeltaTime, идемпотентен — `CreditsRoutine` его не повторяет).
3. Только потом `RestoreBrainBlend()` + `ReleaseCrowdCamera()` — бленд
  Cinemachine обратно к камере игрока скрыт под непрозрачным фоном.
4. `Show()` запускает записи; в конце `ScreenFader.FadeOut` — титры уже на
  собственном чёрном фоне.

Ранняя версия делала Show() → FadeOut фейдера сразу: 0.5–2 с титры были
полупрозрачными над живой сценой, и разворот камеры был виден.

### Почему синглтоны уничтожаются перед выходом в меню

`InputManager`, `GameManager` и др. — DontDestroyOnLoad. `EndGameSequence` отключает
карты ввода InputManager'а на время финального кинематика и не возвращает их. Если
просто загрузить Start, DDOL InputManager продолжит держать карты выключенными —
мышь в меню не работает. Поэтому `EndGameCreditsView` перед `LoadScene("Start")`
уничтожает все DDOL-синглтоны (паттерн `LoadSaveBtn`); меню работает на статических
методах `SaveManager` и не требует инстансов, а новая сцена Game создаёт всё заново.

### Почему сейв удаляется в view (осознанный компромисс)

Логика game flow (удаление сейва, загрузка сцены) живёт внутри `EndGameCreditsView` — по-хорошему
это не задача view. Помечена финальным блоком `CreditsRoutine()`. При рефакторинге переносится целиком.

## Триггеры для рефакторинга (проверить после альфы)

1. **До финала доходят игроки?** Если малая доля — вторые концовки, возможно, не понадобятся.
2. **Где расходятся концовки?**
  - Все финалы = другая кат-сцена после последней загадки → достаточно параметра в `EndGameSequence`.
  - Финалы триггерятся в разных местах (смерть, секретные предметы) → нужен `GameFlowController` с независимой точкой входа.
3. **Пост-титровый контент** (NG+, галерея, разблокировки) → очистка сейва и переход в Start переезжают из view в отдельную сущность.

## Целевая схема (когда рефакторинг понадобится)

```
Любой финал → GameFlowController.ShowEnding(EndingType)
                ├── EndGameSequence / своя кат-сцена
                ├── CreditsView
                ├── DeleteSave
                └── LoadScene("Start")
```

Любая загадка/система знает только про `GameFlowController`, никогда — про титры напрямую.

## Связанные решения

- Кнопка «Загрузить игру» в стартовом меню скрывается автоматически (`MainMenuController.Start` →
`SlotHasSave(0)`), плюс защита в `OnLoadGame` (прячет кнопку, если сейва нет).
- После титров сейв удалён → в стартовом меню «Загрузить игру» не появится.
- Прогресс тестеров логируется локально — см. Progress Logger System.