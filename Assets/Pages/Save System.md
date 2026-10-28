Система сохранений автоматически запоминает состояние игры и восстанавливает его при следующем запуске. Любой объект в сцене может участвовать в сохранении — достаточно реализовать один интерфейс.

## Как это работает в двух словах

```
Запуск игры
  └─ SaveManager.Start() (порядок -10) читает файл с диска
       └─ для каждого объекта в файле вызывает LoadSaveData()
            └─ объект восстанавливает своё состояние

Во время игры (событие — подбор предмета, открытие двери и т.д.)
  └─ вызывается SaveManager.Instance.Save()
       └─ делается снимок всех объектов прямо сейчас
            └─ через 2 секунды снимок пишется на диск (в фоне)
                 └─ показывается индикатор "● Сохранение..."

При закрытии игры / выходе в меню
  └─ OnApplicationQuit и OnDestroy синхронно сбрасывают снимок на диск (ничего не теряется)
```

## Файл сохранения

Файлы лежат в папке `saves/` внутри `Application.persistentDataPath`:

- Windows: `%APPDATA%\..\LocalLow\<CompanyName>\Escape\saves\`
- Основной файл: `slot_<N>.json` (по умолчанию `slot_0.json`)
- Резервные копии: `slot_<N>_bk1.json`, `slot_<N>_bk2.json`

При каждом сохранении старый файл становится `bk1`, `bk1` становится `bk2`. Если основной файл повреждён или не читается — при загрузке автоматически берётся `bk1`, затем `bk2`. Формат файла — `GameSaveData` (`Assets/Scripts/Data/GameSaveData.cs`): `version` (текущая версия — 2), `timestamp` и список `entities` (по записи на каждый `ISaveable`: стабильный `id` + JSON-блоб `data`).

Слоты: сессия запоминает свой слот в PlayerPrefs (`save_manager_last_slot`) и все записи идут в него же. Отладочный слот `999` (`SaveManager.DebugSlot`) — restore point для кнопки Load в меню паузы: из него можно загрузиться, но он никогда не становится активным слотом записи.

---

## Как добавить сохранение к любому объекту

Нужно реализовать интерфейс `ISaveable` (`Assets/Scripts/Core/ISaveable.cs`) — три шага.

### Шаг 1 — Реализовать интерфейс

```csharp
using System;
using UnityEngine;

public class MyObject : MonoBehaviour, ISaveable
{
    [SerializeField] private string _saveId = "my_object_unique_id";

    // Твои данные, которые нужно сохранить
    private bool _isActivated;

