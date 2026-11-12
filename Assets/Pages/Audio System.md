<!-- version: 8 -->

## Audio System

Система аудио: SFX, 3D-звуки, фоновые слои и музыка с кроссфейдами. Музыка полностью управляется `MusicDirector`, SFX — `AudioManager`. Все фейды идут по `Time.unscaledDeltaTime` (работают на паузе).

## Audio Categories (шины микшера)

Все звуки игры делятся на три категории — по шинам микшера `Assets/sound/SFX.mixer`:


| Категория | Шина mixer'а       | Что играет                                                                                                                                                                   |
| --------- | ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Music     | `Master/Music`     | Вся музыка (меню, геймплей, зоны, события) — через `MusicDirector`                                                                                                           |
| SFX2D     | `Master/SFX/SFX2D` | Не позиционные эффекты: one-shot SFX (`PlaySFX`/`PlaySFXExclusive`) и фоновые слои (`PlayBackgroundLayer`), общий 2D-источник `_sfxSource`                                   |
| SFX3D     | `Master/SFX/SFX3D` | Пространственные звуки: `Play3DOneShot`, `Play3DLoop`, tracked-лупы, компонентные источники с `spatialBlend >= 0.5`                                                          |
| Safe      | `Master/Safe`      | Звуки сейфа (`LockDial`): тики, звук корректного шага и слой `_puzzleBackgroundLayer` при применённом стетоскопе. Единственная шина, остающаяся слышимой в режиме стетоскопа |


Громкость: `SFXVolume` управляет **родительской** SFX-шиной — `SFX2D` и `SFX3D` наследуют её, оба слайдера настроек работают без миграций. Разделение категорий — структура для будущих снапшотов/даункейлов.

Автороутинг: `AudioManager.RouteToCategory(AudioSource)` выбирает группу по `source.spatialBlend >= 0.5f ? SFX3D : SFX2D` (fallback — общая `_sfxGroup`, если категория не назначена в инспекторе). Роутинг применяется: к источникам фабрик менеджера, к общему 2D-источнику (в `Awake`), к «осиротевшим» источникам после загрузки сцены (`OnSceneLoaded`) и ко всем источникам, переданным через публичную обёртку `RouteSourceToSfxGroup` (drag/scrape-лупы, эффекты и т.д.).

## Core Components

### AudioManager.cs (`Assets/Scripts/Core/AudioManager.cs`)

Синглтон-сервис звуковых эффектов (`AudioManager.Instance`, `DontDestroyOnLoad`). Музыкой не владеет — всё музыкальное (меню, геймплей, зоны, события) ведёт `MusicDirector` через Music-шину SFX AudioMixer.

Поля инспектора:


| Поле                      | Тип             | Описание                                                                      |
| ------------------------- | --------------- | ----------------------------------------------------------------------------- |
| `_sfxSource`              | AudioSource     | 2D-источник, через который играются все one-shot SFX (категория SFX2D)        |
| `_uiSfxSource`            | AudioSource     | 2D-источник меню/UI-звуков (`/AudioManager/UISFX` в обеих сценах, шина SFX2D); в `Awake` получает `ignoreListenerPause = true`, поэтому клики меню слышны и в паузе |
| `_sfxMixer`               | AudioMixer      | SFX AudioMixer с открытыми параметрами SFXVolume / MusicVolume                |
| `_sfxVolumeParameter`     | string          | Имя параметра SFX-громкости (по умолчанию `"SFXVolume"`)                      |
| `_musicVolumeParameter`   | string          | Имя параметра громкости музыки (по умолчанию `"MusicVolume"`)                 |
| `_sfxGroup`               | AudioMixerGroup | Общая SFX-группа — fallback, когда категория не назначена                     |
| `_sfx2DGroup`             | AudioMixerGroup | Группа категории 2D (one-shot SFX, фоновые слои); null → fallback `_sfxGroup` |
| `_sfx3DGroup`             | AudioMixerGroup | Группа категории 3D (пространственные звуки); null → fallback `_sfxGroup`     |
| `_safeGroup`              | AudioMixerGroup | Шина `Master/Safe` — звуки сейфа, не глушатся в режиме стетоскопа             |
| `_fadeDuration`           | float           | Дефолтная длительность фейда (1.5 с)                                          |
| `_ambienceRescanInterval` | float           | Частота re-scan сцены при мьюте эмбиента (0.5 с)                              |


