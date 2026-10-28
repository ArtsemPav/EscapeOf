## Audio System

Система аудио: SFX, 3D-звуки, фоновые слои и музыка с кроссфейдами. Музыка полностью управляется `MusicDirector`, SFX — `AudioManager`.

## Core Components

### AudioManager.cs (`Assets/Scripts/Core/AudioManager.cs`)

Синглтон-сервис звуковых эффектов (`AudioManager.Instance`). Музыкой не владеет — всё музыкальное (меню, геймплей, зоны, события) ведёт `MusicDirector` через Music-шину SFX AudioMixer.

Поля инспектора:


| Поле                    | Тип             | Описание                                                                 |
| ----------------------- | --------------- | ------------------------------------------------------------------------ |
| `_sfxSource`            | AudioSource     | 2D-источник, через который играются все one-shot SFX                     |
| `_sfxMixer`             | AudioMixer      | SFX AudioMixer с открытыми параметрами SFXVolume / MusicVolume           |
| `_sfxVolumeParameter`   | string          | Имя параметра SFX-громкости (по умолчанию `"SFXVolume"`)                 |
| `_musicVolumeParameter` | string          | Имя параметра громкости музыки (по умолчанию `"MusicVolume"`)            |
| `_sfxGroup`             | AudioMixerGroup | Группа, в которую маршрутизируются все динамически создаваемые источники |
| `_fadeDuration`         | float           | Дефолтная длительность фейда (1.5 с)                                     |


Публичный API:

- `PlaySFX(AudioClip clip, float volume = 1f)` — one-shot на общем 2D-источнике.
- `PlaySFXExclusive(AudioClip clip, float volume = 1f)` — сначала останавливает все звуки на SFX-источнике (для повторяющихся звуков, которые не должны накладываться сами на себя).
- `Play3DOneShot(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 10f)` — одноразовый 3D-звук из временного источника (самоуничтожается).
- `Play3DLoop(AudioClip clip, Transform target, float volume, float minDistance, float maxDistance)` — зацикленный пространственный источник, парентится к цели и регистрируется в системе фонового mute. Возвращает `AudioSource`.
- `RegisterLoopSource(AudioSource source, float volume)` / `UnregisterLoopSource(AudioSource source)` — управление уже существующими источниками в tracking-системе.
- `PlayBackgroundLayer(AudioClip clip, float volume = 1f, float fadeDuration = -1f)` — зацикленный 2D-слой (сердцебиение, гул) с независимым фейдом.
- `StopBackgroundLayer(float fadeDuration = -1f)` — фейд-аут и уничтожение слоя.
- `MuteBackground(float fadeDuration = -1f)` / `UnmuteBackground(float fadeDuration = -1f)` — глушит/возвращает музыку (`MusicDirector.MuteForEvent`/`RestoreAfterEvent`) и все tracked 3D-лупы; one-shot SFX не затрагиваются. Поддерживает вложенность (счётчик владельцев у директора).
- `PlayNewMusicSource(AudioClip clip, bool loop, string trackName = null)` — фабрика: создаёт зацикленный 2D-источник под менеджером для `MusicDirector` (громкость 0, воспроизведение сразу).
- `ApplySavedVolumes()` — применяет сохранённые громкости (PlayerPrefs через `SystemSettings`) к шинам микшера.
- `ResolveMixerExternal()` — доступ к микшеру для `SystemSettings`.
- `RouteSourceToSfxGroup(AudioSource source)` — публичная обёртка маршрутизации для источников компонентов (например drag/scrape-лупы).

Дополнительно: в `Awake` сбрасывает статическое состояние tracking'а (`BackgroundTracking.ResetForNewSession`), подписывается на `SceneManager.sceneLoaded` и после каждой загрузки сцены направляет все «осиротевшие» AudioSource (без mixer group) на SFX-шину.

### MusicDirector.cs (`Assets/Scripts/Core/MusicDirector.cs`)

Синглтон (`MusicDirector.Instance`), решает какой музыкальный трек сейчас играет и кроссфейдит треки через `AudioManager`. Реализует `ISaveable` (SaveId `"music_director"`).

Поля инспектора:


| Поле                 | Тип             | Описание                                                                                |
| -------------------- | --------------- | --------------------------------------------------------------------------------------- |
| `_defaultGameTrack`  | MusicTrack      | Трек, играющий при старте игры                                                          |
| `_pauseMenuTrack`    | MusicTrack      | Трек паузы (меню); после закрытия меню возобновляется игровой                           |
| `_musicGroup`        | AudioMixerGroup | Music-шина, куда направляются все музыкальные источники                                 |
| `_minSwitchInterval` | float           | Минимальный интервал между переключениями (2 с), защита от мерцания на триггерах дверей |


Ключевой API (статический):

