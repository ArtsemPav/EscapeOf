# Localization Keys Registry

Реестр ключей локализации, чтобы не плодить дубли. Сами строки живут в
`Assets/Data/Localization/Resources/Localization/Ru.asset` (текст) и
`RuSprites.asset` (картинки с текстом: страницы ноутбука, фото, белборды).

## Заметки (note.*)

Каждый `DocumentData` из `/Assets/Data/Notes` получил поле `localizationPrefix`.
Ключи страниц: `note.<prefix>.page<n>` (один TMP на странице) или
`note.<prefix>.page<n>.<i>` (несколько TMP, i — в порядке GetComponentsInChildren).
Ключа нет в таблице → показывается запечённый русский текст.

| Префикс | Ассет(ы) |
|---|---|
| `book_doctor_safe` | BookDoctorSafe (4 страницы) |
| `book_morro_dairy` | Book_MorroDairy |
| `book_scandic_alphabet` | Book_ScandicAlphabet |
| `dairy_room1_1` … `dairy_room6_2` | DairyRoom1–6, варианты 1/2 |
| `ftuecontrols` | FTUEControls |
| `ftuenote1` | FTUENote1 1, FTUENote1 2, FTUENote2 (одинаковый текст) |
| `ftuenote3` … `ftuenote8`, `ftuenote10` | FTUENote3–8, FTUENote10 |
| `note_doctor_dairy_1/2` | NoteDoctorDairy 1, NoteDoctorDairy 2 |
| `note_generator_1/2` | NoteGenerator 1, NoteGenerator 2 |
| `note_laboratory_1` | NoteLaboratory 1 |
| `note_nurcery_room_1/3` | NoteNurceryRoom 1, NoteNurceryRoom 3 |
| `note_nurcery_station_1` | NoteNurceryStation 1 |
| `note_procedural_1` | NoteProcedural 1 |
| `note_room2_1`, `note_room3_1/2`, `note_room4_1`, `note_room5_1` | NoteRoom2–5 |

## Формат

`<область>.<объект>[.<деталь>]` — snake_case, нижний регистр.

## Текстовые ключи (Ru.asset)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `ui.settings.window` | ОКНО: | SettingsPanelController |
| `ui.settings.window.borderless` | ОКНО: БЕЗ РАМКИ (ПОЛНЫЙ ЭКРАН) | SettingsPanelController |
| `ui.settings.window.windowed` | ОКНО: ОКОННЫЙ РЕЖИМ | SettingsPanelController |
| `ui.settings.resolution` | РАЗРЕШЕНИЕ: | SettingsPanelController |
| `ui.settings.framerate` | КАДРЫ В СЕКУНДУ: | SettingsPanelController |
| `ui.settings.default_suffix` | " (по умолчанию)" | SettingsPanelController |
| `ui.settings.music_volume` | Музыка | SettingsPanelController (после смены языка) |
| `ui.settings.sfx_volume` | Звуки | SettingsPanelController (после смены языка) |
| `ui.settings.language` | ЯЗЫК: | SettingsPanelController |
| `ui.interact.apply` | Применить {0} {1} | InteractionUI.ShowDragHint |
| `ui.interact.rotate` | Повернуть | CylinderInteractable (fallback: Inspector) |
| `ui.codelock.success` | Доступ открыт | CodeLockUI (fallback: GameConfig) |
| `ui.codelock.wrong` | Неверный код | CodeLockUI (fallback: GameConfig) |
| `ui.minigame.misses` | Промахи: {0} | ScrewdriverMinigamePanel |
| `ui.minigame.misses_limited` | Промахи: {0}/{1} | ScrewdriverMinigamePanel |
| `hints.<id>.key` / `hints.<id>.action` | — | InventoryHints (по записи, id задаётся в Inspector) |
| `journal.category.<id>` | — | JournalUI (id = JournalCategory.categoryNameKey) |
| `puzzle.synthesis.success` | СИНТЕЗ УСПЕШЕН | AnalyzerScreenController |
| `popup.*` | — | PopupMessageData (по мере необходимости) |
| `note.<prefix>.page<n>` | — | DocumentUI (n с 0; подмена только при существующем ключе) |

## Спрайтовые ключи (RuSprites.asset)

| Ключ | Где используется |
|---|---|
| `laptop.<prefix>.page<n>` | LaptopDocumentWindow (n с 0; fallback — оригинальный спрайт) |
| `laptop.<fileId>` | LaptopImageWindow (или `laptop.<prefix>` при заданном localizationPrefix) |
