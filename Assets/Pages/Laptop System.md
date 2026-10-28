## Обзор

Интерактивный ноутбук с виртуальной ОС: игрок входит в «puzzle mode» через `PuzzleModeController`, видит экран логина, вводит пароль с клавиатуры и попадает на рабочий стол с иконками. Двойной клик по иконке открывает окно просмотра файла (текст, изображение, аудио, видео, многостраничный документ). Содержимое файлов задаётся ScriptableObject-ассетами с уникальным `fileId` — стабильным идентификатором для системы сохранений.

Ноутбук — потребитель питания (`IPowerConsumer`): без электричества экран выключен, ввод заблокирован, медиа во всех окнах ставятся на паузу. Прогресс (разблокировка + последний открытый файл) сохраняется через Save System.

Скрипты: `Assets/Scripts/Puzzle/LaptopScripts/`.

---

## Структура системы

```
LaptopContainer
├── LaptopOS                  # ОС: логин → рабочий стол, пароль, питание, сейв
├── LaptopController          # (устаревший вариант) отдельная парольная загадка без ОС
├── PuzzleModeController      # вход/выход из puzzle mode (общий компонент загадок)
└── Canvas
    ├── LoginScreen
    │   ├── PasswordField (TMP_InputField)
    │   ├── StatusText
    │   └── Submit button → LaptopOS.SubmitPassword()
    ├── DesktopScreen
    │   └── IconGrid
    │       ├── LaptopDesktopIcon (TextFile)     → _windowManager.OpenFile
    │       ├── LaptopDesktopIcon (ImageFile)    → _windowManager.OpenFile
    │       └── ...
    └── WindowContainer
        ├── LaptopWindowManager  # менеджер открытых окон (внутри DesktopScreen)
        ├── LaptopTextWindow (prefab instance)
        ├── LaptopImageWindow (prefab instance)
        └── ...
```

---

## Компоненты

### `LaptopOS`

Файл: `Assets/Scripts/Puzzle/LaptopScripts/LaptopOS.cs`. Главный контроллер ОС. Размещается на `LaptopContainer` рядом с `PuzzleModeController` (`[RequireComponent]`). Реализует `ISaveable`, `IPowerConsumer`. Управляет переходом Login → Desktop, вводом пароля, реакцией на питание и сейвом.

**Inspector — Save**

| Поле     | Описание                                                                                   |
| -------- | ------------------------------------------------------------------------------------------ |
| `_saveId` | Уникальный ID сохранения. Контекстное меню `Generate Save ID` генерирует GUID (default: пусто) |

**Inspector — Password**

| Поле        | Описание                                        |
| ----------- | ----------------------------------------------- |
| `_password` | Пароль для разблокировки (default: `"1234"`, сравнение без учёта регистра) |

**Inspector — Screens**

| Поле             | Описание                                  |
| ---------------- | ----------------------------------------- |
| `_loginScreen`   | GameObject экрана логина                  |
| `_desktopScreen` | GameObject рабочего стола                 |

**Inspector — Login UI**

| Поле             | Описание                                                                  |
| ---------------- | ------------------------------------------------------------------------- |
| `_passwordField` | `TMP_InputField` ввода пароля                                             |
| `_statusText`    | `TMP_Text` статуса (ошибка)                                               |
| `_errorMessage`  | Текст ошибки (default: `"Неверный пароль"`)                               |
| `_errorColor`    | Цвет ошибки (default: красный 0.9/0.2/0.2)                                |
| `_shakeTarget`   | `RectTransform`, который трясётся при ошибке (опционально)                |
| `_shakeStrength` | Амплитуда тряски (default: 10)                                            |
| `_shakeDuration` | Длительность тряски в сек (default: 0.4)                                  |

**Inspector — References / Events**

| Поле              | Описание                                                                          |
| ----------------- | --------------------------------------------------------------------------------- |
| `_windowManager`  | `LaptopWindowManager` рабочего стола                                              |
| `_onFirstUnlocked`| `UnityEvent`, срабатывает один раз при первом успешном вводе пароля               |

