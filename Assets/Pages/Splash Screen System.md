<!-- version: 3 -->

## Обзор

Кастомный сплэш-экран внутри стартовой сцены (`Start.unity`) — отдельная сцена не используется. Полноэкранный оверлей поверх меню (`ScreenSpaceOverlay`, sortingOrder 100): логотипы по очереди поверх чёрного фона, версия билда, затем плавное растворение в главное меню с синхронным прилётом камеры. Скип по нажатию отключён — последовательность всегда играется целиком. Играется **один раз за запуск приложения** — при возврате в меню из игры (пауза, титры) сплэш не повторяется. Встроенный сплэш Unity отключён (Project Settings → Player → Splash Image → Show Splash Screen off; доступно на Personal с Unity 6).

---

## Иерархия объектов (Start.unity)

```
SplashCanvas                Canvas (ScreenSpaceOverlay, sortingOrder 100) + SplashController + CanvasGroup
├── BlackBackground         Чёрный Image на весь экран, raycastTarget — блокирует клики в меню
├── LogoCompany             Image + CanvasGroup (alpha 0) — логотип компании
├── LogoGame                Image + CanvasGroup (alpha 0) — логотип игры
└── VersionText             TMP + BuildVersionLabel — версия видна весь сплэш
```

`CanvasScaler` — `ScaleWithScreenSize`, референс 1920×1080, `Expand`. Корневой `CanvasGroup` — то, что растворяется в конце (фон + логотипы + версия вместе).

---

## Таймлайн последовательности

```
для каждого логотипа (LogoCompany → LogoGame):
    fade in → показ → fade out → чёрная пауза
затем:
    [старт dolly камеры + fade out чёрного фона — одновременно]
    канвас отключается, меню активно
```

- Событие **On Splash Finished** срабатывает в момент начала растворения фона — к нему привязан `MenuCameraDolly.StartArrival` на `/Main Camera` (persistent call, RuntimeOnly). Камера дрейфует в сцену синхронно с проявлением меню.
- Бонус первого логотипа: первый элемент массива показывается дольше — он появляется из полной черноты и перцептивно «короче».
- Все фазы на `Time.unscaledDeltaTime` — загрузка не зависит от `Time.timeScale`.

---

## `SplashController` — `Assets/Scripts/UI/SplashController.cs`

Компонент на `SplashCanvas`. Играет последовательность и управляет блокировкой ввода.


| Поле                          | В сцене | Описание                                                              |
| ----------------------------- | ------- | --------------------------------------------------------------------- |
| `_fadeInSeconds`              | `1.2`   | Время проявления логотипа (с)                                         |
| `_fadeOutSeconds`             | `1.5`   | Время затухания логотипа (с)                                          |
| `_logoDisplaySeconds`         | `1.5`   | Пауза полностью видимого логотипа (с)                                 |
| `_firstLogoExtraSeconds`      | `2.5`   | Бонус к показу первого лого (появляется из чёрного, читается короче)  |
| `_menuRevealSeconds`          | `1.5`   | Растворение чёрного фона в меню (с)                                   |
| `_pauseAfterFadeOutSeconds`   | `0.5`   | Чёрная пауза после каждого fade out (с)                               |
| `_logos`                      | 2 лого  | `CanvasGroup[]` — логотипы по порядку                                 |
| `_splashGroup`                | —       | Корневой `CanvasGroup`, растворяется в конце                          |
| `_splashRoot`                 | —       | Корень сплэша, отключается в конце                                    |
| `_eventSystem`                | —       | `EventSystem`, отключается на время сплэша (блок клавиатуры/геймпада) |
| `_onSplashFinished`           | —       | UnityEvent → `MenuCameraDolly.StartArrival`                           |


Публичное C#-событие `SplashFinished` — для кодовых подписок. Защита от повтора: биты срабатывают ровно один раз за прогон.

## Один раз за запуск приложения

Статический флаг `_splashPlayedThisSession` переживает перезагрузки сцены в рамках одного запуска приложения (сбрасывается `RuntimeInitializeOnLoadMethod` в редакторе):

- **Первый вход в `Start`** (запуск игры) — сплэш играется целиком.
- **Возврат из `Game`** («В главное меню» из паузы или с титров) — `SplashController` в `Start()` видит флаг, мгновенно скрывает сплэш-канвас (логотипы не повторяются) и поднимает `OnSplashFinished` без блокировки `EventSystem` — **dolly камеры играет и на возврате**: меню открывается с тем же прилётом, что и при первом запуске.
- Перезапуск приложения — сплэш снова играется.

## Блокировка ввода

- **Мышь** — блокируется самим сплэшем: `BlackBackground` (`raycastTarget`) перекрывает канвас меню.
- **Клавиатура/геймпад** — `SplashController` отключает `EventSystem` в `Start()` и включает обратно после `SetActive(false)` канваса. Если сплэш прерван (`OnDisable`), `EventSystem` включается принудительно — меню не остаётся заблокированным.

## Версия билда

`BuildVersionLabel` (`Assets/Scripts/UI/BuildVersionLabel.cs`) пишет `Application.version` (Project Settings → Player → Version, сейчас `v.25PlayTest`) в TMP-лейбл в `Awake()`. Стоит на `SplashCanvas/VersionText` и на `MainMenuCanvas/MAIN/VersionText` — версия видна весь сплэш и остаётся в меню.

## Изменить логотипы / тайминги

- Новый логотип: добавь `Image` + `CanvasGroup` (alpha 0) под `SplashCanvas` и включи в `_logos` массив `SplashController` — порядок массива = порядок показа.
- Тайминги крутятся в инспекторе, в том числе в Play Mode (сброс после выхода — выставлять в edit mode).
- Спрайты логотипов: `Assets/Sprites/Icons/logoCompany.png` и `logo.png`.
