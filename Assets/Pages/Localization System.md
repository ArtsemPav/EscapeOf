<!-- version: 1 -->

# Localization System

Система двуязычной локализации (RU/EN) для EscapeOf: тексты UI, подсказки взаимодействия, предметы, записки/книги, титры, мировые надписи.

## Архитектура

| Компонент                  | Путь                                                | Роль                                                                                                         |
| -------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `Localization`             | `/Assets/Scripts/Core/Localization.cs`              | Статический доступ: `Get(key)`, `GetFormat(key, args)`, `TryGet(key, out value)`, `Resolve(key, fallback)`   |
| `LocalizationSettings`     | `/Assets/Scripts/Core/`                             | Текущий язык, событие `LanguageChanged` (на него подписываются UI-компоненты)                                |
| `LocalizationKeys`         | `/Assets/Scripts/Core/LocalizationKeys.cs`          | Реестр констант ключей — в коде использовать только их, не строковые литералы                                |
| `Ru.asset` / `En.asset`    | `/Assets/Data/Localization/Resources/Localization/` | Текстовые таблицы (`LocalizedTextTable`), 417 ключей каждая                                                  |
| `RuSprites.asset`          | там же                                              | Спрайтовая таблица (`LocalizedSpriteTable`) — **пустая**, контент не готов                                   |
| `EnSprites.asset`          | там же                                              | **Отсутствует** — создать, когда появятся английские спрайты                                                 |
| `LocalizedTMP`             | `/Assets/Scripts/UI/LocalizedTMP.cs`                | Компонент: подменяет текст любого `TMP_Text` (включая 3D-текст) по ключу `_key`, обновляется при смене языка |
| `LocalizedImage`           | `/Assets/Scripts/UI/`                               | Компонент: подменяет спрайт по ключу (для картинок с текстом)                                                |
| `LocalizedTextTableEditor` | `/Assets/Scripts/Editor/`                           | Editor-окно просмотра/поиска таблиц                                                                          |

## Схема ключей

`<область>.<объект>[.<детали>]` — snake_case, нижний регистр.

| Префикс                                                                                       | Область                                                |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| `ui.menu`, `ui.loading`, `ui.inventory`, `ui.save`, `ui.journal`, `ui.inspect`, `ui.settings` | Общий UI                                               |
| `ui.interact`, `ui.drop`                                                                      | Подсказки взаимодействия (двери, рычаги, чтение, drag) |
| `ui.panel`, `ui.puzzle`, `ui.hint`, `ui.minigame`, `ui.medallion`                             | Загадки и их статусы                                   |
| `ui.codelock`, `ui.laptop`                                                                    | Замки и ноутбук                                        |
| `world.plates`, `world.uv`, `world.posters`, `world.chem`, `world.notes`                      | Надписи в мире (таблички, UV-секреты, экраны)          |
| `item.<AssetName>.name` / `.desc`                                                             | Предметы инвентаря                                     |
| `note.<prefix>.page<n>[.<i>]`                                                                 | Страницы записок и книг                                |
| `credits.<name>.title` / `.text`                                                              | Титры                                                  |
| `journal.category.<id>`                                                                       | Категории журнала                                      |
| `hints.<id>.key` / `.action`                                                                  | Нижняя панель подсказок инвентаря                      |
| `popup.*`                                                                                     | Popup-сообщения                                        |

Полный реестр с значениями и местами использования: `/EscapeOf/Assets/Data/Localization/Keys.md`. Записи таблиц отсортированы по группам в этом же порядке — новые ключи дописывать в свою группу.

## Каналы локализации

### 1. Статичный UI-текст — `LocalizedTMP`

Вешается на объект с `TMP_Text` (UGUI и 3D). Поле `_key` → перевод из таблицы. Перерисовка при `LanguageChanged`. Используется в заголовках меню, паузы, настроек, инвентаря, 3D-табличках (`world.*`).