Публичный API:

- `PlaySFX(AudioClip clip, float volume = 1f)` — one-shot на общем 2D-источнике. Подчиняется `AudioListener.pause`: в паузе молчит, а «застрявшие» one-shot'ы выстреливают при снятии паузы — для звуков меню это дефект, поэтому они идут через `PlayUISFX`.
- `PlayUISFX(AudioClip clip, float volume)` — one-shot на выделенном UI-источнике (`_uiSfxSource`, `ignoreListenerPause = true`): звук меню слышен и при `AudioListener.pause = true` (пауза, потеря фокуса). Используется `MenuButtonSounds` (hover/click в стартовом меню и меню паузы).
- `PlaySFXExclusive(AudioClip clip, float volume = 1f)` — сначала останавливает все звуки на SFX-источнике (для повторяющихся звуков, которые не должны накладываться сами на себя).
- `Play3DOneShot(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 10f)` — одноразовый 3D-звук из временного источника (самоуничтожается).
- `Play3DLoop(AudioClip clip, Transform target, float volume, float minDistance, float maxDistance)` — зацикленный пространственный источник, парентится к цели и регистрируется в системе фонового mute. Возвращает `AudioSource`. Rolloff — Linear: `maxDistance` — жёсткий порог тишины (как в `Play3DOneShot`; логарифмический rolloff по умолчанию игнорирует `maxDistance`).
- `RegisterLoopSource(AudioSource source, float volume)` / `UnregisterLoopSource(AudioSource source)` — управление уже существующими источниками в tracking-системе.
- `PlayBackgroundLayer(AudioClip clip, float volume = 1f, float fadeDuration = -1f)` — зацикленный 2D-слой (сердцебиение, гул) с независимым фейдом. Прежний слой дофейдивается перед запуском нового (иначе фейд бы «тёк»).
- `StopBackgroundLayer(float fadeDuration = -1f)` — фейд-аут и уничтожение слоя.
- `MuteBackground(float fadeDuration = -1f)` / `UnmuteBackground(float fadeDuration = -1f)` — глушит/возвращает музыку (`MusicDirector.MuteForEvent`/`RestoreAfterEvent`) и все tracked 3D-лупы; one-shot SFX не затрагиваются. Поддерживает вложенность (счётчик владельцев у директора).
- `MuteAmbience(float fadeDuration = 1.5f)` / `UnmuteAmbience()` — кинематографичный мьют для финальной катсцены: глушит **все** AudioSource сцены, кроме источников `MusicDirector` (проверка `OwnsSource`) и общего SFX-источника. Не требует регистрации — сканирует сцену и периодически (раз в `_ambienceRescanInterval`) режет новую эмбиент-громкость (огни, которые поджигает финал). Односторонний: credits-флоу уничтожает синглтон, восстановление не требуется.
- `EnterStethoscopeMode(float fadeDuration = -1f)` / `ExitStethoscopeMode(float fadeDuration = -1f)` — режим стетоскопа (загадка сейфа): фейдит шины `Music` и `SFX` (родительскую — глушатся и `SFX2D`, и `SFX3D`) в тишину через exposed-параметры, шина `Safe` (прямой ребёнок Master) не затрагивается. На входе также глушит музыку/tracked-лупы (`MuteBackground`), на выходе восстанавливает сохранённые громкости (`SystemSettings`) и состояние директора. Вложенность не поддерживается — владелец один (`LockDial`).
- `PlaySafeSFX(AudioClip clip, float volume = 1f)` — one-shot через отдельный источник `Safe_SFX`, маршрутизированный в шину `Safe` (создаётся лениво). Единственный звук, слышимый в режиме стетоскопа.
- `RouteSourceToSafeGroup(AudioSource source)` — направляет источник (например, слой усиленного звука сейфа) в шину `Safe`.
- `BelongsToSafeGroup(AudioSource source)` — проверка принадлежности источника шине `Safe`.
- `StethoscopeActive` — активен ли режим стетоскопа (используется `LockDial` для выбора между `PlaySFX` и `PlaySafeSFX`).
- `PlayNewMusicSource(AudioClip clip, bool loop, string trackName = null)` — фабрика: создаёт зацикленный 2D-источник под менеджером для `MusicDirector` (громкость 0, воспроизведение сразу).
- `ApplySavedVolumes()` — применяет сохранённые громкости (PlayerPrefs через `SystemSettings`) к шинам микшера.
- `ResolveMixerExternal()` — доступ к микшеру для `SystemSettings`.
- `RouteSourceToSfxGroup(AudioSource source)` — публичная обёртка маршрутизации для источников компонентов (например drag/scrape-лупы); маршрутизирует по `spatialBlend` в категорию SFX2D/SFX3D.

