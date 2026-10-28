Документ — объект в сцене, который игрок может прочитать. При взаимодействии экран затемняется, 3D-модель документа (книга, журнал, записка) вылетает анимацией на передний план. Текст запечён прямо в префабе как дочерние `TextMeshPro` (3D) объекты — рендерится document-камерой вместе с моделью. Объект остаётся в сцене — в инвентарь не попадает. Прочитанные документы попадают в `JournalSystem`.

## Шаг 1 — Создать DocumentData

**Скрипт:** `Assets/Scripts/Data/DocumentData.cs` — ScriptableObject, хранящий визуальные настройки документа. Текст контента не здесь — он запечён в префабе.

1. В Project щёлкни правой кнопкой → **Create → Escape → Document Data**
2. Задай поля:


| Поле                            | По умолчанию   | Описание                                                                        |
| ------------------------------- | -------------- | ------------------------------------------------------------------------------- |
| **Is Note**                     | `false`        | Если включено — короткая записка: 1 страница, без стрелок и анимации переворота |
| **Document Prefab**             | —              | 3D-префаб документа с запечённым TextMeshPro (3D) текстом на страницах          |
| **Preview Scale**               | `1`            | Множитель масштаба модели в превью. 1 = реальный размер                         |
| **Preview Dim Amount**          | `0.35`         | Затемнение 3D-превью. 0 = нет, 1 = полностью чёрный                             |
| **Use Custom Preview Rotation** | `false`        | Включить индивидуальную ротацию для этого документа                             |
| **Preview Rotation**            | `(15, -35, 0)` | Эйлеровы углы начальной ротации (активно при useCustomPreviewRotation = true)   |
| **Open Clip**                   | —              | Звук при открытии документа (опционально)                                       |
| **Page Turn Clip**              | —              | Звук перелистывания страницы (опционально)                                      |
| **Close Clip**                  | —              | Звук при закрытии документа (опционально)                                       |


## Шаг 2 — Добавить компонент в сцену

**Скрипт:** `Assets/Scripts/Interaction/DocumentInteraction.cs` (`IInteractable`).

1. Выбери 3D-объект документа в сцене (книга, журнал, лист бумаги и т.д.)
2. Добавь компонент `DocumentInteraction`
3. Назначь созданный `DocumentData` в поле **Document Data**
4. При желании измени поле **Interact Text** — текст подсказки («Прочитать» по умолчанию)
5. На объекте должен быть **Collider** и слой **Interactable Layer**

Взаимодействие — одиночный клик ЛКМ (`UseLMBClick => true`), иконка прицела `CrosshairMode.Read`. В `Interact()`: `DocumentUI.Instance.Open(data)` и `JournalSystem.Instance.AddEntry(data)` (журнал сам игнорирует дубли — повторное чтение безопасно; запись добавляется только если панель реально открылась).

## Шаг 3 — Настроить текст в префабе

Текст запечён в префабе документа как дочерние `TextMeshPro` (3D, не UGUI) компоненты. Каждая страница — отдельный GameObject, внутри которого можно свободно размещать Title, Content, картинки и любые другие элементы:

```
Paper                          # меши бумаги + Collider
└── Pages                      # контейнер (имя = "Pages" по умолчанию)
    ├── Page_0                 # Empty GameObject — страница 1
    │   ├── Title              # TextMeshPro (3D) — заголовок
    │   └── Content            # TextMeshPro (3D) — текст страницы
    ├── Page_1                 # Empty GameObject — страница 2
    │   ├── Title
    │   └── Content
    └── Page_2                 # Empty GameObject — страница 3
        ├── Title
        └── Content
```

1. Открой префаб в Prefab Mode
2. Создай child `Pages` (Empty)
3. Создай child `Page_0` (Empty) — это контейнер страницы
4. Внутри `Page_0` создай `Title` → Add Component → `TextMeshPro` (3D, не UGUI)
5. Внутри `Page_0` создай `Content` → Add Component → `TextMeshPro` (3D)
6. Размести TMP чуть выше поверхности бумаги (offset по нормали ~0.001)
7. Настрой RectTransform: размер = габариты бумаги
8. Введи текст с rich text форматированием:
   - Title: `<b>Заголовок</b>`
   - Content: основной текст, `<sprite name="icon_name">` для картинок
