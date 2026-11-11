<!-- version: 4 -->

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

| Скрипт                                              | Роль                                                                                                                         |
| --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `Assets/Scripts/Core/LocalizationSettings.cs`       | Статика: текущий `Language` в PlayerPrefs (`Settings_Language`), событие `LanguageChanged`, дефолт `Language.Ru`             |
| `Assets/Scripts/Core/Localization.cs`               | Статический фасад: `Get(key)`, `TryGet(key, out value)`, `GetFormat(key, args...)`, `GetSprite(key)`                          |
| `Assets/Scripts/Core/LocalizationKeys.cs`           | Константы всех ключей текста (`ui.interact.*`, `note.*`, …) — в коде не пишем ключи строками                                 |
| `Assets/Scripts/Core/LocalizedTextTable.cs`         | ScriptableObject — плоский список ключ → строка для одного языка                                                             |
| `Assets/Scripts/Core/LocalizedSpriteTable.cs`       | ScriptableObject — плоский список ключ → Sprite для одного языка                                                             |
| `Assets/Scripts/UI/LocalizedTMP.cs`                 | Компонент на `TextMeshProUGUI`/`TextMeshPro`: подменяет текст по ключу, живо обновляется при смене языка                     |
| `Assets/Scripts/UI/LocalizedImage.cs`               | Компонент на `Image`/`RawImage`: подменяет спрайт по ключу (fallback — оригинальный спрайт)                                  |
| `Assets/Scripts/Editor/LocalizedTextTableEditor.cs` | Кастомный инспектор таблицы: поиск, добавление/удаление записей, сортировка, подсветка дублей ключей, многострочные значения |
| `Assets/Scripts/Editor/LocalizationKeysValidator.cs`| Валидатор: `Tools → Localization → Validate Keys` проверяет, что каждая константа из `LocalizationKeys` есть в Ru и En        |


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

У каждого `DocumentData` есть поле `localizationPrefix`. Ключи страниц: `note.<prefix>.page<n>` (вся страница одной TMP) или `note.<prefix>.page<n>.<i>` (per-TMP, i — порядок `GetComponentsInChildren`, **с нуля**: `.0` — первый TMP, обычно Title с номером страницы, `.1` — Content). `DocumentUI` и `JournalUI` при открытии документа зондируют ключи через `TryGet`: ключа нет → показывается запечённый текст TMP (никаких warning'ов — per-TMP ключи опциональны по дизайну). Сами тексты документов в префабах не трогаем.

## Локализация документов ноутбука (laptop.*)

Имя файла (`LaptopFileData.fileName`) локализуется ключом `laptop.file.<id>` — уникальным на каждый ассет (см. реестр `Keys.md`, раздел 4.1). `LaptopFileData.DisplayName` берёт строку из таблицы, при пустом/отсутствующем ключе — запечённый `fileName`.

Контент текстовых документов (`LaptopTextFile`) локализуется **парой .txt-файлов**, не таблицей: к ассету прикрепляются `textFile` (русский, `<name>.txt`) и `textFileEn` (английский, `<name>_En.txt`); `LaptopTextFile.Content` возвращает EN-файл при `LocalizationSettings.Current == Language.En`, иначе — русский, при отсутствии файлов — `fallbackContent`. Пример: `Text/Protocol.asset` → `Text/Protocol.txt` + `Text/Protocol_En.txt`. Правила перевода: коды реагентов `GR-*` не меняются (связаны с пазлом синтеза); намеренные глитч-блоки («Ошибка чтения данных...») воспроизводятся в EN с mojibake-эффектом (кириллические гомоглифы).

## Локализация спрайтов (laptop.*)

`LaptopDocumentWindow` (`laptop.<prefix>.page<n>`) и `LaptopImageWindow` (`laptop.<fileId>`) берут спрайты из `LocalizedSpriteTable`; при отсутствии ключа показывается оригинал.

## Паттерн «ключ в инспекторе»

У интеракторов нет serialized-полей с текстом — только ключ локализации (по умолчанию — общий ключ из `LocalizationKeys`):

- `_interactTextKey` заполнен → `Localization.Get(_interactTextKey)` возвращает строку таблицы.
- Ключ перебит в инспекторе на отдельный ключ → объект получает уникальную подсказку (пример: `DoorInteraction` двери Room 7 → `ui.interact.door_jammed` вместо дефолтного `ui.interact.door_locked`).
- Пустая строка → код подставляет дефолтный ключ из константы (`string.IsNullOrEmpty(key) ? LocalizationKeys.InteractPress : key`).

Текст существует в одном источнике — таблицах; уникальные подписи конкретных объектов (кнопки лифта, рубильники, химические кнопки) подключаются собственными ключами `ui.interact.*`, назначенными в инспекторе префаба.

## Смена языка в UI

Кнопка в `SettingsPanelController` циклит enum `Language` через `LocalizationSettings.SetLanguage(...)`; компоненты перерисовываются сами. Значения, которые ставит код (лейблы громкости, `ResolutionButton`, `FrameRateButton`, язык, `WindowModeButton`), `LocalizedTMP` не получает — их обновляет сам контроллер после смены языка.

## Статус перевода (En)

Таблица `En.asset` переведена полностью: UI-строки, подсказки, FTUE, все записки и книги; наборы ключей Ru и En идентичны (по 457 записей, сверены автоматически). Руны — по стандартным английским трактовкам Старшего Футарка; подсказки клавиш в FTUE и разметка `\n`/`<b>`/`<font>`/`<sprite>` сохранены. Дубликаты ключей резолвятся по первой записи (`LocalizedTextTable.TryGet`).

## Чеклист при добавлении нового языка

1. Создай `Assets/Data/Localization/Resources/Localization/<Код>.asset` (`Create → Escape → Escape/Localized Text Table`), имя файла = имя enum-значения `Language`.
2. При наличии спрайтов — аналогично `<Код>Sprites.asset`.
3. Добавь значение в enum `Language` — именно по нему работает переключатель в настройках.