В `Awake`: сбрасывает статическое состояние tracking'а (`BackgroundTracking.ResetForNewSession`), сбрасывает `AudioListener.pause = false` (наследуемый от перехода сцены, где умирающий геймплейный мир молчит под экраном загрузки) и применяет сохранённые громкости; общий 2D-источник направляется в категорию SFX2D (если он ещё не на ней); UI-источнику (`_uiSfxSource`) выставляется `ignoreListenerPause = true` и выполняется роутинг в SFX2D. Подписывается на `SceneManager.sceneLoaded` и после каждой загрузки сцены направляет все «осиротевшие» AudioSource (без mixer group) в их категорию (SFX2D/SFX3D по `spatialBlend`).

### MusicDirector.cs (`Assets/Scripts/Core/MusicDirector.cs`)

Синглтон (`MusicDirector.Instance`, `DontDestroyOnLoad`), решает какой музыкальный трек сейчас играет и кроссфейдит треки через `AudioManager`. Реализует `ISaveable` (SaveId `"music_director"`). Дубль компонента в новой сцене работает как «носитель конфига»: его `_defaultGameTrack` принимается постоянным инстансом, сам дубль уничтожается.

Поля инспектора:


| Поле                 | Тип             | Описание                                                      |
| -------------------- | --------------- | ------------------------------------------------------------- |
| `_defaultGameTrack`  | MusicTrack      | Трек, играющий при старте игры                                |
| `_pauseMenuTrack`    | MusicTrack      | Трек паузы (меню); после закрытия меню возобновляется игровой |
| `_musicGroup`        | AudioMixerGroup | Music-шина, куда направляются все музыкальные источники       |
| `_minSwitchInterval` | float           | Дефолтная длительность фейда (2 с)                            |


Примечание: `_minSwitchInterval` больше **не** анти-фликер интервал — в `RequestTrackInternal` новый запрос всегда побеждает немедленно, старый трек уходит под новый. Значение используется как дефолтная длительность фейда (и фейд-аута ownerless-источника).

Ключевой API (статический):

- `RequestTrack(MusicTrack track, bool playOnce = false)` — запрос трека; тот же активный игнорируется, playOnce-проигранные пропускаются; во время horror-мьюта запрос ставится в очередь (при restore срабатывает только последний). Возвращает `true`, если запрос принят.
- `RequestIdleCueTrack(MusicTrack track)` — как `RequestTrack`, но запоминает cue: трек-подсказка никогда не сохраняется как доминантный (загрузка в дефолт трека сцены), `ClearTrack` возвращает предыдущий трек.
- `ClearTrack(MusicTrack track)` — возвращает предыдущий трек после временного запроса.
- `PlayMenuMusic()` / `PlayGameMusic()` — переключение на музыку паузы и обратно; позиция игрового трека запоминается и восстанавливается. Работает и во время horror-мьюта (в паузе `AudioListener.pause`, menu-трек — единственный слышимый звук); `PlayGameMusic` во время мьюта ничего не включает — restore сделает это сам.
- `MuteForEvent(float fadeDuration = 0)` / `RestoreAfterEvent(float fadeDurationOverride = -1)` — управление звуками для horror-событий; вложенные события считаются по числу владельцев (`_muteOwnerCount`).
- `DuckActiveTrack(float targetMultiplier, float fadeDuration)` — приглушить активный трек и вернуть громкость (используется лифтом). Отменяется сменой трека; во время мьюта или меню игнорируется. Восстановление — к запомненному уровню до duck, не к `track.Volume`.
- `FadeOutForLoadingScreen(float fadeDuration = 1.5f)` — глушит все source пула и очищает активный трек при переходе сцены (фейд идёт на отдельном `DontDestroyOnLoad`-хосте `LoadingFadeHost`, переживающем уничтожение менеджера).
- `RegisterTrack(MusicTrack track)` — регистрация трека для резолва по имени из сейва (статический словарь `KnownTracks`, зоны регистрируют треки в `Awake`).
- `OwnsSource(AudioSource source)` — принадлежит ли источник директору (пул или активный кроссфейд); используется `MuteAmbience`, чтобы не сглотнуть музыку.
- Свойства: `Instance`, `ActiveTrack`, `MutedForEvent`, `IsDefaultTrackActive` (меню и мьют не активны, играет дефолтный трек сцены — проверяют системы, сидящие поверх дефолтного эмбиента, например idle-подсказка).

