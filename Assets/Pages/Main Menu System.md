## Обзор

Главное меню — **мировая плоскость в комнате**: `MainMenuCanvas` работает в режиме
`WorldSpace`, камера при старте сцены подъезжает к нему (`MenuCameraDolly`),
а при переходе в загрузку — пролетает сквозь кадр к дверям. Затемнения (стартовое
проявление, фейд перехода, экран загрузки) живут на отдельном overlay-канвасе,
чтобы всегда закрывать весь экран. Панель настроек — без тёмной подложки,
элементы прямо поверх комнаты.

---

## Настройки меню в мировом пространстве

**Геометрия.** `MainMenuCanvas`: `sizeDelta 1920x1080`, `localScale 0.00328`,
world pose `pos (2.68, 1.19, 0.698)`, rot `(-8.32, 0, 0)` — плоскость в 3 м перед
камерой в её rest-позе. Панель MAIN вписана от пола до верха кадра (vp 0.24–0.97),
контент левее центра — фонарь остаётся виден справа.

**Подъезд камеры** (`MenuCameraDolly` на `Main Camera`):
- arrival: `_arrivalBackDistance 2.4`, `_arrivalDropHeight 0`, `_arrivalDurationSeconds 3.0`
  — стартовая поза (2.68, 0.13, −4.64) над полом коридора за спиной (пол тянется до
  z=−21.7), при подъезде камера не заглядывает под геометрию комнаты;
- переход в загрузку: `_loadingPushDistance 3`, `_loadingPushDurationSeconds 2.5` —
  пролёт к загрузочному экрану, фейд накрывает его конец.

**Затемнение перехода** (`MainMenuController._transitionFadeSeconds = 2.2`) —
длительность fade-to-black «Новая игра / Загрузить игру»; фейд вызывается с этим
временем, чтобы накрыть удлинённый пролёт камеры.

**Экран загрузки и фейдеры** — на `LoadingOverlayCanvas` (Screen Space Overlay,
sortingOrder 200): `LoadingScreen`, `ScreenFader`, `BackgroundFade`. Перенесены с
`MainMenuCanvas`, потому что в мировом пространстве они накрывали бы только
плоскость меню, а не весь экран. `MainMenuController._loadingScreen` указывает на
`/LoadingOverlayCanvas/LoadingScreen`.

**Панель настроек** — тёмная фоновая `Image` удалена, элементы рендерятся
поверх комнаты. Заголовок «НАСТРОЙКИ» опущен до `anchoredPosition (0, −250)`
и сделан Bold, чтобы не выходил за верх кадра и читался как заголовок.

**Титул «ESCAPE»** (`MAIN/Title`, TMP, fontSize 92, Bold) — `anchoredPosition (160, −265)`
внутри `MAIN`: по центру-верху кадра над дверным проёмом; подбирался визуально.

---

## Иерархия объектов

```
LoadingOverlayCanvas         Canvas (Screen Space Overlay, sortingOrder 200)
├── LoadingScreen            FakeLoadingScreen — экран загрузки с полосой прогресса
├── ScreenFader              Общий fade-to-black (синглтон, ScreenFader.cs)
└── BackgroundFade           Стартовое проявление (MenuBackgroundFade: чёрный 2.5 с)

MainMenuCanvas               Canvas (WorldSpace, Main Camera) — меню в пространстве комнаты
└── MAIN                     Панель меню — анимация WindowPopUpBig
    ├── VerticalLayout       Раскладка кнопок — MenuHoverEffect + MenuSelectionLines
    │   ├── Btn_NewGame      Button
    │   ├── Btn_Settings     Button
    │   └── Btn_Exit         Button
    ├── Corner_Detail        Угловой декор (нижний)
    ├── Corner_Line1         Декоративная линия (floating)
    └── Corner_Line2         Декоративная линия (floating)
```

---

## Скрипты

### `MenuHoverEffect`

Находится на `VerticalLayout`. Управляет hover-анимацией кнопок и декоративных элементов.

**Инициализация (Start + WaitForEndOfFrame):**