9. Повтори для дополнительных страниц (Page_1, Page_2, ...)

Страницы — дочерние GameObject'ы контейнера `Pages` (`FindPages` ищет контейнер рекурсивно по имени `_pagesContainerName`), сортируются по имени. При перелистывании вся страница (со всеми дочерними TMP) скрывается/показывается целиком.

Для анимации «печатной машинки» на 3D-тексте есть `TypewriterEffectPro` (см. ниже).

## Как это работает

```
Игрок кликает ЛКМ → DocumentInteraction.Interact()
  └─ DocumentUI.Open(documentData)
       ├─ SpawnDocument(prefab) → visual-only клон на слое Inspection
       │    ├─ InstantiatePreview: клон без ISaveable-компонентов (защита сейв-системы)
       │    ├─ FindPages() → сбор дочерних GameObject'ов контейнера Pages
       │    ├─ Камера orthographic (RT 1536×1024, aspect 1.5) + Key/Rim свет
       │    └─ Fixed bounds — все документы кадрируются одинаково
       ├─ ShowPage(0) → активны 3 страницы (previous/current/next)
       ├─ PageIndicator: "1 / N" (если страниц > 1 и isNote = false)
       ├─ ScreenFader.FadeIn(_darkenDuration, _darkenAlpha) → экран затемняется
       ├─ UIManager.OpenPanel() → блокировка ввода игрока
       └─ ScaleInRoutine → программная анимация scale 0→1

Закрытие (E / Escape / клик вне документа):
  └─ Звук closeClip → ScaleOutRoutine (scale-out 3D-модели)
       ├─ ScreenFader.FadeOut() → экран осветляется
       ├─ UIManager.ClosePanel() → игрок снова управляет персонажем
       └─ Destroy 3D-объекта + сброс
```

### Click to Close

`_allowClickToClose` (вкл. по умолчанию): клик ЛКМ/ПКМ вне 3D-превью и вне UI-элементов закрывает документ. Клик по самой записке (hit-test по проекции её bounds в RawImage, `_noteHalfFrac` с запасом 5%) и клик по элементам панели документа не закрывают его.

## Модель 3 страниц

При `isNote = false` одновременно активны 3 страницы (если есть):


| Роль         | Угол | Позиция            | Описание                      |
| ------------ | ---- | ------------------ | ----------------------------- |
| **previous** | 180° | базовая            | Уже прочитанная, перевёрнутая |
| **current**  | 0°   | базовая + Y offset | Текущая читаемая, приподнята  |
| **next**     | 0°   | базовая            | Следующая, за current         |


### NextPage (→)

1. **Current** анимируется 0 → 180° (поворот по Z, кривая `_pageFlipCurve`)
2. **Old previous** выключается
3. **Old current** (теперь previous) опускается в базовую позицию (180°, base)
4. **Old next** (теперь current) поднимается с Y offset (0°, offset)
5. **New next** активируется (0°, base) — если есть

### PrevPage (←)

1. **Old next** выключается
2. **Old current** теряет offset → становится next (0°, base)
3. **Previous** получает offset → становится current (180°, offset)
4. **New previous** активируется (180°, base) — если есть
5. **New current** анимируется 180 → 0° (раскрытие обратно)

### Одна страница, isNote = false

- **NextPage (→)** — страница анимируется 0 → 180° и остаётся перевёрнутой
- **PrevPage (←)** — страница анимируется 180 → 0° и возвращается в исходное положение
- Повторное нажатие на пределе — без анимации
- Звук перелистывания играет на каждой навигации

## Навигация по страницам


| Кнопка              | Действие                           |
| ------------------- | ---------------------------------- |
| `→` / `D` / `Space` | Следующая страница                 |
| `←` / `A`           | Предыдущая страница                |
| `E` / `Escape`      | Закрыть документ                   |
| Клик вне документа  | Закрыть (при `_allowClickToClose`) |
| UI-кнопка `←`       | Предыдущая страница                |
| UI-кнопка `→`       | Следующая страница                 |


UI-кнопки навигации становятся неактивными (`interactable = false`) на первой и последней странице (при нескольких страницах). При `isNote = true` кнопки и индикатор скрыты полностью. Индикатор «1 / 3» показывается только если страниц больше одной и `isNote = false`.