**Логика:**

```
PuzzleModeController.OnEntered
  └── если есть питание: применить сохранённый экран (Login или Desktop)
      └── если не разблокирован — подписка на Keyboard.onTextInput
PuzzleModeController.OnExited
  └── отписка ввода, SaveManager.Save(), PauseMediaAll()

Ввод пароля:
  Enter / NumpadEnter → SubmitPassword()
  Backspace (если поле не в фокусе) → удалить символ (иначе TMP удалит сам → двойное удаление)
  Пароль верный  → _isUnlocked = true, _onFirstUnlocked, Save, ShowDesktop()
  Пароль неверный → статус ошибки + тряска → очистка поля → повторный фокус

IPowerConsumer.OnPowerStateChanged(false):
  оба экрана off, отписка ввода, PauseMediaAll()
IPowerConsumer.OnPowerStateChanged(true):
  восстановить экран по сохранённому состоянию
```

### `LaptopController`

Файл: `Assets/Scripts/Puzzle/LaptopScripts/LaptopController.cs`. Упрощённый вариант парольной загадки **без** виртуальной ОС и окон: ввод пароля → `_onSolved` → `PuzzleModeController.SetSolved()`. Тоже размещается рядом с `PuzzleModeController` и подписывается на его `OnEntered`/`OnExited`/`OnSolved`. Используйте `LaptopOS` для полного ноутбука; `LaptopController` остаётся как легаси/простой кейс.

**Inspector**

| Поле               | Описание                                                          |
| ------------------ | ----------------------------------------------------------------- |
| `_correctPassword` | Правильный пароль (default: `"1234"`, сравнение регистрозависимое) |
| `_inputField`      | `TMP_InputField` на Canvas ноутбука                               |
| `_statusText`      | `TextMeshProUGUI` статуса                                         |
| `_successMessage`  | Текст успеха (default: `"Доступ разрешён"`)                       |
| `_errorMessage`    | Текст ошибки (default: `"Неверный пароль"`)                       |
| `_successColor` / `_errorColor` | Цвета статуса                                        |
| `_shakeTarget` / `_shakeStrength` / `_shakeDuration` | Тряска при ошибке (10 / 0.4)     |
| `_onSolved`        | `UnityEvent` при верном пароле                                    |

Кнопка 3D-сабмита подключается: `SimpleInteractable.OnInteract → Submit()`.

### `LaptopWindowManager`

Файл: `Assets/Scripts/Puzzle/LaptopScripts/LaptopWindowManager.cs`. Размещается внутри панели `DesktopScreen`. Управляет списком открытых окон, фокусом и восстановлением последнего открытого файла при загрузке.

**Inspector**

| Поле                  | Описание                                                                                     |
| --------------------- | -------------------------------------------------------------------------------------------- |
| `_windowContainer`    | Transform, в который инстанцируются окна                                                     |
| `_textWindowPrefab`   | Префаб окна текста (`LaptopTextWindow`)                                                      |
| `_imageWindowPrefab`  | Префаб окна изображения (`LaptopImageWindow`)                                                |
| `_audioWindowPrefab`  | Префаб аудиоплеера (`LaptopAudioWindow`)                                                     |
| `_videoWindowPrefab`  | Префаб видеоплеера (`LaptopVideoWindow`)                                                     |
| `_documentWindowPrefab` | Префаб многостраничного документа (`LaptopDocumentWindow`)                                  |
| `_allFiles`           | Все `LaptopFileData`-ассеты этого рабочего стола — нужен для `TryOpenFileById` при загрузке сейва |

**Публичные методы**

| Метод                               | Описание                                                                                       |
| ----------------------------------- | ---------------------------------------------------------------------------------------------- |
| `OpenFile(LaptopFileData file)`     | Открывает файл; если окно уже открыто — выводит его на передний план                            |
| `NotifyWindowClosed(LaptopWindow)`  | Вызывается окном при закрытии; фокус переходит к последнему окну в списке                       |
| `CloseAll()`                        | Закрывает все окна (используется при выходе из режима ноутбука)                                 |
| `PauseMediaAll()`                   | Ставит медиа всех окон на паузу без закрытия (выход из puzzle mode / потеря питания)            |
| `TryOpenFileById(string fileId)`    | Ищет файл в `_allFiles` по id и открывает; возвращает `true` при успехе                          |
| `ActiveFileId`                      | `fileId` активного окна (пустая строка, если окон нет) — сохраняется в сейв                      |

