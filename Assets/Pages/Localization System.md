<!-- version: 1 -->

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

## Локализация спрайтов (laptop.*)

`LaptopDocumentWindow` (`laptop.<prefix>.page<n>`) и `LaptopImageWindow` (`laptop.<fileId>`) берут спрайты из `LocalizedSpriteTable`; при отсутствии ключа показывается оригинал.

## Смена языка в UI

Кнопка в `SettingsPanelController` циклит enum `Language` через `LocalizationSettings.SetLanguage(...)`; компоненты перерисовываются сами. Значения, которые ставит код (лейблы громкости, `ResolutionButton`, `FrameRateButton`, язык, `WindowModeButton`), `LocalizedTMP` не получает — их обновляет сам контроллер после смены языка.

## Статус перевода (En)

Таблица `En.asset` переведена полностью: UI-строки, подсказки, FTUE, все записки и книги. Руны — по стандартным английским трактовкам Старшего Футарка; подсказки клавиш в FTUE (`        `) и разметка `\\n`/`<b>`/`<sprite>` сохранены. Дубли ключей (`note.ftuenote1.page0.1` встречается 3 раза) обновлены синхронно; дубликаты резолвятся по первой записи (`LocalizedTextTable.TryGet`).

## Чеклист при добавлении нового языка

1. Создай `Assets/Data/Localization/Resources/Localization/<Код>.asset` (`Create → Escape → Escape/Localized Text Table`), имя файла = имя enum-значения `Language`.
2. При наличии спрайтов — аналогично `<Код>Sprites.asset`.
3. Добавь значение в enum `Language` — именно по нему работает переключатель в настройках.