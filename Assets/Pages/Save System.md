# Save System

Система сохранений автоматически запоминает состояние игры и восстанавливает его при следующем запуске. Любой объект в сцене может участвовать в сохранении — достаточно реализовать один интерфейс.

---

## Как это работает

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

---

## Файл сохранения

Файл находится в папке `saves/` внутри `Application.persistentDataPath`:

- **Windows:** `%APPDATA%\..\LocalLow\<CompanyName>\Escape\saves\`
- Основной файл: `slot_0.json`
- Резервные копии: `slot_0_bk1.json`, `slot_0_bk2.json`

При каждом сохранении старый файл становится `bk1`, `bk1` становится `bk2`.
Если основной файл повреждён — при следующем запуске автоматически загрузится `bk1`, затем `bk2`.

---

## Как добавить сохранение к любому объекту

### Шаг 1 — Реализовать интерфейс ISaveable

```csharp
using System;
using UnityEngine;

public class MyObject : MonoBehaviour, ISaveable
{
    [SerializeField] private string _saveId = "my_object_unique_id";

    // Данные, которые нужно сохранить
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

Укажи значение прямо в поле `_saveId` в Inspector.

**Правила:**
- Уникальна во всей игре — два объекта не могут иметь одинаковый ID
- Никогда не меняется после назначения — иначе старые сохранения не загрузятся
- Понятное имя лучше GUID: `door_firstroom_main` читается, `a3f8b2c1...` — нет

### Шаг 3 — Вызвать Save() при изменении состояния

```csharp
_isActivated = true;
SaveManager.Instance?.Save(); // дебаунс 2 сек — несколько вызовов подряд = один файл
```

> **Важно:** вызывай `Save()` ДО того как уничтожить объект — снимок делается в момент вызова.

---

## Уже реализованные ISaveable в проекте

| Компонент | Пример SaveId | Что сохраняет |
|---|---|---|
| `PickableItem` | `pickable_flashlight` | Подобран ли предмет (`collected`) |
| `InventorySystem` | `inventory` | Содержимое всех слотов |
| `DoorInteraction` | `door_firstroom_main` | Открыта / заперта / угол открытия |
| `CodeLock` | `codelock_firstroom` | Разблокирован ли замок, текущий код |
| `HorrorEvent` | `horror_mannequin_appears` | Сработало ли событие |
| `GameManager` | `game_manager` | Текущая комната |
| `FPSController` | `player` | Позиция и угол камеры |
| `PressurePuzzle` | `pressure_puzzle_boilerroom` | Решена ли загадка (`isSolved`) |

---

## Настройки SaveManager в Inspector

`SaveManager` — GameObject в сцене с компонентом `SaveManager` (DontDestroyOnLoad).

| Поле | По умолчанию | Описание |
|---|---|---|
| **Auto Save Interval** | 120 сек | Как часто автоматически писать файл. `0` — отключить |
| **Save Debounce Delay** | 2 сек | Пауза после `Save()` перед записью. Несколько событий подряд = один файл |
| **Default Slot** | 0 | Номер слота сохранения |
| **Backup Count** | 2 | Сколько резервных копий хранить |

---

## Сброс прогресса

В меню паузы есть кнопка **Сбросить прогресс**. Она:
1. Вызывает `SaveManager.Instance.DeleteSave()` — удаляет файл и все бэкапы
2. Вызывает `SaveManager.Instance.ClearRegistry()` — очищает реестр объектов
3. Перезагружает сцену

---

## Частые ошибки

**Объект не восстанавливается после загрузки**
- Проверь что поле `_saveId` заполнено в Inspector — пустой ID игнорируется
- Убедись что `Register(this)` вызывается в `Awake()`, а не в `Start()` — `SaveManager.Start()` выполняется раньше всех и к тому моменту реестр уже должен быть заполнен

**Два объекта с одинаковым SaveId**
- Второй объект перезапишет первого в реестре — данные первого не загрузятся
- SaveManager выведет предупреждение в Console при регистрации дубля

**Предмет появляется в сцене после загрузки (был подобран)**
- Убедись что `_saveId` заполнен у `PickableItem` в Inspector
- Убедись что перед `Destroy(gameObject)` вызывается `NotifyPickedUp()` — именно он помечает предмет как собранный и вызывает `Save()`

**Состояние не сохраняется при резком закрытии**
- При штатном закрытии (`OnApplicationQuit`) снимок всегда сбрасывается на диск
- При крэше данные за последние 2 секунды могут быть потеряны — это нормально
