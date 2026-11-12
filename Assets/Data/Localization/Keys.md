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

## Текстовые ключи (Ru.asset) — по областям

### 1. UI: меню, панели, настройки (`ui.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `ui.loading.title` | ЗАГРУЗКА | MainMenuLoadingScreen, Start.unity (LocalizedTMP) |
| `ui.menu.continue_game` | ПРОДОЛЖИТЬ | Game.unity MainMenu ResumeBtn (LocalizedTMP) |
| `ui.inventory.title` | ИНВЕНТАРЬ | InventoryPanel TitleText (LocalizedTMP) |
| `ui.save.saving` | ● Сохранение... | SaveIndicator.Label (LocalizedTMP) |
| `ui.journal.close_hint` | J — Закрыть | JournalPanel CloseHint (LocalizedTMP) |
| `ui.inspect.hint` | ЛКМ — вращать \| ЛКМ — взять | InspectionPanel.HintText (LocalizedTMP) |
| `ui.settings.window` | ОКНО: | SettingsPanelController |
| `ui.settings.window.borderless` | ОКНО: БЕЗ РАМКИ (ПОЛНЫЙ ЭКРАН) | SettingsPanelController |
| `ui.settings.window.windowed` | ОКНО: ОКОННЫЙ РЕЖИМ | SettingsPanelController |
| `ui.settings.resolution` | РАЗРЕШЕНИЕ: | SettingsPanelController |
| `ui.settings.framerate` | КАДРЫ В СЕКУНДУ: | SettingsPanelController |
| `ui.settings.default_suffix` | " (по умолчанию)" | SettingsPanelController |
| `ui.settings.music_volume` | ГРОМКОСТЬ МУЗЫКИ / Музыка | Start.unity MusicLabel + SettingsPanelController |
| `ui.settings.sfx_volume` | ГРОМКОСТЬ ЭФФЕКТОВ / Звуки | Start.unity SfxLabel + SettingsPanelController |
| `ui.settings.language` | ЯЗЫК: | SettingsPanelController |
| `ui.settings.language_name.Ru` | РУССКИЙ | SettingsPanelController (самоназвание языка) |
| `ui.settings.language_name.En` | ENGLISH | SettingsPanelController (самоназвание языка) |

### 2. UI: взаимодействие (`ui.interact.*`, `ui.drop.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `ui.interact.open_door` | Открыть | DoorInteraction._openText (fallback: Inspector) |
| `ui.interact.close_door` | Закрыть | DoorInteraction._closeText (fallback: Inspector) |
| `ui.interact.locked` | Заперто | DrawerDrag._lockedHint (fallback: Inspector), DoorInteraction._lockedHintKey (дефолт у всех дверей) |
| `ui.interact.door_jammed` | Дверь нельзя открыть. Заело замок | DoorInteraction (Doormesh Room 7, перебитый ключ) |
| `ui.interact.read` | Прочитать | DocumentInteraction (fallback: Inspector) |
| `ui.interact.inspect` | Осмотреть | PuzzleInteractable, FinalDoorPuzzleController, FinalDoorSideInteractable (fallback: Inspector) |
| `ui.interact.inspect_skull` | Осмотреть череп | ScullPuzzlePhase (fallback: Inspector) |
| `ui.interact.press` | Нажать | ClockHand._pressHintText, PuzzleButtonInteractable, MriCutsceneButton (fallback: Inspector) |
| `ui.interact.press_button` | Нажать кнопку | ElevatorButton._hintText (fallback: Inspector) |
| `ui.interact.rotate` | Повернуть | CylinderInteractable, ClockHand (fallback: Inspector) |
| `ui.interact.pull` | Потянуть | DrawerDrag, PhysicsDraggable (fallback: Inspector) |
| `ui.interact.light_on` | Включить | LightSwitch._turnOnHint (fallback: Inspector) |
| `ui.interact.light_off` | Выключить | LightSwitch._turnOffHint (fallback: Inspector) |
| `ui.interact.lever_pull` | Потянуть рычаг | LeverInteraction._textWhenOff, ElectricLever (fallback: Inspector) |
| `ui.interact.lever_return` | Вернуть рычаг | LeverInteraction._textWhenOn (fallback: Inspector) |
| `ui.interact.lever_switch_on` | Включить рычаг | PressureLever._textWhenOff (fallback: Inspector) |
| `ui.interact.lever_switch_off` | Выключить рычаг | PressureLever._textWhenOn (fallback: Inspector) |
| `ui.interact.switch_camera` | Переключить камеру | TVChannelButton (fallback: Inspector) |
| `ui.interact.no_power` | Нет электричества | LightSwitch, ElevatorController.NoPowerHint (fallback: Inspector) |
| `ui.interact.need_lights` | Нужно включить свет | PuzzleModeController, CardLockPuzzleController (fallback: Inspector) |
| `ui.interact.apply` | Применить {0} {1} | InteractionUI.ShowDragHint |
| `ui.drop.swipe_card` | Провести картой | CardLockPuzzleController._dropHint (fallback: Inspector) |
| `ui.drop.insert_generator` | Установить в генератор | GeneratorPuzzleController._dropHint (fallback: Inspector) |
| `ui.drop.use_screwdriver` | Использовать отвёртку | ScrewdriverRepairController._dropHint (fallback: Inspector) |
| `ui.drop.insert_reel` | Установить бабину | FilmProjectorPuzzleController._dropHint (fallback: Inspector) |
| `ui.drop.on_safe` | на сейф | LockDial.GetDropHint |
| `ui.tooltip.rune_pouch` | Мешочек с рунами | RunePouchUI (константа заменена ключом) |
| `ui.tooltip.rune_master` | Мастер рун | RunePouchUI (константа заменена ключом) |