    // ── ISaveable ────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isActivated = _isActivated });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isActivated = data.isActivated;
        // Применяй восстановленное состояние здесь
    }

    [Serializable]
    private struct SaveData
    {
        public bool isActivated;
    }

    // ── Регистрация ───────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }
}
```

### Шаг 2 — Задать уникальный SaveId

`_saveId` — строка-идентификатор, по которой система находит нужный объект при загрузке. Правила:

- Уникальна в пределах всей игры — два разных объекта не могут иметь одинаковый ID
- Никогда не меняется после первого назначения — иначе старые сохранения не загрузятся
- Понятное имя лучше GUID: `door_firstroom_main` читается, `a3f8...` — нет

В Inspector укажи значение прямо в поле `_saveId`. Для компонентов с автогенерацией (`PickableItem`, `LeverInteraction`, `LiquidDrainInteractable` и др.) доступно контекстное меню компонента → **Generate Save ID** (генерирует GUID).

### Шаг 3 — Вызвать Save() при изменении состояния

Когда происходит важное событие (объект активирован, дверь открыта, предмет подобран):

```csharp
_isActivated = true;
SaveManager.Instance?.Save(); // дебаунс 2 сек — несколько вызовов подряд = один файл
```

> Вызывай `Save()` ДО того как уничтожить объект — снимок делается в момент вызова.

---

## Уже реализованные ISaveable в проекте


| Компонент                     | SaveId пример                 | Что сохраняет                                            |
| ----------------------------- | ----------------------------- | -------------------------------------------------------- |
| `PickableItem`                | `pickable_flashlight`         | Подобран ли предмет (`collected`)                        |
| `InventorySystem`             | `inventory` (константа)       | Содержимое всех слотов                                   |
| `DoorInteraction`             | `door_firstroom_main`         | Открыта/заперта/угол открытия                            |
| `CodeLock`                    | `codelock_firstroom`          | Разблокирован ли замок, текущий код                      |
| `HorrorEvent`                 | `horror_<eventId>`            | Сработало ли событие (авто-ID из `_eventId`)             |
| `FlashlightController`        | `flashlight` (константа)      | Включён ли фонарик                                       |
| `FPSController` (Player)      | `player` (константа)          | Позиция и угол камеры                                    |
| `PressurePuzzle`              | `pressure_puzzle_boilerroom`  | Решена ли загадка (`isSolved`)                           |
| `MedallionBoxInteraction`     | `medallion_puzzle`            | Solved-флаг + ItemId в каждой из 5 лунок                 |
| `MedallionCollectionTracker`  | `medallion_collection`        | Порядок подбора медальонов                               |
| `MedallionRoomTransformation` | `_saveId`                     | Состояние трансформации комнаты медальонов               |
| `LightingSystem`              | `LightingSystem` (константа)  | Power state, generator readiness, switch states          |
| `ElectricPuzzleController`    | `electric_puzzle`             | isSolved, wiresCorrect, connections, fuseInserted        |
| `ChemicalSynthesisController` | `_saveId`                     | isSolved                                                 |
| `LoopPuzzleController`        | `_saveId`                     | isSolved, switchStates, conditionLenses                  |
| `LoopPuzzleHiddenDoor`        | `_saveId`                     | Состояние скрытой двери                                  |
| `PaintingColumn`              | `painting_column_q1`          | Текущая высота картины                                   |
| `SpotlightLensButton`         | `spotlight_lens_l1`           | Текущий шаг линзы (0–3)                                  |
| `PuzzleModeController`        | `puzzle_mode_...`             | Решена ли загадка (общий контроллер режима)              |
| `NurseryLockController`       | `nursery_lock`                | isSolved, isLockpickInserted                             |
| `MetamorfPuzzleController`    | `metamorf_puzzle`             | Состояние загадки метаморфозы                            |
| `BoardPuzzleManager`          | `board_puzzle`                | Состояние трубопроводной загадки                         |
| `FifteenPuzzleManager`        | `fifteen_puzzle`              | Состояние загадки «пятнашки»                             |
| `ClockHand`                   | `clock_hand_...`              | Положение стрелки часов                                  |
| `LaptopOS`                    | `laptop_os`                   | Состояние ОС ноутбука                                    |
| `ElevatorController`          | `elevator`                    | Текущий этаж, состояние лифта                            |
| `MechanicalLock`              | `mechanical_lock_...`         | Состояние механического замка                            |
| `LockDial`                    | `lock_dial_...`               | Положение диска замка                                    |
| `RotateOnTrigger`             | `rotate_on_trigger_...`       | Угол вращения                                            |
| `LeverInteraction`            | `_saveId` (или GUID)          | Состояние рычага (`IsOn`)                                |
| `RoomPuzzleGate`              | `room_gate_coridor_fdoor`     | Состояние шлюза комнаты                                  |
| `HorrorInteractable`          | `hprop_<suffix>`              | Состояние хоррор-интерактивного объекта                  |
| `DuckHorrorInteractable`      | `_saveId`                     | Состояние утки-скримера                                  |
| `SpiderWebSlowTrigger`        | `spider_web/...` (авто)       | Пройдена ли паутина (`isPassed`) — авто-ID из пути сцены |
| `LiquidDrainInteractable`     | (GUID, авто)                  | Слита ли жидкость (`isDrained`)                          |
| `RigidbodyStateSaver`         | `_saveId`                     | Поза Rigidbody (позиция + поворот, опц. скорости)        |
| `MriUvCutscene`               | `_saveId`                     | Проиграна ли UV-катсцена МРИ                             |
| `UVPentagramSequence`         | `_saveId`                     | Состояние UV-пентаграммы                                 |
| `EarthquakeFOVPulse`          | авто-константа                | Отыгран ли эффект землетрясения (один раз)               |
| `ScullPuzzlePhase`            | `final_door_scull_phase`      | Фаза финальной двери (череп)                             |
| `FinalDoorPuzzleController`   | `_saveId`                     | Состояние контроллера финальной двери                    |
| `FinalDoorPuzzleInteraction`  | `final_door_puzzle`           | Состояние взаимодействия финальной двери                 |
| `GeneratorPuzzleController`   | `_saveId`                     | Состояние загадки генератора                             |
| `PuzzleSolvedCinematic`       | `puzzle_solved_cinematic_...` | Воспроизведён ли кинематик                               |
| `MusicDirector`               | `music_director` (константа)  | Доминирующий трек + позиция, playOnce-треки              |


---

## Настройки SaveManager в Inspector

`SaveManager` (`Assets/Scripts/Core/SaveManager.cs`) — объект в сцене с одноимённым компонентом (DontDestroyOnLoad, `[DefaultExecutionOrder(-10)]`).


| Поле                    | По умолчанию | Описание                                                                 |
| ----------------------- | ------------ | ------------------------------------------------------------------------ |
| **Auto Save Interval**  | 120 сек      | Как часто автоматически писать файл. `0` — отключить                     |
| **Save Debounce Delay** | 2 сек        | Пауза после `Save()` перед записью. Несколько событий подряд = один файл |
| **Default Slot**        | 0            | Слот сохранения по умолчанию                                             |
| **Backup Count**        | 2            | Сколько резервных копий хранить                                          |


Ключевой API:

- `Save()` — снапшот всех `ISaveable` + отложенная запись через дебаунс. Вызовы в дебаунс-окне мерджатся в один снапшот (`MergeIntoSnapshot`).
- `SaveImmediate(int slot = -1)` — синхронная запись (для debug-кнопок).
- `Load(int slot)` — загрузка слота с автопадением на бэкапы (файл битый, пустой или не парсится — берётся следующий).
- `DeleteSave(int slot = 0)` / `ClearRegistry()` / `HasSave(int slot = 0)` / `SlotHasSave(int slot)`.
- `StartNewGame(int slot)` — статика: чистит слот и бэкапы, сбрасывает активный слот, запрашивает wake-up intro.
- `RequestLoadFromSlot(int slot)` / `ResetActiveSlot()` — управление слотом следующей сессии.
- `RequestWakeUpIntro()` / `ConsumeWakeUpIntroRequest()` — one-shot флаг интро пробуждения (PlayerPrefs): ставится меню на New Game, консюментится `WakeUpIntroController`.
- `HasSettled` / `InstanceHasSettled` — `true` после первого `Update` (вся сцена проинициализирована). Событийные эффекты при `false` должны восстанавливаться молча, без повторного отыгрыша.
- Событие `OnSaved` — срабатывает после каждой успешной записи (подписан `SaveIndicatorUI`).

Запись выполняется в фоне (`Task.Run`): сериализация на главном потоке, ротация бэкапов и запись файла — в пуле потоков; колбэки возвращаются в главный поток через очередь. Синхронные записи (`OnApplicationQuit`, `OnDestroy`, `SaveImmediate`) ждут завершения фоновой записи, чтобы не перезаписать свежий файл старыми данными.

Защита реестра: дубли `SaveId` не перезаписывают живую регистрацию (клон инспекции не вытеснит оригинал); запись уничтоженного объекта остаётся в реестре, чтобы сохранённое состояние подобранных предметов не потерялось, а runtime-клоны её не затирали.

Fresh start: если файла сейва нет (и бэкапов тоже), `SaveManager.Start()` сам вызывает `RequestWakeUpIntro()` — в Game-сцене отыграется интро пробуждения независимо от точки входа.

---

## Сброс прогресса

### Из меню паузы в игре

Кнопка **Сбросить прогресс** (`ResetProgressBtn`) в меню паузы:

1. Вызывает `SaveManager.Instance.DeleteSave()` — удаляет файл и все бэкапы
2. Вызывает `SaveManager.Instance.ClearRegistry()` — очищает реестр объектов
3. Вызывает `SaveManager.ResetActiveSlot()` — сбрасывает запомненный слот
4. Уничтожает все DontDestroyOnLoad-синглтоны (GameManager, SaveManager, AudioManager, InputManager, ResolutionManager, PopupMessageSystem)
5. Перезагружает сцену

### Из редактора Unity (без запуска игры)

**Tools → Escape → Reset Save Progress**

Скрипт `SaveProgressEditor` (`Assets/Scripts/Editor/SaveProgressEditor.cs`) — редакторский инструмент для быстрого сброса в процессе разработки. Недоступен во время Play Mode. Показывает диалог с путём к файлу и подтверждением, затем удаляет основной файл и оба бэкапа.

---

## Порядок инициализации и ISaveable

Система использует скриптовые приоритеты (`DefaultExecutionOrder`) чтобы гарантировать правильный порядок загрузки:


| Компонент                    | Порядок | Что происходит в `Start()`                                                 |
| ---------------------------- | ------- | -------------------------------------------------------------------------- |
| `SaveManager`                | `-10`   | Читает файл, вызывает `LoadSaveData()` на всех зарегистрированных объектах |
| `MedallionBoxInteraction`    | `-7`    | `ApplyPendingLoad()` — применяет данные о лунках в сцену                   |
| `MedallionCollectionTracker` | `-5`    | Подписывается на `OnInventoryChanged`, синхронизирует порядок сбора        |
| Остальные объекты            | `0`     | Стандартный `Start()`                                                      |


**Почему порядок критичен:** если объект, реализующий `ISaveable`, вызывает `SaveManager.Save()` в своём `Start()` — до того как другие объекты применили `LoadSaveData()` — снимок состояния будет неполным. Например, `MedallionCollectionTracker.Start()` синхронизирует порядок монет и потенциально инициирует сохранение. Если бы `MedallionBoxInteraction` не успел применить состояние лунок, снимок зафиксировал бы их как пустые, перезаписав корректный файл.

**Правило:** любой `ISaveable`, чей `Start()` может вызвать `Save()`, должен иметь порядок выполнения **строго больший** чем у всех объектов, чьи данные он может "захватить" в снимок.

**Дополнительная защита в SaveManager:** снапшоты, запрошенные до «устаканивания» сцены (`Save()` в `_initialSettled == false`), откладываются — дебаунс-flush снимает состояние уже после того, как все `Start()` отыграли (телепорт игрока, восстановление фонарика и т.д. применены).

**Защитный паттерн (`_isReady`):** чтобы синхронизация при старте не инициировала преждевременное сохранение, используй флаг:

```csharp
private bool _isReady;

