## Обзор

Главное меню рендерится поверх сцены в режиме `ScreenSpaceCamera` через `Main Camera`. Переходы между экранами идут через общий `ScreenFader` (фейд в чёрное, подмена панели, обратный фейд). Переход в игру — через фейковый экран загрузки с фоновой подгрузкой сцены.

---

## Иерархия объектов

```
MainMenuCanvas              Canvas (ScreenSpaceCamera, Main Camera) + MainMenuController
└── MAIN                    Корень меню (_menuRoot) — анимация WindowPopUpBig
    ├── VerticalLayout      Раскладка кнопок — MenuHoverEffect + MenuSelectionLines
    │   ├── Btn_NewGame     Button → MainMenuController.OnNewGame
    │   ├── Btn_LoadGame    Button → MainMenuController.OnLoadGame (скрыта, если сейва нет)
    │   ├── Btn_Settings    Button → MainMenuController.OnSettings
    │   └── Btn_Exit        Button → MainMenuController.OnExit
    ├── Corner_Detail       Угловой декор (нижний) — _bottomFrameElements
    ├── Corner_Line1        Декоративная линия (floating)
    └── Corner_Line2        Декоративная линия (floating)
```

---

## Скрипты

### `MainMenuController` — `Assets/Scripts/Core/MainMenuController.cs`

Компонент на `MainMenuCanvas`. Обрабатывает кнопки главного меню (подключение через Inspector `onClick`), автоматически подключает hover- и click-звуки ко всем `Button` под канвасом (включая неактивные панели подменю) через `AudioManager.PlaySFX`.


| Метод                | Описание                                                                                 |
| -------------------- | ---------------------------------------------------------------------------------------- |
| `OnNewGame()`        | `SaveManager.StartNewGame(0)` (чистит слот + запрашивает wake-up intro) → переход в игру |
| `OnLoadGame()`       | Проверяет `SaveManager.SlotHasSave(0)`, ставит `RequestLoadFromSlot(0)` → переход в игру |
| `OnSettings()`       | Скрывает `_menuRoot`, показывает `_settingsPanel`                                        |
| `OnSettingsClosed()` | Обратно: скрывает настройки, показывает меню                                             |
| `OnExit()`           | Выход из приложения (в Editor — выход из Play)                                           |



| Поле                                                          | Описание                                                          |
| ------------------------------------------------------------- | ----------------------------------------------------------------- |
| `_settingsPanel`                                              | Панель настроек                                                   |
| `_menuRoot`                                                   | Корень блока меню, скрывается при открытии настроек               |
| `_loadingScreen`                                              | `FakeLoadingScreen` перед переключением в геймплейную сцену       |
| `_transitionFadeSeconds`                                      | Длительность фейда в чёрное (2.2 с); должна покрывать camera push |
| `_loadGameButton`                                             | Кнопка Load Game — скрывается в `Start`, если сейва нет           |
| `_cameraDolly`                                                | `MenuCameraDolly` для push-forward бита при переходе              |
| `_hoverClip` / `_clickClip` / `_hoverVolume` / `_clickVolume` | Звуки меню (громкость 0.7)                                        |


### Переход в игру (`BeginGameTransition`)

```
OnNewGame / OnLoadGame
  └─ BeginGameTransition()
       ├─ MusicDirector.FadeOutForLoadingScreen() — музыка затихает
       ├─ _cameraDolly.BeginLoadingPush() — камера подаётся вперёд
       └─ GameTransitionRoutine:
            ├─ ScreenFader.FadeIn(_transitionFadeSeconds) — экран в чёрное
            ├─ _menuRoot и настройки скрываются
            ├─ _loadingScreen.BeginLoading() — появляется экран загрузки
            └─ ScreenFader.FadeOut() — загрузка видна уже на чистом экране
```

Wake-up intro триггерится самим `SaveManager`: после очистки слота `SaveManager.Start()` в игровой сцене не находит сейва и запрашивает интро — единая точка решения для всех путей входа.

### `MenuCameraDolly` — `Assets/Scripts/Core/MenuCameraDolly.cs`

На камере меню. «Прибытие» в сцену: камера стартует со смещением назад (по view-направлению, чуть ниже) и плавно дрейфует в rest-позу с ease-out, пока стартовый фейд открывает сцену. Также проигрывает короткий push-forward при переходе к экрану загрузки (`BeginLoadingPush`, ease-in quad).


| Поле                          | По умолчанию | Описание                                                   |
| ----------------------------- | ------------ | ---------------------------------------------------------- |
| `_arrivalBackDistance`        | `2.2`        | Насколько позади rest-позы стартует камера (м)             |
| `_arrivalDropHeight`          | `0.35`       | Вертикальный сдвиг стартовой позы вниз (м)                 |
| `_arrivalDurationSeconds`     | `4`          | Длительность дрейфа (синхронизирована со стартовым фейдом) |
| `_loadingPushDistance`        | `1.2`        | Дистанция push-forward при переходе (м)                    |
| `_loadingPushDurationSeconds` | `1.2`        | Длительность push (должна закончиться под фейдом)          |