- `RequestTrack(MusicTrack track, bool playOnce = false)` — запрос трека; тот же активный игнорируется, playOnce-проигранные пропускаются; во время horror-мьюта запрос ставится в очередь. Возвращает `true`, если запрос принят.
- `ClearTrack(MusicTrack track)` — возвращает предыдущий трек после временного запроса.
- `PlayMenuMusic()` / `PlayGameMusic()` — переключение на музыку паузы и обратно; позиция игрового трека запоминается и восстанавливается.
- `MuteForEvent(float fadeDuration = 0)` / `RestoreAfterEvent(float fadeDurationOverride = -1)` — управление звуками для horror-событий; вложенные события считаются по числу владельцев (`_muteOwnerCount`). Очередь отложенных zone-запросов: при restore срабатывает только последний.
- `DuckActiveTrack(float targetMultiplier, float fadeDuration)` — приглушить активный трек и вернуть громкость (используется лифтом). Отменяется сменой трека.
- `FadeOutForLoadingScreen(float fadeDuration = 1.5f)` — глушит все source пула и очищает активный трек при переходе сцены (фейд идёт на отдельном `DontDestroyOnLoad`-хосте `LoadingFadeHost`, переживающем уничтожение менеджера).
- `RegisterTrack(MusicTrack track)` — регистрация трека для резолва по имени из сейва (вызывается зонами в `Awake`).
- Свойства: `Instance`, `ActiveTrack`, `MutedForEvent`.

Особенности:

- Источники пулятся по трекам (`Dictionary<MusicTrack, AudioSource>`) и **паузятся, а не уничтожаются** — трек возобновляется с позиции фейд-аута.
- Треки, запрошенные с `playOnce`, запоминаются и никогда не перезапускаются.
- `ISaveable`: сериализует доминирующий трек (имя + позиция) и список проигранных playOnce-треков. Пока меню владеет музыкой, сохраняется прерванный игровой трек (автосейв-таймер на unscaled времени).
- В `Start` доминантный трек из сейва имеет приоритет над дефолтом сцены; в сцене без игрового трека играет `_pauseMenuTrack` как музыка меню.
- Нелоoping-трек, доигравший до конца, возвращает дефолтный трек сцены (`RestoreFallbackTrack`).
- Пул источников прогревается (`LoadAudioData`) в `Start` — стриминг без хичей на первом воспроизведении.

### MusicTrack.cs (`Assets/Scripts/Core/MusicTrack.cs`)

ScriptableObject-дескриптор трека (меню `Audio/Music Track`, рекомендуется в `/Assets/Data/Sound`). Поля: `_clip` (AudioClip), `_volume` (float, 0.4), `_loop` (bool, true), `_fadeDuration` (float, −1 = использовать дефолт `MusicDirector`). Свойства: `Clip`, `Volume`, `Loop`, `FadeDuration`.

### MusicZone.cs (`Assets/Scripts/Core/MusicZone.cs`)

Триггерная зона: запускает трек при входе игрока (тег `Player`). Каждая зона срабатывает ровно один раз — повторный вход не меняет музыку; выход из зоны не трогает музыку (либо трек доигрывает до конца, либо его заменит другая непосещённая зона).

Поля: `_zoneTrack` (MusicTrack), `_playOnce` (bool, false — трек помечается проигранным и не восстанавливается сейвами). В `Awake` регистрирует трек в `MusicDirector`; при принятом запросе сохраняет игру (`SaveManager.Instance?.Save()`).

### AudioFades.cs (`Assets/Scripts/Core/AudioFades.cs`)

Статический хелпер фейдов: `AudioFades.Volume(AudioSource source, float to, float duration, Action onComplete = null)` — корутина лерпает громкость по `Time.unscaledDeltaTime` (работает на паузе), молча завершается при уничтожении источника. Используется и `AudioManager`, и `MusicDirector`.

### InventoryAudio.cs (`Assets/Scripts/Inventory/InventoryAudio.cs`)

Слушается `InventorySystem.OnCrafted` и через `AudioManager.PlaySFX` играет звук крафта (`_craftClip`, `_craftVolume = 1`). Ставится на тот же GameObject, что и `InventorySystem`.

## Integration Guide

### Добавление нового окружающего звука

Зацикленный 3D-звук (гудящая лампа, вентиляция):

1. `AudioManager.Instance.Play3DLoop(clip, transform, volume, minDistance, maxDistance)` — создаст управляемый дочерний GameObject с `AudioSource` (3D-спатиализация, SFX-шина) и вернёт источник.
2. Остановка — `AudioManager.Instance.UnregisterLoopSource(source)`.

Разовый 3D-звук в точке: `AudioManager.Instance.Play3DOneShot(clip, position, volume, minDistance, maxDistance)`.

### Музыкальные зоны

1. Создайте ассет `MusicTrack` (меню `Audio/Music Track`), задайте клип, громкость, loop и фейд.
2. Повесьте `MusicZone` на объект с trigger-коллайдером и назначьте `_zoneTrack`.
3. При необходимости включите `_playOnce` — трек сыграет один раз за прогресс (состояние переживает сейвы).

### Обработка паузы и mute

- **Музыка:** владеет `MusicDirector` — `PlayMenuMusic`/`PlayGameMusic` вызываются из `GameManager`, при `Time.timeScale == 0` фейды идут по `Time.unscaledDeltaTime`.
- **Mute для событий:** `AudioManager.MuteBackground/UnmuteBackground` или напрямую `MusicDirector.MuteForEvent/RestoreAfterEvent` — глушится музыка и tracked-лупы, one-shot SFX продолжают звучать.
- **Слои:** `PlayBackgroundLayer`/`StopBackgroundLayer` для независимых 2D-слоёв с фейдами.
- **Громкости:** `SystemSettings` пишет в exposed-параметры микшера (`SFXVolume` / `MusicVolume`), изменения применяются мгновенно к играющему звуку.
