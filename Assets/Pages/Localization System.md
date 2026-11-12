<!-- version: 7 -->

Лёгкая собственная система локализации: две строки кода на каждую локализуемую сущность вместо сторонних пакетов. Русский — язык по умолчанию; английский подключён вторым. Тексты и картинки с текстом (страницы ноутбука, фото, белборды) локализуются раздельно.

## Архитектура

```
Assets/Data/Localization
├── Keys.md                              # человеческий реестр ключей (не плодить дубли)
└── Resources/Localization               # Assets > Create > Escape > ...
    ├── Ru.asset                         # LocalizedTextTable  — русский текст
    ├── En.asset                         # LocalizedTextTable  — английский текст
    └── RuSprites.asset                  # LocalizedSpriteTable — русские спрайты
```

**Скрипты:**


| Скрипт                                               | Роль                                                                                                                         |
| ---------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `Assets/Scripts/Core/LocalizationSettings.cs`        | Статика: текущий `Language` в PlayerPrefs (`Settings_Language`), событие `LanguageChanged`, дефолт `Language.Ru`             |
| `Assets/Scripts/Core/Localization.cs`                | Статический фасад: `Get(key)`, `TryGet(key, out value)`, `GetFormat(key, args...)`, `GetSprite(key)`                         |
| `Assets/Scripts/Core/LocalizationKeys.cs`            | Константы всех ключей текста (`ui.interact.*`, `note.*`, …) — в коде не пишем ключи строками                                 |
| `Assets/Scripts/Core/LocalizedTextTable.cs`          | ScriptableObject — плоский список ключ → строка для одного языка                                                             |
| `Assets/Scripts/Core/LocalizedSpriteTable.cs`        | ScriptableObject — плоский список ключ → Sprite для одного языка                                                             |
| `Assets/Scripts/UI/LocalizedTMP.cs`                  | Компонент на `TextMeshProUGUI`/`TextMeshPro`: подменяет текст по ключу, живо обновляется при смене языка                     |
| `Assets/Scripts/UI/LocalizedImage.cs`                | Компонент на `Image`/`RawImage`: подменяет спрайт по ключу (fallback — оригинальный спрайт)                                  |
| `Assets/Scripts/Editor/LocalizedTextTableEditor.cs`  | Кастомный инспектор таблицы: поиск, добавление/удаление записей, сортировка, подсветка дублей ключей, многострочные значения |
| `Assets/Scripts/Editor/LocalizationKeysValidator.cs` | Валидатор: `Tools → Localization → Validate Keys` проверяет, что каждая константа из `LocalizationKeys` есть в Ru и En       |


Таблицы грузятся из Resources по имени языка: `Localization/Ru`, `Localization/RuSprites`. При смене языка фасад сбрасывает кэш и перегружает таблицы; все `LocalizedTMP`/`LocalizedImage` на сцене перерисовываются по событию `LanguageChanged`.

## Формат ключей

`<область>.<объект>[.<деталь>]` — snake_case, нижний регистр. Полный реестр — `Assets/Data/Localization/Keys.md`: группы `note.*`, `ui.*`, `hints.*`, `journal.category.*`, `puzzle.*`, `popup.*`, `laptop.*`. В коде ключ берётся только константой из `LocalizationKeys.cs` — строковые литералы не используем.

Правило: ключа нет в таблице → `Localization.Get` логирует warning и возвращает сам ключ (видимая заглушка вместо падения); `TryGet` молчит и возвращает false — им пользуются для **опциональных** ключей; у спрайтов fallback — исходный sprite.

## Добавление новой строки

1. Проверь в `Keys.md`, что ключ ещё не занят, и заведи константу в `LocalizationKeys.cs`, если её нет.
2. Открой `Ru.asset` (и `En.asset`) — кастомный инспектор позволяет искать по подстроке и добавлять записи без ручного YAML.
3. Добавь запись в обе таблицы, обнови `Keys.md`, если заводишь новую группу.
4. Прогони `Tools → Localization → Validate Keys` — он упадёт, если константа осталась без записи хоть в одной таблице.