1. Ждёт один кадр — за это время `VerticalLayoutGroup` раскладывает кнопки и устанавливает их `anchoredPosition`
2. Находит все дочерние объекты с компонентом `Button` и сохраняет их исходные позиции
3. Добавляет `EventTrigger` (PointerEnter / PointerExit) к каждой кнопке через код
4. Инициализирует позиции декоративных элементов
5. **Отключает `VerticalLayoutGroup**` — с этого момента позиции кнопок управляются вручную

**При наведении на кнопку с индексом `i`:**

- Кнопки с индексом `< i` → цель смещается вверх на `_slideDistance`
- Кнопки с индексом `> i` → цель смещается вниз на `_slideDistance`
- Кнопка `i` → остаётся на месте
- `_topFrameElements` → смещаются вверх на `_slideDistance` (если кнопка `i` не первая)
- `_bottomFrameElements` → смещаются вниз на `_slideDistance` (если кнопка `i` не последняя)
- `_floatingElements` → каждый элемент получает тот же сдвиг, что и ближайшая к нему по Y кнопка

**Update:** каждый кадр позиции всех элементов плавно интерполируются (`Vector2.Lerp`) к целевым значениям с коэффициентом `Time.unscaledDeltaTime * _animationSpeed`. Используется **unscaled time** — анимация работает при `Time.timeScale = 0`.


| Поле                   | Описание                                                                                               |
| ---------------------- | ------------------------------------------------------------------------------------------------------ |
| `_slideDistance`       | Расстояние в пикселях, на которое смещаются кнопки.                                                    |
| `_animationSpeed`      | Скорость Lerp-интерполяции.                                                                            |
| `_topFrameElements`    | Декоративные элементы верхней границы рамки. Уезжают вверх вместе с верхней кнопкой.                   |
| `_bottomFrameElements` | Декоративные элементы нижней границы рамки. Уезжают вниз вместе с нижней кнопкой.                      |
| `_floatingElements`    | Декоративные элементы, привязанные к ближайшей кнопке. Двигаются вместе с ней, не перекрывая соседние. |


**Текущие назначения в сцене:**


| Слот                   | Объекты                        |
| ---------------------- | ------------------------------ |
| `_bottomFrameElements` | `Corner_Detail`                |
| `_floatingElements`    | `Corner_Line1`, `Corner_Line2` |


---

## Пост-процессинг

`GameManager.SetPause(bool)` при паузе глушит весь звук через `AudioListener.pause` и переключает музыку через `MusicDirector` (`PlayMenuMusic` / `PlayGameMusic`). Пост-обработку (Volume) `GameManager` не трогает.

> `MenuScript` упразднён — вся логика паузы, меню и подписки на ESC находится исключительно в `GameManager`.

---

## `MainMenuController`

**Скрипт:** `Assets/Scripts/Core/MainMenuController.cs`

Компонент на `MainMenuCanvas`. Обрабатывает нажатия кнопок главного меню.
Кнопки подключаются через persistent `onClick` в Inspector.

| Метод          | Описание                                                       |
| -------------- | -------------------------------------------------------------- |
| `OnNewGame()`  | Чистит слот 0 и запускает переход в игровую сцену               |
| `OnLoadGame()` | Загружает слот 0 (скрывает кнопку, если сейв не найден)         |
| `OnSettings()` | Показывает `SettingsPanel`, прячет `MAIN`                       |
| `OnSettingsClosed()` | Обратно: прячет настройки, возвращает `MAIN`              |
| `OnExit()`     | Закрывает приложение (в Editor — выходит из Play)               |

| Поле                     | Описание                                                    |
| ------------------------ | ----------------------------------------------------------- |
| `_settingsPanel`         | Панель настроек для кнопки Settings                         |
| `_menuRoot`              | Корень меню (`MAIN`), прячется при открытии настроек         |
| `_loadingScreen`         | `FakeLoadingScreen` (лежит на `LoadingOverlayCanvas`)        |
| `_loadGameButton`        | Кнопка «Загрузить игру» — скрывается, если сейва нет         |
| `_cameraDolly`           | `MenuCameraDolly` — пуш камеры при переходе в загрузку       |
| `_hoverClip` / `_clickClip` | Звуки ховера/клика (см. «Звуки меню»)                     |
| `_transitionFadeSeconds` | Длительность fade-to-black перехода в загрузку (2.2)         |

