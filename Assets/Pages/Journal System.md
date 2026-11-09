<!-- version: 1 -->

# Plan: Journal System — Доска с зажимом (редизайн)

## Context

Журнал был реализован как UGUI-панель (список записок слева + TMP-текст справа). Пользователь переделывает его в **доску с бумажным зажимом**: слева — кнопки категорий документов, справа — стопка страниц, собираемая из **существующих 3D-префабов страниц и записок**. Листание — кликом по краям стопки. Записи активируются в журнале при **первом прочтении** документа в мире (`DocumentInteraction` → `DocumentData`) — сами записки при этом **не исчезают**.

![Референс доски журнала](bezi://upload/f587d31d-01c7-4bd6-8018-8476dfee2500)

### Подтверждённые решения (опрос пользователя)

1. **Отслеживание через `DocumentData`, без `ItemData`:** связь с журналом — напрямую через SO `DocumentData`; `ItemData` и `PickableItem` **не изменяются**.
2. **Рендер:** как в `DocumentUI` — камера рендерит реальные 3D-префабы страниц в `RawImage` на весь экран. UGUI-текстовых дублей нет.
3. **Переписываются оба компонента:** `JournalSystem` (записи из `DocumentInteraction`) и `JournalUI` (доска вместо списка+текста).
4. **Категории фиксированные:** слева кнопки категорий (задаются в инспекторе `JournalSystem`); клик по категории **сразу** показывает стопку страниц — все документы категории склеены в одну стопку и листаются по очереди.
5. **Непрочитанные документы скрыты** — в стопке только найденные.
6. **Записки не исчезают:** активация записи — при первом взаимодействии/прочтении; объект в мире остаётся (так уже работает `DocumentInteraction` → `DocumentUI.Open`).

### Существующая архитектура (проверено в коде)


| Компонент                                   | Путь                                                          | Роль                                                                                                             |
| ------------------------------------------- | ------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `DocumentData`                              | `/EscapeOf/Assets/Scripts/Data/DocumentData.cs`               | SO записки: `isNote`, `documentPrefab` (3D-префаб с TMP в `Pages/Page_N`), аудио. **Единственный ключ журнала**  |
| `DocumentInteraction`                       | `/EscapeOf/Assets/Scripts/Interaction/DocumentInteraction.cs` | `IInteractable`, `Interact()` → `DocumentUI.Instance.Open(data)` + хук `AddEntry` — **точка регистрации записи** |
| `JournalEntryData`                          | `/EscapeOf/Assets/Scripts/Data/JournalEntryData.cs`           | Статический экстрактор страниц из 3D-префаба + кеш. **Переиспользовать как есть**                                |
| `JournalSystem`                             | `/EscapeOf/Assets/Scripts/Journal/JournalSystem.cs`           | Singleton, `AddEntry(DocumentData)`, `ISaveable` (SaveId `journal`). **Переписать модель записей**               |
| `JournalUI`                                 | `/EscapeOf/Assets/Scripts/Journal/UI/JournalUI.cs`            | UGUI список+текст. **Полностью переделать в доску**                                                              |
| `JournalEntryButton`                        | `/EscapeOf/Assets/Scripts/Journal/UI/JournalEntryButton.cs`   | Кнопка элемента списка. **Переиспользовать как кнопку категории**                                                |
| `DocumentUI`                                | `/EscapeOf/Assets/Scripts/UI/DocumentUI.cs`                   | Референс: камера + `RawImage`, спавн префаба, стэк страниц `_pageStackOffset`, flip-анимация, edge-click         |
| `InputManager`                              | `/EscapeOf/Assets/Scripts/Core/InputManager.cs`               | Событие `OnJournalPerformed` — **уже есть, не менять**                                                           |
| `GameManager` / `UIManager` / `SaveManager` | `/EscapeOf/Assets/Scripts/Core/`                              | `SetPause`, `OpenPanel/ClosePanel`, `ISaveable` — использовать как есть                                          |
| `PickableItem` / `ItemData`                 | `/EscapeOf/Assets/Scripts/Inventory/`                         | **Не изменяются** — журнал не зависит от инвентаря                                                               |


### Проектные правила

- Код → `/Assets/Scripts`, префабы → `/Assets/Prefabs`, SO → `/Assets/Data`.
- Input System: **не редактировать** сгенерированные файлы — действие `Journal` (J) уже добавлено.
- Комментарии к публичным методам, константы вместо магических чисел.

---

## Modifications

### A. Script: `JournalSystem.cs` — переписать

**Путь:** `/EscapeOf/Assets/Scripts/Journal/JournalSystem.cs`

**Описание:** Хранилище фиксированных категорий журнала и множества прочитанных (разблокированных) документов + `ISaveable`.

**Публичный API:**

```csharp
public class JournalSystem : MonoBehaviour, ISaveable
{
    public static JournalSystem Instance { get; }
    public bool IsOpen { get; }

    [Serializable]
    public class JournalCategory
    {
        public string categoryName;               // текст кнопки слева
        public List<DocumentData> documents;      // фиксированный порядок страниц в стопке
    }

    // Фиксированные категории, заполняются в инспекторе
    public List<JournalCategory> Categories { get; }

    // Вызывается при разблокировке нового документа (для UI)
    public event Action<DocumentData> OnEntryAdded;

    // Разблокировать документ (вызывается из DocumentInteraction при первом прочтении)
    public void AddEntry(DocumentData data);

    // Документ разблокирован?
    public bool IsUnlocked(DocumentData data);

    // Открыть/закрыть журнал (пауза + панель — без изменений)
    public void OpenJournal();
    public void CloseJournal();
}
```

**Логика:**

- `_unlocked: HashSet<DocumentData>` (+ параллельный `List<DocumentData>` для стабильного порядка сейва).
- `AddEntry`: `null`/уже разблокирован → выход; иначе добавить в обе коллекции, `OnEntryAdded?.Invoke(data)`, `SaveManager.Instance?.Save()`.
- `OpenJournal` / `CloseJournal` — **без изменений** из текущей реализации: `GameManager.SetPause` → `UIManager.OpenPanel`, закрытие в обратном порядке.
- **SaveData (ISaveable):** без изменений — `SaveId = "journal"`, `List<string> entryIds` из `DocumentData.name`, восстановление через `Resources.FindObjectsOfTypeAll<DocumentData>()` без вызова `OnEntryAdded`/`Save`. Формат данных прежний — старые сейвы совместимы.
- Категории НЕ сериализуются в сейв (фиксированные, задаются в инспекторе).

**Зависимости:** `DocumentData`, `SaveManager`, `GameManager`, `UIManager`.

---

### B. Script: `DocumentInteraction.cs` — сохранить хук

**Путь:** `/EscapeOf/Assets/Scripts/Interaction/DocumentInteraction.cs`

**Описание:** Единственная точка регистрации записи. Хук `JournalSystem.Instance?.AddEntry(_documentData);` в `Interact()` (добавленный в прошлой итерации) — **оставить как есть**. Документ при этом открывается через `DocumentUI.Open` и остаётся в мире; повторные взаимодействия отсекаются дедупликацией `AddEntry`.

**Зависимости:** `DocumentUI`, `JournalSystem`.

---

### C. Script: `JournalUI.cs` — переписать в контроллер доски

**Путь:** `/EscapeOf/Assets/Scripts/Journal/UI/JournalUI.cs`

**Описание:** Полноэкранная доска с зажимом: слева колонка кнопок категорий, справа стопка 3D-страниц, листаемая кликами по краям.

**Инспектор-поля:**


| Поле                                 | Тип                    | Назначение                                                        |
| ------------------------------------ | ---------------------- | ----------------------------------------------------------------- |
| `_journalPanel`                      | `GameObject`           | Корневая панель доски                                             |
| `_categoryButtonsContainer`          | `Transform`            | Контейнер кнопок категорий (VerticalLayoutGroup)                  |
| `_categoryButtonPrefab`              | `JournalEntryButton`   | Префаб кнопки категории (переиспользуем `PRF_JournalEntryButton`) |
| `_boardRawImage`                     | `RawImage`             | Куда камера рендерит стопку страниц                               |
| `_inspectionCamera`                  | `Camera`               | Отдельная камера рендера страниц (по образцу `DocumentUI`)        |
| `_pageContainer`                     | `Transform`            | Корень инстанцированных страниц перед камерой                     |
| `_rightClickZone` / `_leftClickZone` | `UnityEngine.UI.Image` | Кнопки-стрелки по краям стопки (спрайт рун, ColorTint-переходы)   |
| `_closeHintText`                     | `TextMeshProUGUI`      | Подсказка внизу                                                   |


**Публичный API:**

```csharp
public class JournalUI : MonoBehaviour
{
    public static JournalUI Instance { get; }
    public void Toggle();                       // JournalSystem.OpenJournal/CloseJournal
    public void SelectCategory(int index);      // клик по кнопке категории
    public void FlipRight();                    // клик по правому краю
    public void FlipLeft();                     // клик по левому краю
}
```

**Логика:**

1. **Открытие категории:** `SelectCategory(i)` → взять из `JournalSystem.Categories[i]` только разблокированные (`IsUnlocked`) документы → для каждого узнать число страниц через `JournalEntryData.ExtractPages(data)`; **инстанцировать 3D-префабы** (`data.documentPrefab`) под `_pageContainer` перед `_inspectionCamera` (offscreen-origin как в `DocumentUI.InspectionOrigin`), страницы — `Pages/Page_N`. Страницы складываются в единый плоский список `_pages` по порядку документов.
2. **Стопка:** страницы визуально лежат стопкой с небольшим смещением/поворотом (`_pageStackOffset` — константы, по образцу `DocumentUI`). Активна (видима поверх) одна «верхняя» страница; порядок отображения — sibling index / z-смещение, как в `DocumentUI`.
3. **Листание:** мгновенное, без анимации — `FlipRight()`/`FlipLeft()` вызывают `ShowPage(_currentPage ± 1)`; корутина `FlipRoutine`, `_isFlipping`, `_flipDuration`, `_flipCurve` удалены. На границах клики игнорируются.
4. **Edge-click:** `_rightClickZone`/`_leftClickZone` — видимые `Image` со спрайтом рун (`T_Runes.png` / `Runes_17`), полноценные кнопки: `Button` с `Transition.ColorTint` (`targetGraphic` привязан явно), подсветка при наведении `(1, 0.92, 0.7)` и затемнение при нажатии `(0.7, 0.7, 0.7)`, `fadeDuration = 0.1`. Стрелка скрывается (`button.interactable = false` + `zone.enabled = false`), если страницы в эту сторону нет — `UpdateEdgeZoneAvailability()` в конце `ShowPage`.
5. **Кнопки категорий:** при `OnEnable`/`OnEntryAdded`/открытии панели — пересобрать контейнер: для каждой категории `Instantiate(_categoryButtonPrefab)`, вызов `Setup(categoryName, () => SelectCategory(i))`. Активная категория подсвечивается (`ColorBlock`/выбранный спрайт). Если в категории 0 разблокированных документов — кнопка скрыта.
6. **Клавиатура:** `Update()` по образцу `DocumentUI.Update` (строки 179–206): `←/A` → `FlipLeft`, `→/D/Space` → `FlipRight`, `Escape` → `CloseJournal`. J/Tab обрабатывается `InputManager.OnJournalPerformed` (уже есть).
7. **Камера:** создание/настройка `RenderTexture` и позиции камеры — скопировать из `DocumentUI.Awake`/`Open` (строки 127–168); `_boardRawImage.texture` = RT.
8. **Закрытие:** уничтожить инстанцированные страницы (`_pageContainer`), снять выделение категории.

**Зависимости:** `JournalSystem`, `JournalEntryData`, `JournalEntryButton`, `DocumentData`, `InputManager`.

---

### D. Script: `JournalEntryButton.cs` — минимальная правка

**Путь:** `/EscapeOf/Assets/Scripts/Journal/UI/JournalEntryButton.cs`

**Описание:** Переиспользуется как кнопка категории. Заменить `Setup(DocumentData data)` на `Setup(string label, Action onClick)` — текст + произвольный клик. Старый вариант удалить, если больше нигде не используется.

**Зависимости:** нет.

---

### E. Engine Actions: перестройка `/Canvas/JournalPanel` в сцене `Game.unity`

**Описание:** Заменить иерархию списка+текста на доску. Старых детей `EntryList`/`NoteView` — удалить, панель и `Header` переиспользовать.

**Целевая структура:**

```
Canvas
└── JournalPanel                     # JournalUI, inactive; фон — арт доски (Image)
    ├── Header
    │   └── CloseHint                # TMP: "J / Escape — закрыть"
    ├── CategoryButtons              # VerticalLayoutGroup, левая колонка
    │   └── (кнопки из PRF_JournalEntryButton — инстансятся рантаймом)
    ├── BoardView
    │   ├── BoardRawImage            # RawImage — рендер камеры со стопкой
    │   ├── LeftClickZone            # Image-стрелка левого края (кнопка, скрывается без предыдущей страницы)
    │   └── RightClickZone           # Image-стрелка правого края (кнопка, скрывается без следующей страницы)
    └── PageContainer3D              # пустой корень (маркер; 3D-страницы — вне Canvas)
```

**Bezi Actions (проверены в прошлой итерации):**

1. `getSceneHierarchy({ scope: '/EscapeOf/Assets/Scenes/Game.unity' })` — текущее состояние `/Canvas/JournalPanel`.
2. `deleteGameObject` — старые `EntryList`, `NoteView` (и их детей).
3. `createGameObject` + `addOrUpdateComponent` (`UnityEngine.UI.RawImage`, `UnityEngine.UI.Image`, `UnityEngine.UI.VerticalLayoutGroup`, `UnityEngine.UI.Button`, `TMPro.TextMeshProUGUI`, `JournalUI`) для новых узлов.
4. `setRectTransformLayout` — якоря/растяжения: `JournalPanel` — Stretch, `CategoryButtons` — левая колонка, `BoardView` — занимает остаток.
5. `updateGameObject({ path: '/Canvas/JournalPanel', activeSelf: false })`.
6. Заполнить сериализованные поля `JournalUI` через `addOrUpdateComponent` (props со ссылками `UnityObjectReference { gameObjectPath }`).

**Зависимости:** скрипты A–D скомпилированы.

---

### F. Engine Actions: настройка `JournalSystem` в сцене

**Описание:** Заполнить фиксированные категории в инспекторе компонента `JournalSystem`. **Состав категорий определяет пользователь в процессе** — план не фиксирует конкретную разбивку; при Build создаётся стартовый набор, дальше правится в инспекторе.

**Стартовый набор (предложение, правится в инспекторе в любой момент):**

```
Дневники      DairyRoom1..6 (части " 1"/" 2" подряд — склеятся в стопке по порядку)
Записки       NoteGenerator 1–2, NoteLaboratory 1, NoteNurceryRoom 1–3,
              NoteProcedural 1, NoteRoom2..5 (части подряд)
Книги         BookDoctorSafe, BookMorroDairy, BookScandicAlphabet
Обучение      FTUEBook, FTUENote2..10
```

Все `DocumentData` лежат в `/EscapeOf/Assets/Data/Notes`.

**Bezi Actions:**

1. `getGameObjectsByComponent({ scope, componentType: 'JournalSystem' })` — найти объект с компонентом.
2. `addOrUpdateComponent` — props: `categories` = массив `{ categoryName, documents: [ { assetPath: '/Assets/Data/Notes/....asset' }, ... ] }` со стартовым набором выше; состав пользователь меняет в инспекторе после Build.

**Зависимости:** `DocumentData`-ассеты уже существуют и используются в `DocumentInteraction`-объектах сцены — дополнительного ручного связывания не требуется.

---

### G. Engine Actions: проверка

**Bezi Actions:**

1. `readConsole({ projectPath: '/EscapeOf', shouldRecompile: true, logType: 'Error' })`.
2. `getIsolatedImage({ sceneObjs: [{ scenePath: '/EscapeOf/Assets/Scenes/Game.unity', objects: [{ path: '/Canvas/JournalPanel' }] }] })` — визуальная проверка доски (без Play Mode страницы не видны — это нормально; проверить раскладку кнопок/зон).

---

## Key Patterns to Reuse


| Источник                                                      | Паттерн                                                                                                                                |
| ------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `/EscapeOf/Assets/Scripts/UI/DocumentUI.cs`                   | Камера + `RawImage` + RT, спавн 3D-префаба, стэк `_pageStackOffset`, flip-анимация, edge-click зоны, клавиатурная навигация в `Update` |
| `/EscapeOf/Assets/Scripts/Data/JournalEntryData.cs`           | `ExtractPages` + кеш — переиспользовать без изменений                                                                                  |
| `/EscapeOf/Assets/Scripts/Interaction/DocumentInteraction.cs` | Хук `AddEntry` в `Interact()` — единая точка регистрации записи                                                                        |
| `/EscapeOf/Assets/Scripts/Inventory/UI/InventoryUI.cs`        | Подписка на `InputManager` в `OnEnable/OnDisable`, `UIManager.OpenPanel/ClosePanel`                                                    |
| `/EscapeOf/Assets/Scripts/Core/GameManager.cs`                | `SetPause` — не дублировать логику `Time.timeScale`                                                                                    |


## Implementation Notes

- **Регистрация записи — при первом прочтении:** единственная точка — хук в `DocumentInteraction.Interact()`. Документ не исчезает из мира; `AddEntry` идемпотентен — повторные чтения безопасны.
- **Журнал не зависит от инвентаря:** `ItemData`/`PickableItem` не изменяются; предметы, которые нужно показать в журнале, оформляются как `DocumentInteraction` + `DocumentData` (префаб страницы уже лежит в `/Assets/Prefabs/Notes` и `/Assets/Prefabs/books`).
- **Старые сейвы:** формат `journal` (список имён `DocumentData`) не меняется — миграция не нужна. Смысл записей меняется (теперь это документы, прочитанные через `DocumentInteraction`) — старые записи останутся разблокированными, это приемлемо.
- **Страницы категории = склейка документов:** порядок страниц строго по порядку `documents` в категории; внутри документа — по `Page_N`. Пересборка стопки при каждом `SelectCategory` (инстансы уничтожаются при закрытии панели/смене категории).
- **Листание мгновенное:** анимация сдвига листа удалена по решению пользователя — страницы появляются сразу в целевой позе, камера ставится `FrameCamera` без твина. Флаг `_isFlipping` не нужен.
- **Сдвиги стопок настраиваются в инспекторе** `JournalUI` на `/JournalUIRoot`: `_stackRightShift` (прижатие стопки к правой границе кадра, 0.16 — край листа на границе), `_pileShift` (выдвижение стопки прочитанных влево, в ширинах листа), плюс `_pageStackOffset` и `_framingMultiplier`.
- **Нормализация страниц:** `FitInstanceIntoFixedFrame` масштабирует инстанс по bounds всех рендереров в фиксированный кадр `_fixedBoundsSize` (fill 90%). Разные меши бумаги (`Paper` vs `HalfPaperCut` у FTUE-базы) дают разный аспект и масштаб — при необходимости унифицировать меш в `PRF_PaperNoteFtueBase`.
- **Выделение кнопок категорий:** подсветку сохраняет `JournalCategoryButton` (подкласс `Button`, форсирует `selectedColor`); `JournalUI` хранит `_categoryButtonIndices` — индексы только видимых категорий.
- **Арт доски/зажима:** временно плейсхолдер-Image; финальные спрайты (доска, зажим, скрепки кнопок) подставит пользователь — структура UI их уже предполагает (фон панели, фон кнопок).
- **Edge-зоны:** теперь видимые кнопки-стрелки со спрайтом рун; не должны перекрывать кнопки категорий (зоны только над областью стопки).
- **J-логика не меняется:** `InputManager.OnJournalPerformed` и действие `Journal` в `.inputactions` уже реализованы — пункт старого плана G выполнен и удаляется из объёма.

## TODO

- [x] Переписать `/EscapeOf/Assets/Scripts/Journal/JournalSystem.cs` — категории (`JournalCategory`), `_unlocked`, `AddEntry/IsUnlocked`, `ISaveable` (формат `journal` сохранить)
- [x] Проверить `/EscapeOf/Assets/Scripts/Interaction/DocumentInteraction.cs` — хук `AddEntry` в `Interact()` сохранён (единственная точка регистрации)
- [x] Переписать `/EscapeOf/Assets/Scripts/Journal/UI/JournalUI.cs` — доска: категории, стопка 3D-страниц, edge-click flip, камера+RawImage, клавиатура
- [x] Редактировать `/EscapeOf/Assets/Scripts/Journal/UI/JournalEntryButton.cs` — `Setup(string label, Action onClick)` для кнопок категорий
- [x] Перестроить `/Canvas/JournalPanel` в `Game.unity`: удалить EntryList/NoteView, создать CategoryButtons/BoardView/click-зоны, назначить поля JournalUI
- [x] Заполнить категории в инспекторе `JournalSystem` стартовым набором (Дневники/Записки/Книги/Обучение — `/Assets/Data/Notes`); состав правится пользователем в процессе
- [x] Перекомпилировать, проверить консоль, снять изолированный скриншот панели

## Reference

- [@ id="/Pages/Private/Notes.md" label="Notes.md"] — структура `Pages/Page_N/Title|Content` 3D-префабов.
- Референс визуала — изображение доски, приложенное в чате.

## Текущее состояние (итерация «доска без анимации листания»)

- `**JournalUI**` (`/Assets/Scripts/Journal/UI/JournalUI.cs`): мгновенное листание через `ShowPage`; корутины, `_isFlipping`, `_flipDuration`, `_flipCurve` удалены. Кнопки-стрелки: ColorTint-переходы (`WireEdgeZone`), включаются только при наличии предыдущей/следующей страницы (`UpdateEdgeZoneAvailability` в `ShowPage`).
- **Инспектор `/JournalUIRoot` (сцена `Game.unity`):** `_stackRightShift = 0.16`, `_pileShift = 1`, `_pageStackOffset = 0.1`, `_framingMultiplier = 2.2`, `_fixedBoundsSize = (0.44, 0.025, 0.31)`.
- **Кнопки категорий:** `JournalCategoryButton` хранит форс-выделение; `JournalUI` мапит видимые кнопки на индексы категорий через `_categoryButtonIndices`.
- **Документы:** поворот превью из `DocumentData.previewRotation` (`GetPreviewRotation`), масштаб `previewScale` + нормализация в фиксированный кадр (`FitInstanceIntoFixedFrame`).
- **Крайний случай:** категория без разблокированных страниц — стрелки не обновятся (ранний `return` в `ShowPage`); при появлении таких категорий перенести `UpdateEdgeZoneAvailability` до возврата.
- **Play Mode не прогонялся** после последних итераций — проверить визуально: листание, подсветку стрелок, вид стопок.

&nbsp;