Выбор префаба — по типу файла: `LaptopTextFile` → текстовое окно, `LaptopImageFile` → изображение, `LaptopAudioFile` → аудио, `LaptopVideoFile` → видео, `LaptopDocumentFile` → документ.

### `LaptopWindow` (абстрактный базовый класс окон)

Файл: `Assets/Scripts/Puzzle/LaptopScripts/LaptopWindow.cs`. База для всех окон: заголовок с именем файла (`fileName`), кнопка закрытия, жизненный цикл `Open → OnOpen → (OnPuzzleExited) → Close → OnClose`.

**Inspector**

| Поле           | Описание                              |
| -------------- | ------------------------------------- |
| `_windowTitle` | `TMP_Text` заголовка окна             |
| `_closeButton` | `Button` закрытия (подключается сам)  |

**Свойства и методы**

| Член                      | Описание                                                                        |
| ------------------------- | ------------------------------------------------------------------------------- |
| `FileData`                | Файл, открытый в окне                                                           |
| `Open(file)`              | Показывает окно, ставит заголовок, вызывает `OnOpen(file)`                       |
| `SetVisible(bool)`        | Прячет окно без уничтожения (фокус-менеджмент менеджера)                        |
| `Close()`                 | Уведомляет менеджер (`NotifyWindowClosed`) и уничтожает GameObject               |
| `OnPuzzleExited()`        | Виртуальный хук: пауза медиа при выходе из puzzle mode                           |
| `OnOpen(file)` / `OnClose()` | Абстрактный / виртуальный — переопределяются наследниками                     |

### Окна-наследники

| Класс                 | Файл                       | Поля инспектора                                                                                                    | Поведение |
| --------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------ | --------- |
| `LaptopTextWindow`    | `LaptopTextWindow.cs`      | `_contentText` (TMP_Text), `_scrollRect` (ScrollRect)                                                              | Показывает `content`, скролл сбрасывается наверх |
| `LaptopImageWindow`   | `LaptopImageWindow.cs`     | `_imageDisplay` (Image), `_aspectFitter` (AspectRatioFitter), `_zoomContainer` (RectTransform), `_minScale` 0.5, `_maxScale` 3, `_zoomStep` 0.2 | Картинка с сохранением пропорций; `ZoomIn()` / `ZoomOut()` / `ResetZoom()` |
| `LaptopAudioWindow`   | `LaptopAudioWindow.cs`     | `_timeDisplay`, `_playButton` / `_pauseButton` / `_stopButton`, `_progressSlider`, `_visualizerMaterial`, `_smoothness` 15, `_spectrumMultiplier` 50, `_audioSource` | Play/Pause/Stop, слайдер перемотки, спектр-визуализатор: 8 полос → `Material.SetFloatArray("_Bands")` для UI/шейдерного визуализатора |
| `LaptopVideoWindow`   | `LaptopVideoWindow.cs`     | `_videoDisplay` (RawImage), `_timeDisplay`, кнопки Play/Pause/Stop, `_progressSlider`, `_videoPlayer` (VideoPlayer) | VideoPlayer рендерит в runtime `RenderTexture` размером с клип; текстура освобождается в `OnClose`/`OnDestroy` |
| `LaptopDocumentWindow`| `LaptopDocumentWindow.cs`  | `_pageTemplate` (Image-шаблон), `_pageCounter` (TMP_Text), `_scrollRect`                                            | Инстанцирует страницу на каждый спрайт `pages[]` с `AspectRatioFitter`, между страницами — чёрный разделитель (8 px), счётчик страниц, скролл наверх |