---

## Панель настроек: кадры в секунду

**Скрипты:** `Assets/Scripts/Core/VideoSettings.cs` (список `FrameRates`), `Assets/Scripts/Core/SystemSettings.cs`, `Assets/Scripts/UI/SettingsPanelController.cs`

Кнопка «КАДРЫ В СЕКУНДУ» в панели настроек сцены Start перебирает список
`VideoSettingOptions.FrameRates` (60 — по умолчанию, 30) и сразу применяет выбор:
`QualitySettings.vSyncCount = 0` (иначе лимит игнорируется) + `Application.targetFrameRate`.
Значение хранится в `Settings_TargetFrameRate` и восстанавливается при старте через
`SystemSettings.ApplyAtStartup()` (вызывает `ResolutionManager` на объекте
`VideoSettingsApplier` в сценах Start и Game).

Кнопка и её текст сидят в `SettingsPanel/FrameRateButton`; клики подключены
persistent onClick к `SettingsPanelController.CycleFrameRate` — по одному вызову
на клик (дубли кодом не добавлять: двухшаговый цикл возвращает то же значение).

---

## Панель настроек: разрешение экрана и режим окна

**Скрипты:** `Assets/Scripts/Core/VideoSettings.cs`, `Assets/Scripts/Core/SystemSettings.cs`, `Assets/Scripts/UI/SettingsPanelController.cs`

Список разрешений строится динамически из режимов, которые поддерживает текущий монитор: на каждое соотношение сторон остаются два наибольших режима (нативный и одна ступень вниз для производительности), отсортированные от большего к меньшему, — включая чужие пропорции. Растяжение исключается режимом окна: по умолчанию borderless fullscreen (`FullScreenWindow`), который при несовпадении пропорций выбранного разрешения сам добавляет чёрные полосы по краям. Режим окна переключается кнопкой в настройках (без рамки / оконный); исключительный fullscreen в списке отсутствует — драйвер монитора растягивает там несовпадающие пропорции, а это вне контроля Unity.

- Выбор сохраняется в PlayerPrefs по ширине/высоте (`Settings_ResolutionWidth` / `Settings_ResolutionHeight`) и режиму окна (`Settings_FullScreenMode`), а не по индексу списка — выбор переживает смену монитора; старый индексный ключ `Settings_ResolutionIndex` читается как fallback.
- По умолчанию берутся нативное разрешение дисплея и borderless fullscreen; при отсутствии сохранённого выбора применяется именно эта пара.
- Кнопка «ОКНО» есть в панелях настроек обеих сцен: Start (`/MainMenuCanvas/SettingsPanel`) и Game (`/Canvas/MainMenu` — панель только со звуком, режим окна там не переключается).

---

## Звуки меню

Схема: ховер — `S_Click3`, клик — `S_Click2` (обе из `/Assets/sound/buttonClick/`, громкость `0.7`). Звуки идут через `AudioManager.PlaySFX` — шина SFX микшера (см. Audio System).

Оба звука встроены в `MainMenuController` (поля `Hover Clip` / `Click Clip`): в `Awake` он автоматически находит **все** `Button` под `MainMenuCanvas` (включая выключенное подменю настроек) и подписывает их на ховер (`PointerEnter`) и клик (`onClick`). Отдельные компоненты на кнопках не нужны — новая кнопка получает звуки автоматически.

## Добавление новой кнопки

1. Добавь дочерний объект с компонентом `Button` в `VerticalLayout`
2. `MenuHoverEffect` обнаружит её автоматически при следующем запуске — явная регистрация не нужна
3. Звуки ховера и клика `MainMenuController` подключит автоматически

## Добавление нового декоративного элемента

Определи, какой тип поведения подходит, и добавь `RectTransform` в соответствующий слот `MenuHoverEffect` в Inspector:


| Поведение                            | Слот                    |
| ------------------------------------ | ----------------------- |
| Двигается вверх с верхней кнопкой    | `Top Frame Elements`    |
| Двигается вниз с нижней кнопкой      | `Bottom Frame Elements` |
| Двигается вместе с ближайшей кнопкой | `Floating Elements`     |