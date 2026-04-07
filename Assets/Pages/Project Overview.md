# Project Documentation Overview

Escape Room от первого лица. Один игрок, 5 комнат, последовательные загадки. Unity 6000.3, URP, Input System.

## Игровые системы

- Flashlight System — режимы линз, скрытые надписи на стенах.
- Pressure Puzzle System — загадка с рычагами и давлением.
- Sliding Puzzle System — классический пятнашки в 3D.
- Save System — автосохранения, бэкапы, ISaveable.
- Inventory System — инвентарь, крафт, 3D-осмотр.
- Drag Interaction System — физические двери и ящики.
- Audio System — BGM, SFX, аудиовизуальная синхронизация.
- Horror System — хоррор-события: триггеры, эффекты, сохранение.

## Ядро проекта

- GameManager — структура сцены, комнаты, пауза, UIManager, InputManager.
- toDoList — текущий статус разработки.

## Стек

| Технология | Версия / Детали |
|---|---|
| Unity | 6000.3 |
| Render Pipeline | URP 17.3.0 |
| Input System | com.unity.inputsystem 1.18.0 |
| UI | uGUI (Canvas) |
| NavMesh | com.unity.ai.navigation 2.0.10 |
| Cinemachine | 3.1.6 |
| Post Processing | com.unity.postprocessing 3.5.1 |

## Структура скриптов

```
Assets/Scripts/
├── Core/           # GameManager, UIManager, InputManager, AudioManager, SaveManager
│                   # HorrorSystem, HorrorEvent, GameConfig, NeonLightFlicker, ...
├── Player/         # FPSController, FootstepController, CameraZoom, PhysicsGrabber
├── Interaction/    # IInteractable, IDraggable, DoorInteraction, DrawerDrag, CodeLock, ...
├── Inventory/      # InventorySystem, ItemDataSO, CraftingRecipe, PickableItem, ItemInspector
│   └── UI/         # InventoryUI, InventorySlot, DraggableItem, InventoryItemPreview, ...
├── Flashlight/     # FlashlightController, FlashlightConfig, HiddenWallSign, FlashlightLagFollow
├── Room/           # RoomController
├── Buttons/        # BaseButton, CloseBtn, ResumeBtn, ResetProgressBtn
├── UI/             # UIPanel, CodeLockUI, InteractionUI, NoteUI, PopupMessageSystem, ...
├── Data/           # GameSaveData, NoteData
├── Text/           # TypewriterEffectPro
├── Editor/         # MissingScriptCleaner, PressurePuzzleEditor, PuzzleSolverUtility
├── PuzzleManager.cs
└── PuzzleElement.cs
```