private void Start()
{
    // Синхронизация без сохранения — остальные объекты ещё не готовы
    SyncState();
    _isReady = true;
}

private void OnSomethingChanged()
{
    UpdateState();
    if (_isReady)
        SaveManager.Instance?.Save(); // Только после полной инициализации
}
```

Этот паттерн реализован в `MedallionCollectionTracker`.

---

## Частые ошибки

**Объект не восстанавливается после загрузки**

- Проверь что поле `_saveId` заполнено в Inspector — пустой ID игнорируется системой (в консоль пишется предупреждение с пингом объекта)
- Убедись что `Register(this)` вызывается в `Awake()`, а не в `Start()` — `SaveManager.Start()` выполняется раньше и к тому моменту реестр должен быть заполнен

**Два объекта с одинаковым SaveId**

- Второй объект НЕ перезапишет первого — дубль игнорируется с предупреждением в консоли (клик по логу пингует объект в Hierarchy)
- Исключение: «мёртвая» запись (уничтоженный объект той же сцены) замещается при новой загрузке сцены

**Состояние не сохраняется при резком закрытии**

- Система сбрасывает снимок синхронно в `OnApplicationQuit` и в `OnDestroy` (выход в меню и debug-load уничтожают SaveManager через `Destroy()`, минуя quit) — при штатном закрытии данные не теряются
- При крэше данные за последние 2 секунды могут быть потеряны (это нормально)

**Предмет появляется в сцене после загрузки (был подобран)**

- Убедись что `_saveId` заполнен у `PickableItem` в Inspector
- Убедись что перед `Destroy(gameObject)` вызывается `NotifyPickedUp()` — именно он помечает предмет как собранный и вызывает `Save()`

**После подбора предмета инвентарь пустой при перезагрузке**

- Убедись что `ItemData` этого предмета есть в массиве `_allItems` у компонента `InventorySystem`. `FindItemById()` ищет только внутри этого массива — если `ItemData` там нет, слот при загрузке вернёт `null`.