### 3. UI: панели загадок и статусы (`ui.panel.*`, `ui.puzzle.*`, `ui.hint.*`, `ui.minigame.*`, `ui.medallion.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `ui.panel.breaker_on` | Щиток [Питание ВКЛ] | ElectricPanel._hintPoweredOn (fallback: Inspector) |
| `ui.panel.breaker_off` | Щиток [Питание ВЫКЛ] | ElectricPanel._hintPoweredOff (fallback: Inspector) |
| `ui.panel.breaker_no_power` | Щиток [Нет питания] | ElectricPanel._hintNotActivated (fallback: Inspector) |
| `ui.puzzle.generator_first` | Сначала нужно запустить генератор. | ElectricPuzzleController._noGeneratorHint (fallback: Inspector) |
| `ui.puzzle.no_fuse` | Нужен предохранитель. | ElectricPuzzleController._noFuseHint (fallback: Inspector) |
| `ui.puzzle.fifteen.move` | Передвинуть плитку | FifteenPuzzleElement (fallback: "Move Tile") |
| `ui.hint.gen_both/first/second` | Установите (оба) предмет(ы/а) в генератор. | GeneratorPuzzleController popups |
| `ui.hint.projector` | Установите бабину в проектор. | FilmProjectorPuzzleController popup |
| `ui.hint.need_plunger` | Нужен вантуз | LiquidDrainInteractable.GetBlockedHint |
| `ui.hint.exit_esc` | Выход: Esc | FinalDoorPuzzleController popup |
| `ui.medallion.hint` | Вставьте медальоны в нужные отверстия | MedallionBoxPanel.HintText (LocalizedTMP) |
| `ui.minigame.space` | ПРОБЕЛ / ЛКМ | ScrewdriverMinigamePanel центр (LocalizedTMP) |
| `ui.minigame.misses` | Промахи: {0} | ScrewdriverMinigamePanel |
| `ui.minigame.misses_limited` | Промахи: {0}/{1} | ScrewdriverMinigamePanel |
| `puzzle.synthesis.success` | СИНТЕЗ УСПЕШЕН | AnalyzerScreenController |

### 4. UI: кодовые замки и ноутбук (`ui.codelock.*`, `ui.laptop.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `ui.codelock.title` | КОДОВЫЙ ЗАМОК | CodeLockPanel.Title (LocalizedTMP, Game + Sample) |
| `ui.codelock.success` | Доступ открыт | CodeLockUI (fallback: GameConfig) |
| `ui.codelock.wrong` | Неверный код | CodeLockUI (fallback: GameConfig) |
| `ui.interact.enter_code` | Ввести код | CodeLock._interactText (fallback: Inspector) |
| `ui.laptop.access_granted` / `ui.laptop.wrong_password` | Доступ разрешён / Неверный пароль | LaptopController, LaptopOS |

### 4.1. Файлы ноутбука (`laptop.file.*`)

Уникальный ключ на каждый файл-ассет (`LaptopFileData.fileNameKey`), показывается в заголовке окна и подписи иконки. Fallback — запечённый `fileName`. Спрайтовые ключи ноутбука — см. раздел `laptop.*` ниже.

