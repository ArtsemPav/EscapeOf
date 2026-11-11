<!-- version: 3 -->

Лёгкая собственная система локализации: две строки кода на каждую локализуемую сущность вместо сторонних пакетов. Русский — язык по умолчанию; английский подключён вторым. Тексты и картинки с текстом (страницы ноутбука, фото, белборды) локализуются раздельно.

## Архитектура

```
Assets/Data/Localization
├── Keys.md                              # реестр ключей (не плодить дубли)
└── Resources/Localization               # Assets > Create > Escape > ...
    ├── Ru.asset                         # LocalizedTextTable  — русский текст
    ├── En.asset                         # LocalizedTextTable  — английский текст
    └── RuSprites.asset                  # LocalizedSpriteTable — русские спрайты
```

**Скрипты:**

| Скрипт | Роль |
|---|---|
| `Assets/Scripts/Core/LocalizationSettings.cs` | Статика: текущий `Language` в PlayerPrefs (`Settings_Language`), событие `LanguageChanged`, дефолт `Language.Ru` |
| `Assets/Scripts/Core/Localization.cs` | Статический фасад: `Get(key)`, `GetFormat(key, args...)`, `GetSprite(key)` |
| `Assets/Scripts/Core/LocalizedTextTable.cs` | ScriptableObject — плоский список ключ → строка для одного языка |
| `Assets/Scripts/Core/LocalizedSpriteTable.cs` | ScriptableObject — плоский список ключ → Sprite для одного языка |
| `Assets/Scripts/UI/LocalizedTMP.cs` | Компонент на `TextMeshProUGUI`/`TextMeshPro`: подменяет текст по ключу, живо обновляется при смене языка |
| `Assets/Scripts/UI/LocalizedImage.cs` | Компонент на `Image`/`RawImage`: подменяет спрайт по ключу (fallback — оригинальный спрайт) |
| `Assets/Scripts/Editor/LocalizedTextTableEditor.cs` | Кастомный инспектор таблицы: поиск, добавление/удаление записей, сортировка, подсветка дублей ключей, многострочные значения |

Таблицы грузятся из Resources по имени языка: `Localization/Ru`, `Localization/RuSprites`. При смене языка фасад сбрасывает кэш и перегружает таблицы; все `LocalizedTMP`/`LocalizedImage` на сцене перерисовываются по событию `LanguageChanged`.

## Формат ключей

`<область>.<объект>[.<деталь>]` — snake_case, нижний регистр. Полный реестр — `Assets/Data/Localization/Keys.md`: группы `note.*`, `ui.*`, `hints.*`, `journal.category.*`, `puzzle.*`, `popup.*`, `laptop.*`.

Правило: ключа нет в таблице → `Localization.Get` логирует warning и возвращает сам ключ (видимая заглушка вместо падения); у спрайтов fallback — исходный sprite.

## Добавление новой строки

1. Проверь в `Keys.md`, что ключ ещё не занят.
2. Открой `Ru.asset` (и `En.asset`) — кастомный инспектор позволяет искать по подстроке и добавлять записи без ручного YAML.
3. Добавь запись в обе таблицы, обнови `Keys.md`, если заводишь новую группу.

## Локализация записок (note.*)

У каждого `DocumentData` есть поле `localizationPrefix`. Ключи страниц: `note.<prefix>.page<n>` (одна TMP на странице) или `note.<prefix>.page<n>.<i>` (несколько TMP, i — порядок `GetComponentsInChildren`). `DocumentUI` подменяет текст при открытии документа только если ключ существует в таблице — иначе показывается запечённый русский текст. Сами тексты документов в префабах не трогаем.

## Локализация документов ноутбука (laptop.*)

Имя файла (`LaptopFileData.fileName`) локализуется ключом `laptop.file.<id>` — уникальным на каждый ассет (см. реестр `Keys.md`, раздел 4.1). `LaptopFileData.DisplayName` берёт строку из таблицы, при пустом/отсутствующем ключе — запечённый `fileName`.

Контент текстовых документов (`LaptopTextFile`) локализуется **парой .txt-файлов**, не таблицей: к ассету прикрепляются `textFile` (русский, `<name>.txt`) и `textFileEn` (английский, `<name>_En.txt`); `LaptopTextFile.Content` возвращает EN-файл при `LocalizationSettings.Current == Language.En`, иначе — русский, при отсутствии файлов — `fallbackContent`. Пример: `Text/Protocol.asset` → `Text/Protocol.txt` + `Text/Protocol_En.txt`. Правила перевода: коды реагентов `GR-*` не меняются (связаны с пазлом синтеза); намеренные глитч-блоки («Ошибка чтения данных...») воспроизводятся в EN с mojibake-эффектом (кириллические гомоглифы).

## Локализация спрайтов (laptop.*)

`LaptopDocumentWindow` (`laptop.<prefix>.page<n>`) и `LaptopImageWindow` (`laptop.<fileId>`) берут спрайты из `LocalizedSpriteTable`; при отсутствии ключа показывается оригинал.

## Паттерн «serialized текст + ключ»

У текстовых полей в скриптах-интеракторах (например, `DoorInteraction._lockedHintText`) парное serialized-поле ключа локализации, по умолчанию равное общему ключу из `LocalizationKeys`:

- Поле ключа пустое → показывается запечённый Inspector-текст (локализация отключена осознанно).
- Ключ заполнен → `Localization.Resolve(key, fallback)` возвращает строку из таблицы, при отсутствии ключа — fallback-текст.

Так все объекты по умолчанию используют одну строку таблицы, а уникальный текст конкретного объекта подключается одним из двух способов: перебить ключ в инспекторе на отдельный ключ (пример: `DoorInteraction` двери Room 7 → `ui.interact.door_jammed` вместо дефолтного `ui.interact.locked`) или очистить ключ, оставив запечённый текст.

## Смена языка в UI

Кнопка в `SettingsPanelController` циклит enum `Language` через `LocalizationSettings.SetLanguage(...)`; компоненты перерисовываются сами. Значения, которые ставит код (лейблы громкости, `ResolutionButton`, `FrameRateButton`, язык, `WindowModeButton`), `LocalizedTMP` не получает — их обновляет сам контроллер после смены языка.

## Статус перевода (En)

Таблица `En.asset` переведена полностью: UI-строки, подсказки, FTUE, все записки и книги. Руны — по стандартным английским трактовкам Старшего Футарка; подсказки клавиш в FTUE (`        `) и разметка `\\n`/`<b>`/`<sprite>` сохранены. Дубли ключей (`note.ftuenote1.page0.1` встречается 3 раза) обновлены синхронно; дубликаты резолвятся по первой записи (`LocalizedTextTable.TryGet`).

## Чеклист при добавлении нового языка

1. Создай `Assets/Data/Localization/Resources/Localization/<Код>.asset` (`Create → Escape → Escape/Localized Text Table`), имя файла = имя enum-значения `Language`.
2. При наличии спрайтов — аналогично `<Код>Sprites.asset`.
3. Добавь значение в enum `Language` — именно по нему работает переключатель в настройках.