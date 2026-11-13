<!-- version: 1 -->

## Обзор

Фейковая слот-машина — развлекательная мини-игра **внутри ОС ноутбука медсестры** (`NurseryRoom`). Открывается двойным кликом по иконке «My-CGS» на рабочем столе: создается окно `LaptopSlotMachineWindow` (потомок `LaptopWindow`). Математика выплат повторяет реальные слоты BGaming: фиксированные ленты барабанов (reel strips), точные вероятности из состава лент, линии выплат, WILD, SCATTER, фри-спины с ретригером. Наград игроку никаких — игра чисто для атмосферы.

Скрипты: `Assets/Scripts/Puzzle/LaptopScripts/` (рядом с остальной системой ноутбука).

```
Иконка Slots (LaptopDesktopIcon, IconGrid)
└── double click → LaptopWindowManager.OpenFile(SlotMachineConfig)
    └── CreateWindow → префаб PRF_LaptopSlotMachineWindow
        ├── ReelsArea (корпус машины 640×460, тёмный)
        │   └── Viewport
        │       ├── Reel0/1/2   # окна 130×390, RectMask2D; лента ячеек строится кодом
        ├── BalanceText (шапка, белый)   ├── BetText + −/+ (внизу, слева от SPIN)
        ├── WinText (золото с обводкой)  ├── MessageText
        ├── SpinButton / PaytableButton  └── PopupOverlay (попапы выигрыша/бонуса/пейтабла)
```

## Математика

- **Ленты**: по 30 стопов на барабан. Вероятность символа = доля вхождений в ленту (`J×3, Q×3, K×4, A×4, BELL×4, BAR×3, STAR×3, BONUS×2, WILD×2, SEVEN×2`).
- **Спин** = 3 независимых стопа. Видимое окно 3×3: ряды = `strip[stop]`, `strip[stop+1]`, `strip[stop+2]`.
- **3 линии** (верх/центр/низ), ставка на линию 1/2/5 (кнопки −/+). Списание = ставка × 3.
- **WILD** подменяет любой символ слева; 3 WILD = 220.
- **Выплаты за линию** (×ставка): SEVEN 110 (2× = 2), BAR 45 (2× = 1), BELL 16, A 7, K 5, Q 4, J 3.
- **SCATTER** (`STAR`): 3+ в любом месте → ×2 общей ставки.
- **BONUS** (`BONUS`): 3+ → 10 фри-спинов ×2, ретригер возможен.
- **RTP = 96.5%** — откалибровано Monte-Carlo (1 млн спинов × 3 seed'а, разброс < 0.1%).

Проверка своих правок: ContextMenu **Estimate RTP (1M spins)** на ассете `SlotMachineConfig` (`SlotMachineRtpEstimator` — та же логика, что в рантайме).

## Компоненты

### `LaptopSlotMachineFile` (ScriptableObject)
Ассет: `Assets/Data/LaptopData/SlotMachineConfig.asset`. Конфиг: символы (id, TMP-glyph фолбэк, цвет, спрайт), 3 ленты, таблица выплат, scatter/bonus параметры, экономика (стартовые кредиты 100, ставка, 3 линии). `fileId` обязателен (сейв баланса).

### `LaptopSlotMachineWindow : LaptopWindow, ISaveable`
Окно игры. Спин: списание → все 3 барабана стартуют одновременно с **постоянной скоростью** (модель BGaming `reel.c.ts`), тормозок только на последних 2.5 ячейках, остановка по очереди (левый первый, шаг 0.45с), отскок при посадке. Затем оценка: линии → scatter → бонус. Попапы: `YOU WIN! / +N` с раскладом комбинаций, `SCATTER!`, `BONUS!`, `RETRIGGER!`, `BONUS COMPLETE!` — закрываются кликом, всегда поверх окна (`SetAsLastSibling`). Баланс анимируется count-up (0.8с, ease-out). Кнопка **PAYTABLE** — попап с полной таблицей из конфига.

**Save**: `SaveId = "slotmachine." + fileId`. Сохраняет `credits`. Ставка 1/2/5 (`_betOptions`).

### `LaptopWindowManager` (изменён)
Добавлены `_slotMachineWindowPrefab` и ветка `file is LaptopSlotMachineFile` в `CreateWindow`. Ассет добавлен в `_allFiles` ноутбука медсестры (восстановление окна из сейва).

## Ассеты

| Ассет | Назначение |
| --- | --- |
| `Assets/Data/LaptopData/SlotMachineConfig.asset` | Конфиг игры (числа калибровки V5) |
| `Assets/Prefabs/LaptopPrefabs/PRF_LaptopSlotMachineWindow.prefab` | Окно (скопирован со стилем `PRF_LaptopTextWindow`) |
| `Assets/Sprites/SlotMachine/*.png` | 10 карточек символов + `MyCGS-icon.png` (иконка стола) |

Спрайты сгенерированы через **PhotoCraft** (256×256, скруглённый квадрат + буква), импорт: `Texture Type = Sprite (Single)`. Поле `SlotSymbol.sprite` — приоритет; при пустом рендерится TMP-глиф.

## Известные грабли

- При пересоздании дочерних объектов префаба перепроверять все ссылки `LaptopSlotMachineWindow` — batch-правки откатываются целиком, старые ссылки остаются битыми (симптом: `UnassignedReferenceException` на `_reelViewports`, пустые барабаны).
- RTP линеен по выплатам: умножил таблицу на 1.2 — RTP вырос на ~20%. После любой правки лент/выплат гонять `Estimate RTP`.