## Локализация записок (note.*)

У каждого `DocumentData` есть поле `localizationPrefix`. Ключи страниц: `note.<prefix>.page<n>` (вся страница одной TMP) или `note.<prefix>.page<n>.<i>` (per-TMP, i — порядок `GetComponentsInChildren`, **с нуля**: `.0` — первый TMP, обычно Title с номером страницы, `.1` — Content). `DocumentUI` и `JournalUI` при открытии документа зондируют ключи через `TryGet`: ключа нет → показывается запечённый текст TMP (никаких warning'ов — per-TMP ключи опциональны по дизайну). Сами тексты документов в префабах не трогаем. Дневники (dairy_*) хранят всю запись одной страницей (`page0.1`).

## Локализация документов ноутбука (laptop.*)

Имя файла (`LaptopFileData.fileName`) локализуется ключом `laptop.file.<id>` — уникальным на каждый ассет (см. реестр `Keys.md`, раздел 4.1). `LaptopFileData.DisplayName` берёт строку из таблицы, при пустом/отсутствующем ключе — запечённый `fileName`.

Контент текстовых документов (`LaptopTextFile`) локализуется **парой .txt-файлов**, не таблицей: к ассету прикрепляются `textFile` (русский, `<name>.txt`) и `textFileEn` (английский, `<name>_En.txt`); `LaptopTextFile.Content` возвращает EN-файл при `LocalizationSettings.Current == Language.En`, иначе — русский, при отсутствии файлов — `fallbackContent`. Пример: `Text/Protocol.asset` → `Text/Protocol.txt` + `Text/Protocol_En.txt`. Правила перевода: коды реагентов `GR-*` не меняются (связаны с пазлом синтеза); намеренные глитч-блоки («Ошибка чтения данных...») воспроизводятся в EN с mojibake-эффектом (кириллические гомоглифы).

## Локализация спрайтов (laptop.*)

`LaptopDocumentWindow` (`laptop.<prefix>.page<n>`) и `LaptopImageWindow` (`laptop.<fileId>`) берут спрайты из `LocalizedSpriteTable`; при отсутствии ключа показывается оригинал.

## Категории журнала (journal.category.*)

У `JournalSystem.JournalCategory` есть `categoryNameKey` — id категории без префикса (например, `LearningCat`); `JournalUI.ResolveCategoryName` собирает полный ключ `journal.category.<categoryNameKey>` и берёт подпись кнопки из таблицы, при пустом/несуществующем ключе — Inspector `categoryName`. Ключ в инспекторе должен быть **без пробелов по краям** — пробел ломает сборку ключа. Константы: `LocalizationKeys.JournalCategory*` (префикс `JournalCategoryPrefix`).

## Химический пазл (puzzle.synthesis.*)

Тексты устройств (тултипы центрифуги, горелки, миксера, анализатора, раковины) хранятся в таблицах под `puzzle.synthesis.<device>_title/.desc`. `DeviceTooltipInfo` содержит только поля-ключи (`centrifugeTitleKey`, …) с дефолтами из `LocalizationKeys` — текстовых полей нет; `ChemicalSynthesisController.ShowDeviceTooltip` резолвит их через `ResolveTitle/ResolveDescription`. Результат анализа (`AnalyzerController`) показывает `ItemData.LocalizedName/LocalizedDescription` — имена соединений идут по ключам `item.<имя ассета колбы>.name/.desc` (в т.ч. `Unknown*` варианты). Вердикт успеха — `puzzle.synthesis.success`.

## Паттерн «ключ в инспекторе»

У интеракторов нет serialized-полей с текстом — только ключ локализации (по умолчанию — общий ключ из `LocalizationKeys`):

- `_interactTextKey` заполнен → `Localization.Get(_interactTextKey)` возвращает строку таблицы.
- Ключ перебит в инспекторе на отдельный ключ → объект получает уникальную подсказку (примеры: `DoorInteraction` двери Room 7 → `ui.interact.door_jammed` вместо дефолтного `ui.interact.door_locked`; кнопки этажей → `ui.interact.press_button0/1/2` = Подвал/1й/2й этаж).
- Пустая строка → код подставляет дефолтный ключ из константы (`string.IsNullOrEmpty(key) ? LocalizationKeys.InteractPress : key`).

Текст существует в одном источнике — таблицах; уникальные подписи конкретных объектов (кнопки лифта, рубильники, химические кнопки) подключаются собственными ключами `ui.interact.*`, назначенными в инспекторе префаба.

## Локализация предметов (item.*)

Имя и описание предмета строятся из **имени ассета**: ключ `item.<имя ассета>.name/.desc` (`ItemDataSO.LocalizationKeyPrefix = "item."`). При отсутствии ключа — фолбэк на серийные `itemName`/`description`. Правило: имя ассета — только латиница; кириллица в имени (например, кириллическая «С» вместо `C`) попадает в ключ и ломает поиск, а `ItemId` в ассете задан явно и от имени файла не зависит. Пример: `DataCanister.asset` → `item.DataCanister.name`.

## Смена языка в UI

Кнопка в `SettingsPanelController` циклит enum `Language` через `LocalizationSettings.SetLanguage(...)`; компоненты перерисовываются сами. Значения, которые ставит код (лейблы громкости, `ResolutionButton`, `FrameRateButton`, язык, `WindowModeButton`), `LocalizedTMP` не получает — их обновляет сам контроллер после смены языка.

## Строка «Язык: …» в настройках

Строку целиком собирает `SettingsPanelController.RefreshLanguageLabel`: префикс — ключ `ui.settings.language` («ЯЗЫК:» / «LANGUAGE:»), название — `LocalizationSettings.Current.DisplayName()`. Метод расширения `LanguageExtensions.DisplayName` (`Language.cs`) берёт **самоназвание** языка из таблицы по ключу `ui.settings.language_name.<имя enum>` («РУССКИЙ», «ENGLISH» — значения одинаковы в обеих таблицах, самоназвания не зависят от текущего языка UI); при отсутствии ключа — фолбэк на имя enum. Новый язык получает строку автоматически, достаточно добавить ключ `ui.settings.language_name.<Имя>` в обе таблицы и константу-префикс не трогать (`LocalizationKeys.LanguageNamePrefix`).

## Статус перевода (En)

Таблица `En.asset` переведена полностью: UI-строки, подсказки, FTUE, все записки, книги и категории журнала; наборы ключей Ru и En идентичны (по 484 записи, сверены автоматически — одинаковые наборы, без пустых значений и дублей). Руны — по стандартным английским трактовкам Старшего Футарка; подсказки клавиш в FTUE и разметка `\n`/`<b>`/`<font>`/`<sprite>` сохранены. Дубликаты ключей резолвятся по первой записи (`LocalizedTextTable.TryGet`). Последняя синхронизация: `ui.interact.horror_phone` (Pick up the receiver), `ui.interact.press_safe` (Rotate), `laptop.file.new_document` → File, ключ канистры нормализован в `item.DataCanister.name`, добавлены самоназвания языков `ui.settings.language_name.*`.

## Чеклист при добавлении нового языка

1. Создай `Assets/Data/Localization/Resources/Localization/<Код>.asset` (`Create → Escape → Escape/Localized Text Table`), имя файла = имя enum-значения `Language`.
2. При наличии спрайтов — аналогично `<Код>Sprites.asset`.
3. Добавь значение в enum `Language` — именно по нему работает переключатель в настройках.
4. Добавь самоназвание `ui.settings.language_name.<Имя enum>` в обе таблицы (в обеих — одинаковое нативное написание).

&nbsp;