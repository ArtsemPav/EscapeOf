Система сохранений автоматически запоминает состояние игры и восстанавливает его при следующем запуске. Любой объект в сцене может участвовать в сохранении — достаточно реализовать один интерфейс.

## Как это работает в двух словах

```
Запуск игры
  └─ SaveManager.Start() читает файл с диска
       └─ для каждого объекта в файле вызывает LoadSaveData()
            └─ объект восстанавливает своё состояние

Во время игры (событие — подбор предмета, открытие двери и т.д.)
  └─ вызывается SaveManager.Instance.Save()
       └─ делается снимок всех объектов прямо сейчас
            └─ через 2 секунды снимок пишется на диск
                 └─ показывается индикатор "● Сохранение..."

При закрытии игры
  └─ OnApplicationQuit немедленно сбрасывает снимок на диск (ничего не теряется)
```

## Файл сохранения

Файл находится в папке `saves/` внутри `Application.persistentDataPath`:

- Windows: `%APPDATA%\..\LocalLow\<CompanyName>\Escape\saves\`
- Основной файл: `slot_0.json`
- Резервные копии: `slot_0_bk1.json`, `slot_0_bk2.json`

При каждом сохранении старый файл становится `bk1`, `bk1` становится `bk2`. Если основной файл повреждён — при следующем запуске автоматически загрузится `bk1`, затем `bk2`.

---

## Как добавить сохранение к любому объекту

Нужно реализовать интерфейс `ISaveable` — три шага.

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

`_saveId` — это строка-идентификатор, по которой система сохранений находит нужный объект при загрузке. Правила:

- Уникальна в пределах всей игры — два разных объекта не могут иметь одинаковый ID
- Никогда не меняется после первого назначения — иначе старые сохранения не загрузятся
- Понятное имя лучше GUID: `door_firstroom_main` читается, `a3f8...` — нет

В Inspector укажи значение прямо в поле `_saveId`. Для `PickableItem` также доступен контекстное меню компонента → **Generate Save ID** для автогенерации GUID.

### Шаг 3 — Вызвать Save() при изменении состояния

Когда происходит важное событие (объект активирован, дверь открыта, предмет подобран):

```csharp
_isActivated = true;
SaveManager.Instance?.Save(); // дебаунс 2 сек — несколько вызовов подряд = один файл
```

> Вызывай `Save()` ДО того как уничтожить объект — снимок делается в момент вызова.

---

## Уже реализованные ISaveable в проекте


| Компонент                     | SaveId пример                 | Что сохраняет                                     |
| ----------------------------- | ----------------------------- | ------------------------------------------------- |
| `PickableItem`                | `pickable_flashlight`         | Подобран ли предмет (`collected`)                 |
| `InventorySystem`             | `inventory`                   | Содержимое всех слотов                            |
| `DoorInteraction`             | `door_firstroom_main`         | Открыта/заперта/угол открытия                     |
| `CodeLock`                    | `codelock_firstroom`          | Разблокирован ли замок, текущий код               |
| `HorrorEvent`                 | `horror_mannequin_appears`    | Сработало ли событие                              |
| `GameManager`                 | `game_manager`                | Текущая комната                                   |
| `FPSController` (Player)      | `player`                      | Позиция и угол камеры                             |
| `PressurePuzzle`              | `pressure_puzzle_boilerroom`  | Решена ли загадка (`isSolved`)                    |
| `MedallionBoxInteraction`     | `medallion_puzzle`            | Solved-флаг + ItemId в каждой из 5 лунок          |
| `MedallionCollectionTracker`  | `medallion_collection`        | Порядок подбора медальонов                        |
| `LightingSystem`              | `lighting_system`             | Power state, generator readiness, switch states   |
| `ElectricPuzzleController`    | `electric_puzzle`             | isSolved, wiresCorrect, connections, fuseInserted |
| `ChemicalSynthesisController` | `chemical_synthesis`          | isSolved                                          |
| `LoopPuzzleController`        | `loop_puzzle`                 | isSolved, switchStates, conditionLenses           |
| `LoopPuzzleHiddenDoor`        | `loop_puzzle_hidden_door`     | Состояние скрытой двери                           |
| `PaintingColumn`              | `painting_column_q1`          | Текущая высота картины                            |
| `SpotlightLensButton`         | `spotlight_lens_l1`           | Текущий шаг линзы (0–3)                           |
| `PuzzleModeController`        | `puzzle_mode_...`             | Решена ли загадка (общий контроллер режима)       |
| `NurseryLockController`       | `nursery_lock`                | isSolved, isLockpickInserted                      |
| `MetamorfPuzzleController`    | `metamorf_puzzle`             | Состояние загадки метаморфозы                     |
| `BoardPuzzleManager`          | `board_puzzle`                | Состояние трубопроводной загадки                  |
| `FifteenPuzzleManager`        | `fifteen_puzzle`              | Состояние загадки «пятнашки»                      |
| `ClockHand`                   | `clock_hand_...`              | Положение стрелки часов                           |
| `LaptopOS`                    | `laptop_os`                   | Состояние ОС ноутбука                             |
| `ElevatorController`          | `elevator`                    | Текущий этаж, состояние лифта                     |
| `MechanicalLock`              | `mechanical_lock_...`         | Состояние механического замка                     |
| `LockDial`                    | `lock_dial_...`               | Положение диска замка                             |
| `RotateOnTrigger`             | `rotate_on_trigger_...`       | Угол вращения                                     |
| `HorrorInteractable`          | `horror_interactable_...`     | Состояние хоррор-интерактивного объекта           |
| `PuzzleSolvedCinematic`       | `puzzle_solved_cinematic_...` | Воспроизведён ли кинематик                        |


---

## Настройки SaveManager в Inspector

`SaveManager` — объект в сцене с одноимённым компонентом (DontDestroyOnLoad).


| Поле                    | По умолчанию | Описание                                                                 |
| ----------------------- | ------------ | ------------------------------------------------------------------------ |
| **Auto Save Interval**  | 120 сек      | Как часто автоматически писать файл. `0` — отключить                     |
| **Save Debounce Delay** | 2 сек        | Пауза после `Save()` перед записью. Несколько событий подряд = один файл |
| **Default Slot**        | 0            | Номер слота сохранения (для будущих нескольких слотов)                   |
| **Backup Count**        | 2            | Сколько резервных копий хранить                                          |


---

## Сброс прогресса

### Из меню паузы в игре

В меню паузы есть кнопка **Сбросить прогресс**. Она:

1. Вызывает `SaveManager.Instance.DeleteSave()` — удаляет файл и все бэкапы
2. Вызывает `SaveManager.Instance.ClearRegistry()` — очищает реестр объектов
3. Перезагружает сцену

### Из редактора Unity (без запуска игры)

**Tools → Escape → Reset Save Progress**

Скрипт `SaveProgressEditor` — редакторский инструмент для быстрого сброса в процессе разработки. Недоступен во время Play Mode. Показывает диалог с путём к файлу и подтверждением, затем удаляет основной файл и оба бэкапа.

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

- Проверь что поле `_saveId` заполнено в Inspector — пустой ID игнорируется системой
- Убедись что `Register(this)` вызывается в `Awake()`, а не в `Start()` — `SaveManager.Start()` выполняется раньше и к тому моменту реестр должен быть заполнен

**Два объекта с одинаковым SaveId**

- Второй объект перезапишет первого в реестре — данные первого не загрузятся
- SaveManager выведет предупреждение в консоль при регистрации дубля

**Состояние не сохраняется при резком закрытии**

- Система сбрасывает снимок в `OnApplicationQuit` — при штатном закрытии это работает
- При крэше данные за последние 2 секунды могут быть потеряны (это нормально)

**Предмет появляется в сцене после загрузки (был подобран)**

- Убедись что `_saveId` заполнен у `PickableItem` в Inspector
- Убедись что перед `Destroy(gameObject)` вызывается `NotifyPickedUp()` — именно он помечает предмет как собранный и вызывает `Save()`

**После подбора предмета инвентарь пустой при перезагрузке**

- Убедись что `ItemData` этого предмета есть в массиве `_allItems` у компонента `InventorySystem`. `FindItemById()` ищет только внутри этого массива — если `ItemData` там нет, слот при загрузке вернёт `null`.