Особенности:

- Источники пулятся по трекам (`Dictionary<MusicTrack, AudioSource>`) и **паузятся, а не уничтожаются** — трек возобновляется с позиции фейд-аута. `ignoreListenerPause = true` только у track паузы (единственный слышимый звук при `AudioListener.pause`).
- Треки, запрошенные с `playOnce`, запоминаются и никогда не перезапускаются.
- Кроссфейд (`FadeRoutine`) переключает bookkeeping (`_activeSource`) в **начале** фейда; прерывание fade (пауза, мьют, потеря фокуса) дофинализирует уходящий источник (`_fadingOutSource`) — источник-сирота на полугромкости невозможен.
- `ISaveable`: сериализует доминирующий трек (имя + позиция) и список проигранных playOnce-треков. Пока меню владеет музыкой, сохраняется прерванный игровой трек. Idle-cue в сейв не попадает.
- В `Start` доминантный трек из сейва имеет приоритет над дефолтом сцены; в сцене без игрового трека играет `_pauseMenuTrack` как музыка меню.
- Нелоопинг-трек, доигравший до конца, возвращает дефолтный трек сцены (`RestoreFallbackTrack`).
- Пул источников прогревается (`LoadAudioData`) в `Start` — стриминг без хичей на первом воспроизведении.

Защитные механизмы:

- **Watchdog мьюта** (`MuteMaxHoldSeconds = 120`, проверяется в `Update` до раннего return при мьюте): владелец, не отпустивший мьют (событие disabled mid-play с замороженным failsafe, ручной `MuteForEvent` без restore), не может заглушить игру навсегда — счётчик принудительно обнуляется (`ForceReleaseMute`), пре-мьют трек возвращается.
- **Мьют на потерю фокуса (Alt-Tab):** menu-трек глушится, остальные источники пула замораживаются (`Pause`). Контроль — в `Update`, а не в `OnApplicationFocus`, потому что порядок callback'ов с `GameManager` не гарантирован; иначе нелооп-трек «доигрывал» за кадром и лёг fallback посреди финала. На рефокусе источники размораживаются (кроме состояния паузы игры).
- **Track-life guard** (стейт-машина в `Update`): если активный источник остановился без запроса, реакция по причине — пауза/мьют/фейд владеют аудио сами; доигравший нелооп-трек → `RestoreFallbackTrack`; «никогда не стартовавший» (гонка стриминга, `Play()` не взялся) → ревайв с rate-limit 0.5 с. Дискриминатор «доиграл» vs «не стартовал» — флаг наблюдаемого воспроизведения `_activeSourcePlayed` (`source.time` ненадёжен). Мёртвый луп-источник (потеря устройства) пересоздаётся через фабрику.
- **Restore из мьюта:** доигравший на момент мьюта нелооп-трек не рестартуется с нуля — уходит в `RestoreFallbackTrack`.

### MusicTrack.cs (`Assets/Scripts/Core/MusicTrack.cs`)