## Настройка DocumentUI в сцене

**Скрипт:** `Assets/Scripts/UI/DocumentUI.cs` — Singleton (`Instance`). Компонент на объекте `/Canvas`. Поля:


| Поле DocumentUI           | Назначение                                                                                              |
| ------------------------- | ------------------------------------------------------------------------------------------------------- |
| **Panel**                 | `/Canvas/DocumentPanel`                                                                                 |
| **Document Preview**      | `RawImage` на `/Canvas/DocumentPanel/DocumentPreview` (AspectRatioFitter 1.5 добавляется автоматически) |
| **Inspection Camera**     | `Camera` на `/InspectionSetup/DocumentCamera`                                                           |
| **Pages Container Name**  | Имя контейнера страниц в префабе (по умолчанию «Pages»)                                                 |
| **Prev/Next Page Button** | `Button` навигации                                                                                      |
| **Page Indicator**        | `TextMeshProUGUI` — «1 / 3»                                                                             |
| **Allow Click To Close**  | Клик-вне закрытие (по умолчанию вкл.)                                                                   |
| **Click Safe Area**       | RectTransform, клики внутри которого не закрывают (по умолчанию — превью)                               |


## Настройка анимации и камеры

Параметры в инспекторе `DocumentUI`:


| Поле                           | По умолчанию          | Описание                                                                                  |
| ------------------------------ | --------------------- | ----------------------------------------------------------------------------------------- |
| **Darken Duration**            | `0.4`                 | Длительность затемнения экрана (секунды)                                                  |
| **Darken Alpha**               | `0.85`                | Непрозрачность затемнения (0–1)                                                           |
| **Page Flip Duration**         | `0.4`                 | Длительность анимации переворота страницы (секунды)                                       |
| **Page Flip Curve**            | EaseInOut             | Кривая анимации переворота                                                                |
| **Page Stack Offset**          | `0.001`               | Сдвиг current-страницы по локальной Y над стопкой                                         |
| **Scale In Duration/Curve**    | `0.5` / EaseInOut     | Анимация появления 3D-объекта                                                             |
| **Scale Out Duration/Curve**   | `0.35` / EaseInOut    | Анимация исчезновения                                                                     |
| **Inspection Layer Name**      | `Inspection`          | Слой рендера превью                                                                       |
| **Framing Multiplier**         | `2.2`                 | Множитель дистанции камеры (больше = модель меньше)                                       |
| **Initial Rotation**           | `(15, -35, 0)`        | Глобальная начальная ротация (когда в DocumentData нет кастомной)                         |
| **Fixed Bounds Size**          | `(0.44, 0.025, 0.31)` | Фиксированный bounds — все книги кадрируются одинаково, независимо от содержимого страниц |
| **Fixed Bounds Center Offset** | `(0, 0.006, 0)`       | Смещение центра bounds — позиция пивота и камеры                                          |


Анимации идут на `Time.unscaledDeltaTime` — работают при паузе.

## Сцена: структура DocumentPanel

```
Canvas
└── DocumentPanel              # полноэкранная панель, inactive по умолчанию
    ├── DocumentPreview        # RawImage — RenderTexture (1536×1024) из DocumentCamera
    ├── PrevPageButton         # Button — "←"
    ├── NextPageButton         # Button — "→"
    ├── PageIndicator          # TMP — "1 / 3"
    └── CloseHint              # TMP — "E / Escape — закрыть"
```

---

## TypewriterEffectPro

**Скрипт:** `Assets/Scripts/Text/TypewriterEffectPro.cs` — анимация «печатной машинки» для `TMP_Text`.

- Вешается на объект с `TMP_Text`; при `playOnStart = true` запускается в `Start`
- `StartTypewriter()` — печатает текущий текст посимвольно через `maxVisibleCharacters` (rich text теги обрабатываются корректно — ждёт один кадр на обработку тегов)
- `SkipToEnd()` — мгновенно показать весь текст
- Поле `typingSpeed` — задержка между символами (по умолчанию 0.05 с)

Подходит для записок с эффектом печатания; для книг с готовым текстом не нужен.