| Ключ | Значение (Ru) | Ассет |
|---|---|---|
| `laptop.file.protocol` | Протокол "Резонанс" | Protocol |
| `laptop.file.resonance` | Протокол "Резонанс" | Resonans |
| `laptop.file.med_card_1` … `med_card_7` | Мед карта П1 … П7 | MedCards/Patient1–7 |
| `laptop.file.personal_file_1` … `personal_file_6` | Личное дело П1 … П6 | PersonalFile/PersonalFile1–6 |
| `laptop.file.video_analysis` | Анализ реагента | Videos/Video_Analysis |
| `laptop.file.video_clear_flask` | Очистка колбы | Videos/Video_ClearColba |
| `laptop.file.video_clear_reagent` | Очистка реагента | Videos/Video_ClearReagent |
| `laptop.file.video_heating` | Нагревание | Videos/Video_Heating |
| `laptop.file.video_mixing` | Смешивание | Videos/Video_Mixing |
| `laptop.file.picture` | Picture | Photos/ImageFdoor |
| `laptop.file.pentagram` | Pentagram | Photos/ImagePentagram |
| `laptop.file.new_audio` / `new_document` / `new_image` / `new_text` / `new_text_2` / `new_video` | значения равны запечённым | Tamplate/* |

### 5. Мир: надписи на стенах и предметах (`world.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `world.plates.*` | Палата 1–7, Морг, Уборная, д-р Морро, Лаборатория, Кладовая, Процедурная, Комната медсестры, Генераторная, Электрощитовая | 3D-таблички TextTablichka на дверях (LocalizedTMP) |
| `world.posters.light_riddle` | Свет зародится под землей, если найдешь ты корм ему среди "лесов" | TextTablichka, PRF_Corridor2ndFloor (LocalizedTMP) |
| `world.posters.diploma` | 1978 - это был лучший год в моей жизни | TextTablichka, PRF_Diplom (LocalizedTMP) |
| `world.uv.corridor1` | Я искал выход там, где конец встречается с началом» | UVText (1), PRF_Corridor1stFloor (LocalizedTMP) |
| `world.uv.nursery` | Я теряла цвета. Свет продолжал хранить их все | UVText (1), PRF_NurseryRoom (LocalizedTMP) |
| `world.uv.room3` | Каждый новый виток растет по закону чисел | UVText (1), PRF_Room3 (LocalizedTMP) |
| `world.uv.room5` | Я искала путь среди развилок, забыв о корнях | UVText (1), PRF_Room5 (LocalizedTMP) |
| `world.uv.room6` | Прежняя форма исчезает. Жизнь принимает новую | UVText (1), PRF_Room6 (LocalizedTMP) |
| `world.notes.clock` | Нужно найти время... Установи правильное время... | Content, Paperclock.prefab (LocalizedTMP) |
| `world.chem.centrifuge_ready` / `world.chem.awaiting_sample` | Готова / Ожидание образца | Экраны центрифуги и анализатора (LocalizedTMP) |

### 6. Предметы (`item.*`)

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `item.<AssetName>.name` / `.desc` | Названия и описания 92 ItemData | ItemData.LocalizedName/LocalizedDescription (ключ = m_Name ассета, НЕ имя файла!) |

### 7. Динамические семейства

| Ключ | Значение (Ru) | Где используется |
|---|---|---|
| `hints.<id>.key` / `hints.<id>.action` | — | InventoryHints (по записи, id задаётся в Inspector) |
| `journal.category.<id>` | — | JournalUI (id = JournalCategory.categoryNameKey) |
| `popup.*` | — | PopupMessageData (по мере необходимости) |
| `note.<prefix>.page<n>` | — | DocumentUI, JournalUI (n с 0; подмена только при существующем ключе) |

Ключи в коде берутся из статического класса `LocalizationKeys` (`Assets/Scripts/Core/LocalizationKeys.cs`) — не использовать инлайновые литералы.

## Спрайтовые ключи (RuSprites.asset)

| Ключ | Где используется |
|---|---|
| `laptop.<prefix>.page<n>` | LaptopDocumentWindow (n с 0; fallback — оригинальный спрайт) |
| `laptop.<fileId>` | LaptopImageWindow (или `laptop.<prefix>` при заданном localizationPrefix) |

## Префиксы спрайтов ноутбука (EnSprites.asset)

Таблица `EnSprites.asset` создана с записями-заглушками: ключ → **оригинальный русский
спрайт**. При появлении EN-версии арта просто замени спрайт в записи — ключи и ассеты
править не нужно. До замены английский язык показывает русские страницы (fallback).

Файлы-ассеты: `PersonalFile1–6` (личные дела), `Patient1–7` (медкарты, у Patient 7
страниц нет — записи под будущее), фото `ImageFdoor` / `ImagePentagram`.
Префиксы заданы в `localizationPrefix` каждого ассета.

| Префикс | Ключи страниц | Оригинальные спрайты |
|---|---|---|
| `laptoppersonalfile1` … `laptoppersonalfile6` | `laptop.laptoppersonalfileN.page0` … `.page1` (PF3 — до `.page2`) | MorroPC/Patient N/T_PersonnelFile#NP*Ru.png |
| `laptopmedcard1` … `laptopmedcard7` | `laptop.laptopmedcardN.page0` … `.page2` (2 и 4 — до `.page3`) | Nursery/Patient N/T_MedicalCard#NP*Ru.png/jpg |
| `laptoppicturedoor` | `laptop.laptoppicturedoor` (одиночный, LaptopImageWindow) | MorroLaptop/PaintFDoor.png |
| `laptopfilepentagram` | `laptop.laptopfilepentagram` (одиночный, LaptopImageWindow) | MorroLaptop/PentagramPaint.png |