ScriptableObject-дескриптор трека (меню `Audio/Music Track`, рекомендуется в `/Assets/Data/Sound`). Поля: `_clip` (AudioClip), `_volume` (float, 0.4), `_loop` (bool, true), `_fadeDuration` (float, −1 = использовать дефолт `MusicDirector`). Свойства: `Clip`, `Volume`, `Loop`, `FadeDuration`.

### MusicZone.cs (`Assets/Scripts/Core/MusicZone.cs`)

Триггерная зона: запускает трек при входе игрока (тег `Player`). Каждая зона срабатывает ровно один раз — повторный вход не меняет музыку; выход из зоны не трогает музыку (либо трек доигрывает до конца, либо его заменит другая непосещённая зона).

Поля: `_zoneTrack` (MusicTrack), `_playOnce` (bool, false — трек помечается проигранным и не восстанавливается сейвами). В `Awake` регистрирует трек в `MusicDirector`; при принятом запросе сохраняет игру (`SaveManager.Instance?.Save()`).

### AudioFades.cs (`Assets/Scripts/Core/AudioFades.cs`)

Статический хелпер фейдов: `AudioFades.Volume(AudioSource source, float to, float duration, Action onComplete = null)` — корутина лерпает громкость по `Time.unscaledDeltaTime` (работает на паузе), молча завершается при уничтожении источника. Используется и `AudioManager`, и `MusicDirector`.

### IdleMusicTrigger.cs (`Assets/Scripts/Core/IdleMusicTrigger.cs`)

Анти-stuck подсказка: если игрок долго бездействует и `MusicDirector.IsDefaultTrackActive`, запрашивает idle-трек через `RequestIdleCueTrack`. Cue никогда не сохраняется как доминантный трек.

### ImpactSoundEmitter.cs (`Assets/Scripts/Core/ImpactSoundEmitter.cs`)

Компонент удара: на объекте с `Rigidbody` и не-триггерным коллайдером играет случайный клип из `_impactClips` в `OnCollisionEnter`. Громкость масштабируется от силы удара (`_minImpactVelocity` → `_minVolume`, `_maxImpactVelocity` → `_maxVolume`), pitch слегка рандомизируется (0.9–1.1). Источник `AudioSource` создаётся в `Awake` (`AddComponent`) и маршрутизируется в SFX-группу через `AudioManager.RouteSourceToSfxGroup`.

Важно про наложение: используется `PlayOneShot` — он **накладывает** новый проигрыш поверх уже играющих на том же источнике, ничего не прерывая. Кулдаун между ударами (`_cooldown = 0.15`) намного короче длины типового клипа, поэтому предмет, бьющийся дважды (удар + заваливание корпуса), раньше накладывал копии клипа друг на друга — второй звук стартовал до окончания первого. Флаг `_skipWhilePlaying` (default `true`) проверяет `_audioSource.isPlaying` и пропускает запуск, пока предыдущий проигрыш не закончился — наложение невозможно в принципе.

| Поле | Тип | Описание | Default |
| --- | --- | --- | --- |
| `_impactClips` | AudioClip[] | Клипы на удар, случайный выбор | — |
| `_minVolume` / `_maxVolume` | float (0–1) | Громкость при Min / Max Impact Velocity | `0.4` / `1` |
| `_minImpactVelocity` | float | Относительная скорость (м/с), ниже которой тишина | `1.5` |
| `_maxImpactVelocity` | float | Скорость, при которой звук достигает полной громкости | `6` |
| `_cooldown` | float | Секунды между срабатываниями | `0.15` |
| `_skipWhilePlaying` | bool | Не запускать новый звук, пока предыдущий играет | `true` |
| `_spatialBlend` | float (0–1) | 3D-размытость (1 = полностью позиционный) | `1` |
| `_minDistance` / `_maxDistance` | float | Дистанции аттенюации (Logarithmic rolloff) | `1` / `15` |

### ImpactSoundLoadGate.cs (`Assets/Scripts/Core/ImpactSoundLoadGate.cs`)

Статический «гейт» тишины: глушит импакт-звуки первые 3 секунды (`SettleGraceSeconds = 3f`) после загрузки сцены. Без него предметы, заспавненные чуть внахлёст или на грани равновесия, при первом шаге физики играют удары, которых игрок не вызвал (включая кадр после телепорта сейв-рестором). Окно увеличено с 1 с до 3 с, когда восстановление решённых загадок перешло на мгновенные `RunInstant`-последовательности (`DelayedActionSequence`): телепорт предметов в лежачие позы и расселение коллайдеров стен дают остаточные микро-удары в первые секунды после загрузки.