При `OnDisable` камера мгновенно возвращается в rest-позу. Всё на `Time.unscaledDeltaTime` (с ограничением кадра 0.05 с против хичей).

### `ScreenFader` — `Assets/Scripts/Core/ScreenFader.cs`

Singleton на Canvas с `CanvasGroup`. Полноэкранный фейд-оверлей: `FadeIn()` (в чёрное), `FadeIn(duration, targetAlpha)`, `FadeOut()` (в прозрачность). На `Time.unscaledDeltaTime`; при alpha > 0 блокирует raycast. Используется `MainMenuController`, `FakeLoadingScreen` и другими системами.

### `MenuBackgroundFade` — `Assets/Scripts/UI/MenuBackgroundFade.cs`

На полноэкранном `Image`-оверлее канваса меню. При старте сцены закрывает экран чёрным и плавно открывает меню из чёрного (smooth-step easing), параллельно с pop-up анимацией меню.


| Поле                      | По умолчанию | Описание                                     |
| ------------------------- | ------------ | -------------------------------------------- |
| `_overlay`                | —            | Overlay Image, анимируется alpha             |
| `_delayBeforeFadeSeconds` | `0.5`        | Пауза, чтобы pop-up анимация успела начаться |
| `_fadeDurationSeconds`    | `2.5`        | Длительность фейда (с)                       |


### `MenuHoverEffect` — `Assets/Scripts/UI/MenuHoverEffect.cs`

На `VerticalLayout`. Управляет hover-анимацией кнопок и декоративных элементов.

**Инициализация (Start + WaitForEndOfFrame):**

1. Ждёт один кадр — за это время `VerticalLayoutGroup` раскладывает кнопки
2. Находит все дочерние объекты с компонентом `Button` и сохраняет их исходные позиции
3. Добавляет `EventTrigger` (PointerEnter / PointerExit) к каждой кнопке через код
4. Инициализирует позиции декоративных элементов
5. **Отключает `VerticalLayoutGroup`** — с этого момента позиции кнопок управляются вручную

**При наведении на кнопку с индексом `i`:**

- Кнопки с индексом `< i` → цель смещается вверх на `_slideDistance`
- Кнопки с индексом `> i` → цель смещается вниз на `_slideDistance`
- Кнопка `i` → остаётся на месте
- `_topFrameElements` → смещаются вверх (если кнопка `i` не первая)
- `_bottomFrameElements` → смещаются вниз (если кнопка `i` не последняя)
- `_floatingElements` → каждый элемент получает тот же сдвиг, что и ближайшая к нему по Y кнопка

**Update:** позиции плавно интерполируются (`Vector2.Lerp`) с `Time.unscaledDeltaTime * _animationSpeed` — анимация работает при `Time.timeScale = 0`. При уходе курсора все элементы возвращаются на исходные позиции.


| Поле                   | По умолчанию | Описание                                                               |
| ---------------------- | ------------ | ---------------------------------------------------------------------- |
| `_slideDistance`       | `55`         | Расстояние в пикселях, на которое смещаются кнопки                     |
| `_animationSpeed`      | `12`         | Скорость Lerp-интерполяции                                             |
| `_topFrameElements`    | —            | Декор верхней границы рамки                                            |
| `_bottomFrameElements` | —            | Декор нижней границы рамки (`Corner_Detail`)                           |
| `_floatingElements`    | —            | Декор, движущийся с ближайшей кнопкой (`Corner_Line1`, `Corner_Line2`) |


### `MenuSelectionLines` — `Assets/Scripts/UI/MenuSelectionLines.cs`

На `VerticalLayout`. Двигает две горизонтальные линии-разделителя так, чтобы они обрамляли активную (hover) кнопку меню. Линии плавно скользят на unscaled time и остаются на последней hover-кнопке, когда курсор уходит. При старте первая кнопка выбирается автоматически.


| Поле              | По умолчанию | Описание                          |
| ----------------- | ------------ | --------------------------------- |
| `_topLine`        | —            | Линия над активным пунктом        |
| `_bottomLine`     | —            | Линия под активным пунктом        |
| `_padding`        | `8`          | Зазор между линией и краем кнопки |
| `_animationSpeed` | `12`         | Скорость скольжения               |


### `FakeLoadingScreen` — `Assets/Scripts/UI/FakeLoadingScreen.cs`

Фейковый полноэкранный экран загрузки с анимированным прогресс-баром. Геймплейная сцена грузится в фоне (`SceneManager.LoadSceneAsync`, `allowSceneActivation = false`, приоритет `BelowNormal`); сцена активируется, когда бар полон и сцена готова — кадр никогда не фризится на синхронной загрузке.