### 2. Динамические подсказки взаимодействия — `Localization.Resolve`

`IInteractable.GetInteractText()` / `GetBlockedHint()` в коде:

```csharp
return Localization.Resolve(LocalizationKeys.InteractOpenDoor, _openText);
```

Ключ существует → перевод; ключа нет → значение из Inspector (fallback). Паттерн единый для всех интерактиблов.

### 3. Предметы — `ItemData`

Вычисляемые свойства `LocalizedName` / `LocalizedDescription`: ключ = `item.<m_Name ассета>.name` / `.desc`, fallback — исходные поля. Используются в подсказках инвентаря, окне осмотра, `PickableItem`.

### 4. Записки и книги — схема `note.<prefix>`

Контент живёт **в префабах страниц** (запечённый русский TMP). `DocumentData.localizationPrefix` задаёт префикс; при показе `DocumentUI.ApplyLocalizedPageText` (подбор) и `JournalUI.ApplyLocalizedPageText` (журнал) заменяют текст ключами `note.<prefix>.page<n>` (вся страница) или `note.<prefix>.page<n>.<i>` (отдельные TMP). Ключа нет → запечённый текст.

Не вешать `LocalizedTMP` на страницы записок — сломает постраничную схему.

### 5. Титры — `CreditsEntry`

`LocalizedTitle` / `LocalizedText` с ключами `credits.<имя ассета>.title` / `.text`; `EndGameCreditsView` собирает текст из них.

### 6. Категории журнала

`JournalSystem.JournalCategory.categoryNameKey` → ключ `journal.category.<id>`; резолв в `JournalUI.ResolveCategoryName`. Сейчас `categoryNameKey` в сцене пустые — кнопки показывают русские имена.

## Как добавить новый ключ

1. Выбрать префикс по области (см. схему) — не смешивать области.
2. Добавить константу в `LocalizationKeys.cs` (не инлайн-литерал).
3. Добавить запись в `Ru.asset` и `En.asset` **в свою группу** (не в конец файла).
4. Дописать строку в `Keys.md`.
5. В коде — `Localization.Resolve(key, fallback)` для подсказок, `Get(key)` для обязательных текстов.

Правка таблиц — через Bezi (`getAsset` + `createOrUpdateAsset` с `assetType: "LocalizedTextTable"`); прямое редактирование `.asset` текстовым инструментом запрещено настройками воркспейса.

## Правила проекта

- Fallback-строки в Inspector остаются русскими — это рабочий механизм, переводить их в коде не нужно.
- Комментарии и Tooltip'ы в коде не локализуются.
- Порядок записей в таблице на работу не влияет (поиск по ключу; при дублях берётся первая запись).

## Статус и известные пробелы

- Текстовая локализация: UI, интерактиблы, загадки, предметы, мировые надписи, титры — **готово** (проверено свипом кириллицы по сценам/префабам/коду).
- Категории журнала: механизм есть, `categoryNameKey` в `JournalSystem` (Game.unity, `/GameManager`) пустые — заполнить при желании перевода имён («Обучение», «Р. Морро», …).
- Спрайты с текстом (страницы ноутбука, фото, белборды): `RuSprites` пуст, `EnSprites` отсутствует — контент для художника.
- Имена файлов ноутбука (`LaptopFileData.fileName`) — контент ассетов, схемой `note.*` не покрываются.
- `GameConfig.pickUpPrefix` («Взять») — мёртвое поле, нигде не используется.

## Проверка

- Свип кириллицы по сценам: скрипт в шелл (`Select-String` по `m_text:.*\\u04` в `Assets/Scenes/*.unity` и префабах) — текст в YAML хранится как `\u04XX`-последовательности.
- После правок таблиц читать значения обратно (`getAsset`), после правок кода — перекомпиляция и `readConsole`.
- Визуальная проверка смены языка — Play Mode, переключение в настройках.