- `RegisterSceneLoad()` — фиксирует момент загрузки (по `Time.time`); вызывается из `Awake` подписчиков, каждая загрузка сцены перерегистрируется.
- `ShouldSuppress` — `true`, пока идёт грейс-период. На scaled-времени, поэтому экран загрузки с `timeScale = 0` корректно продлевает тишину до старта геймплея.

Подписчики: `ImpactSoundEmitter` и `PhysicsDraggable` — оба зовут `RegisterSceneLoad()` в `Awake` и выходят из `OnCollisionEnter` при активном гейте. В `PhysicsDraggable` проверка стоит **после** `RecordGroundContact` — fall-safety (возврат упавшего сквозь пол предмета) работает с первого кадра, глушится только звук. Скрейп-луп не глушится: он включается только от скольжения предмета, которое при загрузке не возникает.

### InventoryAudio.cs (`Assets/Scripts/Inventory/InventoryAudio.cs`)

Слушается `InventorySystem.OnCrafted` и через `AudioManager.PlaySFX` играет звук крафта (`_craftClip`, `_craftVolume = 1`). Ставится на тот же GameObject, что и `InventorySystem`.

## Integration Guide

### Добавление нового окружающего звука

Зацикленный 3D-звук (гудящая лампа, вентиляция):

1. `AudioManager.Instance.Play3DLoop(clip, transform, volume, minDistance, maxDistance)` — создаст управляемый дочерний GameObject с `AudioSource` (3D-спатиализация, категория SFX3D) и вернёт источник.
2. Остановка — `AudioManager.Instance.UnregisterLoopSource(source)`.

Разовый 3D-звук в точке: `AudioManager.Instance.Play3DOneShot(clip, position, volume, minDistance, maxDistance)`.

### Музыкальные зоны

1. Создайте ассет `MusicTrack` (меню `Audio/Music Track`), задайте клип, громкость, loop и фейд.
2. Повесьте `MusicZone` на объект с trigger-коллайдером и назначьте `_zoneTrack`.
3. При необходимости включите `_playOnce` — трек сыграет один раз за прогресс (состояние переживает сейвы).

### Обработка паузы и mute

- **Музыка:** владеет `MusicDirector` — `PlayMenuMusic`/`PlayGameMusic` вызываются из `GameManager`, при `Time.timeScale == 0` фейды идут по `Time.unscaledDeltaTime`.
- **Mute для событий (horror):** `AudioManager.MuteBackground/UnmuteBackground` или напрямую `MusicDirector.MuteForEvent/RestoreAfterEvent` — глушится музыка и tracked-лупы, one-shot SFX продолжают звучать. Вложенность — по счётчику владельцев; утечка держателя дольше 120 с чинится watchdog'ом.
- **Мьют всей сцены (финальная катсцена):** `AudioManager.MuteAmbience` — глушит всё, кроме музыки директора и one-shot SFX, включая эмбиент, появляющийся во время катсцены.
- **Слои:** `PlayBackgroundLayer`/`StopBackgroundLayer` для независимых 2D-слоёв с фейдами.
- **Стетоскоп (загадка сейфа):** при применении стетоскопа `LockDial` вызывает `AudioManager.EnterStethoscopeMode` — музыка и все SFX-шины фейдятся в тишину, а тики/звук корректного шага и `_puzzleBackgroundLayer` идут через шину `Safe` (`PlaySafeSFX`/`RouteSourceToSafeGroup`) и остаются слышимыми. Выход из паззла — `ExitStethoscopeMode`, громкости восстанавливаются. Требует шину `Master/Safe` в `SFX.mixer` и назначенное поле `_safeGroup` у `AudioManager` (в сцене `Game.unity` уже назначено).
- **Громкости:** `SystemSettings` пишет в exposed-параметры микшера (`SFXVolume` / `MusicVolume`), изменения применяются мгновенно к играющему звуку.

&nbsp;