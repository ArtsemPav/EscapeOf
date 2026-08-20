Документ — объект в сцене, который игрок может прочитать. При взаимодействии экран затемняется, 3D-модель документа (книга, журнал, записка) вылетает анимацией на передний план, поверх показывается текст. Объект остаётся в сцене — в инвентарь не попадает.

## Шаг 1 — Создать DocumentData

`DocumentData` — ScriptableObject, хранящий все данные о документе.

1. В Project щёлкни правой кнопкой → **Create → Escape → Document Data**
2. Задай поля:


| Поле                 | Описание                                                                                 |
| -------------------- | ---------------------------------------------------------------------------------------- |
| **Title**            | Заголовок документа (отображается крупно вверху панели)                                  |
| **Pages**            | Список страниц. Каждый элемент — отдельная страница. Добавляй новые элементы для +1 стр. |
| **Document Prefab**  | 3D-модель документа (книга, журнал, записка). Если не задан — текст без 3D-превью        |
| **Preview Rotation** | Начальная эйлерова ротация 3D-модели относительно камеры                                 |
| **Preview Scale**    | Множитель масштаба модели в превью. 1 = реальный размер                                  |
| **Font**             | Шрифт основного текста (TMP_FontAsset). Если null — шрифт по умолчанию                   |
| **Font Size**        | Размер шрифта основного текста                                                           |
| **Font Color**       | Цвет основного текста                                                                    |
| **Text Alignment**   | Выравнивание основного текста                                                            |
| **Title Font**       | Шрифт заголовка. Если null — шрифт по умолчанию                                          |
| **Title Font Size**  | Размер шрифта заголовка                                                                  |
| **Title Color**      | Цвет заголовка                                                                           |
| **Title Alignment**  | Выравнивание заголовка                                                                   |
| **Open Clip**        | Звук при открытии документа (опционально)                                                |


## Шаг 2 — Добавить компонент в сцену

1. Выбери 3D-объект документа в сцене (книга, журнал, лист бумаги и т.д.)
2. Добавь компонент `DocumentInteraction`
3. Назначь созданный `DocumentData` в поле **Document Data**
4. При желании измени поле **Interact Text** — это текст подсказки ("Прочитать" по умолчанию)
5. На объекте должен быть **Collider** и слой **Interactable Layer**

## Как это работает

```
Игрок нажимает E → DocumentInteraction.Interact()
  └─ DocumentUI.Open(documentData)
       ├─ ScreenFader.FadeIn(0.85 alpha) → экран затемняется
       ├─ SpawnDocument(prefab) → 3D-модель на слое Inspection
       │    ├─ Камера orthographic + Key/Rim свет (как в ItemInspector)
       │    └─ Программная анимация вылета: scale 0→1, position offset→0, rotation
       ├─ Применяется типографика из DocumentData (шрифт, размер, цвет, выравнивание)
       ├─ Показывается страница 0: title + pages[0] + "1 / N"
       └─ UIManager.OpenPanel() → блокировка ввода игрока

Закрытие (E / Escape):
  └─ Обратная анимация вылета 3D-модели
       ├─ ScreenFader.FadeOut() → экран осветляется
       ├─ UIManager.ClosePanel() → игрок снова управляет персонажем
       └─ Destroy 3D-объекта + сброс
```

## Навигация по страницам


| Кнопка              | Действие            |
| ------------------- | ------------------- |
| `→` / `D` / `Space` | Следующая страница  |
| `←` / `A`           | Предыдущая страница |
| `E` / `Escape`      | Закрыть документ    |
| UI-кнопка `←`       | Предыдущая страница |
| UI-кнопка `→`       | Следующая страница  |


UI-кнопки навигации автоматически становятся неактивными на первой и последней странице. Индикатор "1 / 3" показывается только если страниц больше одной.

## Настройка DocumentUI в сцене

Компонент `DocumentUI` находится на объекте `/Canvas`. Проверь, что назначены:


| Поле DocumentUI       | Что назначить                                              |
| --------------------- | ---------------------------------------------------------- |
| **Panel**             | `/Canvas/DocumentPanel`                                    |
| **Document Preview**  | `RawImage` на `/Canvas/DocumentPanel/DocumentPreview`      |
| **Inspection Camera** | `Camera` на `/InspectionSetup/DocumentCamera`              |
| **Title Text**        | `TextMeshProUGUI` на `/Canvas/DocumentPanel/TitleText`     |
| **Content Text**      | `TextMeshProUGUI` на `/Canvas/DocumentPanel/ContentText`   |
| **Prev Page Button**  | `Button` на `/Canvas/DocumentPanel/PrevPageButton`         |
| **Next Page Button**  | `Button` на `/Canvas/DocumentPanel/NextPageButton`         |
| **Page Indicator**    | `TextMeshProUGUI` на `/Canvas/DocumentPanel/PageIndicator` |


## Настройка анимации

Параметры анимации вылета/улетания настраиваются в инспекторе `DocumentUI`:


| Поле                      | Описание                                           |
| ------------------------- | -------------------------------------------------- |
| **Darken Duration**       | Длительность затемнения экрана (секунды)           |
| **Darken Alpha**          | Непрозрачность затемнения (0–1, по умолчанию 0.85) |
| **Fly In Duration**       | Длительность анимации вылета 3D-объекта            |
| **Fly In Start Offset**   | Стартовое смещение позиции (откуда вылетает)       |
| **Fly In Start Rotation** | Стартовая ротация                                  |
| **Fly In Curve**          | Кривая анимации вылета                             |
| **Fly Out Duration**      | Длительность обратной анимации                     |
| **Fly Out Curve**         | Кривая обратной анимации                           |


## Сцена: структура DocumentPanel

```
Canvas
└── DocumentPanel              # полноэкранная панель, inactive по умолчанию
    ├── DocumentPreview        # RawImage — RenderTexture из DocumentCamera
    ├── TitleText              # TMP — заголовок
    ├── ContentText            # TMP — текст текущей страницы
    ├── PrevPageButton         # Button — "←"
    ├── NextPageButton         # Button — "→"
    ├── PageIndicator          # TMP — "1 / 3"
    └── CloseHint              # TMP — "E / Escape — закрыть"
```