`LaptopAudioWindow` и `LaptopVideoWindow` переопределяют `OnPuzzleExited()` — пауза воспроизведения при выходе из puzzle mode. Аудиоисточник без mixer-группы маршрутизируется в SFX-шину `AudioManager`.

### `LaptopDesktopIcon`

Файл: `Assets/Scripts/Puzzle/LaptopScripts/LaptopDesktopIcon.cs`. Иконка на рабочем столе. `[RequireComponent(typeof(Button))]`. В `Awake()` подтягивает спрайт и подпись из файла.

**Inspector**

| Поле                 | Описание                                                |
| -------------------- | ------------------------------------------------------- |
| `_icon`              | `Image` иконки                                          |
| `_label`             | `TMP_Text` подписи                                      |
| `_file`              | `LaptopFileData`, который открывает иконка              |
| `_windowManager`     | `LaptopWindowManager` рабочего стола                    |
| `_doubleClickThreshold` | Максимальный интервал между кликами для двойного (default: 0.35 сек) |

Логика: одиночный клик — выделение (просто запоминает время), двойной клик в пределах порога → `_windowManager.OpenFile(_file)`.

---

## Файлы (ScriptableObject)

Файл: `Assets/Scripts/Puzzle/LaptopScripts/Data/`. Базовый класс `LaptopFileData` хранит отображаемое имя, иконку и стабильный `fileId`; наследники добавляют контент. Создание: ПКМ в Project → `Create → Laptop → ...`.

```
LaptopFileData (abstract ScriptableObject)
├── LaptopTextFile       — content: string (TextArea 5–30)
├── LaptopImageFile      — image: Sprite
├── LaptopAudioFile      — clip: AudioClip
├── LaptopVideoFile      — clip: VideoClip
└── LaptopDocumentFile   — pages: Sprite[]   (многостраничный документ)
```

**Поля базового класса `LaptopFileData`**

| Поле        | Описание                                                                                              |
| ----------- | ----------------------------------------------------------------------------------------------------- |
| `fileName`  | Отображаемое имя на вкладке окна и иконке (default: `"Файл"`)                                          |
| `fileIcon`  | Спрайт иконки рабочего стола                                                                          |
| `[FileId] fileId` | Стабильный уникальный ID для сейва. Назначается один раз, после создания не менять              |

### `FileIdAttribute` и `FileIdDrawer`

Файлы: `Data/FileIdAttribute.cs`, `Data/Editor/FileIdDrawer.cs`. Атрибут-маркер `[FileId]` над строковым полем: PropertyDrawer рисует рядом кнопку **Set Random File ID**, которая генерирует GUID. Также доступно контекстное меню `Generate File ID` на самом ассете (и `Generate Save ID` на `LaptopOS`).

---

## Как добавить контент (новый файл)

1. ПКМ в Project → `Create → Laptop → Text File / Image File / Audio File / Video File / Document File`.
2. Заполнить `fileName`, `fileIcon` и контент.
3. Нажать **Set Random File ID** в инспекторе (один раз).
4. Добавить ассет в массив `_allFiles` окна `LaptopWindowManager`.
5. На рабочем столе создать иконку: `LaptopDesktopIcon` + `Button`, привязать `_file` и `_windowManager`.

---

## Взаимодействие с другими системами

| Система                                        | Связь                                                                                                    |
| ---------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `PuzzleModeController`                         | `LaptopOS`/`LaptopController` живут на одном объекте: `OnEntered` включает экран и клавиатуру, `OnExited` — сейв и пауза медиа |
| Lighting System                                | `IPowerConsumer`: нет питания → экраны off, ввод заблокирован, медиа на паузе                             |
| Save System                                    | `ISaveable`: сохраняет `isUnlocked` и `lastOpenedFileId`; при загрузке `TryOpenFileById` восстанавливает окно |
| `AudioManager`                                 | Аудио в окнах маршрутизируется в SFX-шину; лупы регистрируются для `MuteBackground`                       |
| Input System (`Keyboard.current`)              | Прямой ввод символов, Backspace, Enter — в обход стандартного ввода TMP при необходимости                 |