Публичное API: `Show()` (показать с нулевым баром) и `BeginLoading()` (показать и заполнить).

Константы: заполнение `2.2` с (ease-out, бар держится на 99% до готовности сцены), пауза на полном баре `0.2` с, head-start `1.2` с — тяжёлая загрузка откладывается, пока reveal-фейд не отыграл. Перед активацией сцены — фейд в чёрное через `ScreenFader`.


| Поле              | По умолчанию | Описание                                 |
| ----------------- | ------------ | ---------------------------------------- |
| `_progressSlider` | —            | Slider-прогрессбар (интерактив отключён) |
| `_progressLabel`  | —            | Опциональная подпись «%»                 |
| `_sceneToLoad`    | `"Game"`     | Геймплейная сцена                        |


---

## Настройки (ResolutionManager / SystemSettings / VideoSettings)

### `SystemSettings` — `Assets/Scripts/Core/SystemSettings.cs`

Статический класс настроек игрока в PlayerPrefs. Громкости (`MusicVolume` / `SfxVolume`, 0–1) пишутся в dB в exposed-параметры микшера (`ToDecibels`: 0 → −80 dB), слайдеры применяются мгновенно через `AudioManager.ResolveMixerExternal()`. Ключи префов: `Settings_MusicVolume`, `Settings_SfxVolume`, `Settings_QualityLevel`, `Settings_ResolutionWidth/Height`, `Settings_TargetFrameRate`, `Settings_FullScreenMode`.

- `SetMusicVolume` / `SetSfxVolume` / `SetQualityLevel` / `SetResolution` / `SetTargetFrameRate` / `SetFullScreenMode` — применяют и сохраняют.
- `ApplyAtStartup()` — восстановление quality, window mode, разрешения и frame rate при запуске.
- Дефолтный window mode — borderless `FullScreenWindow`: несовпадающие пропорции letterbox'ятся чёрными полосами вместо растяжения. Exclusive fullscreen никогда не восстанавливается (ремапится в borderless).
- Разрешение хранится размерами (не индексом списка) — выбор переживает смену монитора; есть fallback на legacy-преф `Settings_ResolutionIndex`.

### `VideoSettingOptions` (файл `VideoSettings.cs`) — `Assets/Scripts/Core/VideoSettings.cs`

Статические опции для UI настроек: `Resolutions` — два крупнейших режима на каждое соотношение сторон, которое выдаёт дисплей (короткий список, включая не-нативные пропорции); `FrameRates = {60, 30}`; `DefaultFrameRate = 60`; `DefaultResolution` — текущая (нативная) или крупнейшая поддерживаемая. `ResolutionLabels()` — строки для UI.

### `ResolutionManager` — `Assets/Scripts/Core/ResolutionManager.cs`

Singleton, `[DefaultExecutionOrder(-50)]` — в `Awake` вызывает `SystemSettings.ApplyAtStartup()` в каждой сцене, чтобы видеонастройки применились раньше, чем их прочитает геймплей. Сама логика настроек — в `SystemSettings`, UI — в `SettingsPanelController` (`Assets/Scripts/UI/SettingsPanelController.cs`).

---

## Пауза и меню паузы

Логика паузы (ESC, `Time.timeScale`, курсор, музыка) живёт в `GameManager`:

- `SetPause(bool)`: `Time.timeScale = 0/1`, `AudioListener.pause = pause` — глушится весь звук, кроме источников с `ignoreListenerPause` (только музыка меню паузы), открывается `menuUI` через `UIManager`, музыка переключается `MusicDirector.PlayMenuMusic()` / `PlayGameMusic()`.
- ESC сначала закрывает открытый таб-инвентарь и не открывает паузу, пока открыта любая другая панель (`UIManager.IsAnyPanelOpen`).
- Курсор: разлочен при паузе или любой открытой панели (`UpdateCursorState`).

> `MenuScript` упразднён — класс пустой (deprecated), компонент можно смело удалить со сцены.

---

## Добавление новой кнопки

1. Добавь дочерний объект с компонентом `Button` в `VerticalLayout`
2. `MenuHoverEffect` и `MenuSelectionLines` обнаружат её автоматически при следующем запуске — явная регистрация не нужна
3. Звук hover/click подключится автоматически — их вешает `MainMenuController.WireMenuSounds()` на все кнопки канваса

## Добавление нового декоративного элемента

Определи, какой тип поведения подходит, и добавь `RectTransform` в соответствующий слот `MenuHoverEffect` в Inspector:


| Поведение                            | Слот                    |
| ------------------------------------ | ----------------------- |
| Двигается вверх с верхней кнопкой    | `Top Frame Elements`    |
| Двигается вниз с нижней кнопкой      | `Bottom Frame Elements` |
| Двигается вместе с ближайшей кнопкой | `Floating Elements`     |
