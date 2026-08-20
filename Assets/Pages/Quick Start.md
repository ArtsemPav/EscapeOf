Это руководство для тех, кто работает с проектом впервые. Здесь описано устройство игры и как добавить в неё новые элементы.

## Что за игра

Escape Room — игра от первого лица. Игрок перемещается по комнатам, подбирает предметы, читает записки и вводит коды в замки, чтобы открывать двери и переходить в следующие комнаты.

## Ключевые системы


| Система                   | Что делает                                                                                                                                    |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **UIManager**             | Управляет всеми UI-панелями: открытие, закрытие, курсор, ввод игрока                                                                          |
| **GameConfig**            | Единый конфиг для текстов и цветов — меняешь в одном месте, работает везде                                                                    |
| **InventorySystem**       | Хранит предметы игрока, поддерживает крафтинг                                                                                                 |
| **GameManager**           | Управляет прогрессом по комнатам, паузой (ESC), меню — вся логика паузы здесь                                                                 |
| **MainMenuController**    | Обрабатывает кнопки главного меню (New Game, Settings, Exit). См. [@ id="/Pages/Private/Main Menu System.md" label="Main Menu System"] |
| **SaveManager**           | Автосохранение и загрузка состояния игры. См. [@ id="/Pages/Private/Save System.md" label="Save System"]                          |
| **LightingSystem**        | Управление электричеством, зонами освещения, генератором. См. [@ id="/Pages/Private/Lighting System.md" label="Lighting System"]      |
| **AudioManager**          | Глобальное управление звуком (SFX, музыка, mute/volume)                                                                                       |
| **ScreenFader**           | Затемнение экрана для переходов и кинематиков. Объект `/Canvas/ScreenFader` должен быть активен                                               |
| **RoomVisibilityManager** | Отсечение геометрии комнат по позиции игрока. См. [@ id="/Pages/Private/Room Visibility System.md" label="Room Visibility System"]           |
| **HorrorSystem**          | Координатор хоррор-событий. См. [@ id="/Pages/Private/Horror System.md" label="Horror System"]                                      |
| **InventoryBackdrop**     | Закрывает инвентарь кликом вне его панели                                                                                                     |
| **InventoryItemPreview**  | Встроенный 3D-просмотр предмета в правой части инвентаря                                                                                      |


## Обязательные объекты в сцене

Чтобы игра работала, в сцене должны быть:

```
GameManager             ← компонент GameManager, список комнат
UIManager               ← компонент UIManager, ссылки на Player и GameConfig
Canvas                  ← все UI-панели внутри (включая ScreenFader)
Player                  ← компонент FPSController
SaveManager             ← компонент SaveManager (DontDestroyOnLoad)
LightingSystem          ← компонент LightingSystem
AudioManager            ← компонент AudioManager
RoomVisibilityManager   ← компонент RoomVisibilityManager
HorrorSystem            ← компонент HorrorSystem
```

## Как начать добавлять контент

- [@ id="/Pages/Private/Pickable Items.md" label="Pickable Items"] — как добавить предмет, который игрок может подобрать
- [@ id="/Pages/Private/Notes.md" label="Notes"] — как добавить читаемую записку
- [@ id="/Pages/Private/Code Locks.md" label="Code Locks"] — как добавить кодовый замок на дверь
- [@ id="/Pages/Private/Save System.md" label="Save System"] — как работает система сохранений и как добавить сохранение к новому объекту
- [@ id="/Pages/Private/Inventory System.md" label="Inventory System"] — как работает инвентарь и крафтинг
- [@ id="/Pages/Private/Drag Interaction System.md" label="Drag Interaction System"] — перетаскивание ящиков и дверей
- [@ id="/Pages/Private/Flashlight System.md" label="Flashlight System"] — фонарик с режимами линз
- [@ id="/Pages/Private/Horror System.md" label="Horror System"] — хоррор-события
- [@ id="/Pages/Private/Lighting System.md" label="Lighting System"] — электричество и освещение
- [@ id="/Pages/Private/Room Visibility System.md" label="Room Visibility System"] — отсечение геометрии комнат
- [@ id="/Pages/Private/Main Menu System.md" label="Main Menu System"] — главное меню

### Загадки

- [@ id="/Pages/Private/Pressure Puzzle System.md" label="Pressure Puzzle System"]
- [@ id="/Pages/Private/Medallion Puzzle System.md" label="Medallion Puzzle System"]
- [@ id="/Pages/Private/Electric Puzzle System.md" label="Electric Puzzle System"]
- [@ id="/Pages/Private/Loop Puzzle System.md" label="Loop Puzzle System"]
- [@ id="/Pages/Private/Chemical Synthesis Puzzle.md" label="Chemical Synthesis Puzzle"]
- [@ id="/Pages/Private/Card Lock Puzzle System.md" label="Card Lock Puzzle System"]
- [@ id="/Pages/Private/Nursery Lock Puzzle System.md" label="Nursery Lock Puzzle System"]

## Быстрый чеклист для новой сцены

- [ ] В сцене есть `GameManager` с заполненным списком комнат
- [ ] В сцене есть `UIManager` — назначены `FPSController` и `GameConfig`
- [ ] Создан и назначен ассет `GameConfig` (правой кнопкой → Create → Game → Game Config)
- [ ] `Canvas` содержит все панели: инвентарь, записки, кодовый замок, инспекцию предметов
- [ ] Слой `Interactable Layer` назначен на все интерактивные объекты
- [ ] Слой `Inspection` существует — используется для 3D-превью предметов в инвентаре и ItemInspector
- [ ] `InventoryPanel` содержит `LeftPanel`, `RightPanel`, `HintsBar` (смотри [@ id="/Pages/Private/Inventory System.md" label="Inventory System"])
- [ ] `InventoryBackdrop` — первый дочерний объект Canvas перед `InventoryPanel`; назначен в поле `Inventory Backdrop` компонента `InventoryUI` на Player
- [ ] `RightPanel/PreviewImage` имеет компонент `AspectRatioFitter` (FitInParent, ratio 1:1) и `InventoryItemPreview`
- [ ] `InspectionSetup/InventoryPreviewCamera` назначен в поле `Preview Camera` компонента `InventoryItemPreview`
- [ ] `/Canvas/ScreenFader` активен (`activeSelf = true`) — нужен для переходов и кинематиков
- [ ] `SaveManager` в сцене (DontDestroyOnLoad) — без него сохранения не работают
- [ ] `LightingSystem` в сцене — без неё электричество и освещение не функционируют