## Overview

The electric puzzle is a wire-connection panel. The player inserts a fuse to power the panel, then connects six colored terminals to six neutral terminals in the correct order. When all connections are correct the indicator lamp turns green. Pulling the lever with a correct solution completes the puzzle; pulling it with a wrong solution instantly resets all wires.

Camera, cursor management, FPS input blocking, and ESC handling are delegated to `PuzzleModeController` — the same shared component used by all puzzles in the project. `ElectricPuzzleController` subscribes to its `OnEntered` / `OnExited` / `OnSolved` events.

---

## Scripts

```
ElectricPuzzleController.cs   Orchestrator — input, wire lifecycle, fuse, save/load
ElectricLever.cs              Lever animation and one-shot pull event
ElectricTerminal.cs           Per-terminal state holder (type, index, attached wire)
ElectricWire.cs               Verlet-physics wire simulation and LineRenderer rendering
ElectricWireSettings.cs       Serializable settings struct shared by all wires
ElectricPuzzleData.cs         ScriptableObject — solution mapping and per-wire colors
```

---

## Puzzle Flow

```
Player clicks panel
  └─ PuzzleInteractable calls PuzzleModeController.EnterPuzzleMode()
       └─ HandleEntered()  →  _isOpen = true
                             Cinemachine camera blends to ElectricCamera
                             FPS input blocked, cursor freed
                             PuzzleInventoryBar shown (_showInventoryBar = true)
                             ElectricLever.SetInteractionEnabled(true)

Player inserts fuse
  Drag Safeguard from PuzzleInventoryBar → drop on Safeguardanchor
  └─ HandleDrop() raycasts _terminalLayer, checks hit.collider == _fuseAnchorCollider
       └─ AnimateFuseInsertion()  →  Lerp fuse mesh from offset to anchor (ease-out, 0.5s)
            └─ FinalizeFuseInsertion()  →  _fuseInserted = true, fuseItemId saved
                                          anchor collider disabled, lamp = red, Save()

Player drags wires  (only after fuse is inserted; lamp must be on)
  LMB press on colored terminal  →  StartDrag()       creates ElectricWire
  LMB release on neutral terminal →  ConnectActiveWire() snaps wire, EvaluateWires()
  LMB on occupied terminal        →  PickUpWire() / PickUpWireFromNeutral()
  RMB                             →  CancelActiveDrag() discards floating wire

EvaluateWires()
  CheckSolution() matches _connections[] against ElectricPuzzleData.Solution
  ├─ correct → lamp turns green, _wiresCorrect = true, Save()
  └─ wrong   → lamp stays red

Player clicks lever (pCube17 / ElectricLever)
  TryInteractLever() returns early if !_fuseInserted
  ├─ Wrong wires  →  particles play, ResetAllWires() instantly, lever animates down
  │                   OnPulled fires → HandleLeverPulled() → lever.Reset() returns it
  └─ Correct wires →  _isSolved = true
                        lever.Interact() → animates down
                        OnPulled fires → HandleLeverPulled()
                        ├─ _cinematicCamera assigned → SolvedCinematicRoutine():
                        │    1. Quick fade to black (_cutFadeDuration, default 0.3s)
                        │    2. Instant cut to _cinematicCamera (EllectricCamera)
                        │    3. Fade from black — player sees cinematic shot
                        │    4. ActivatePower(), UpdateLamp() (green), _solvedClip, StartSolvedLoop()
                        │       _lampAnimator.SetTrigger(_lampAnimTrigger) — lamp animation
                        │    5. Hold shot for _lampAnimDuration (default 3s)
                        │    6. Fade to black (_solvedFadeDuration, default 1s)
                        │    7. Deactivate cinematic camera, restore blend
                        │    8. _controller.SetSolved() → ExitPuzzleMode(), camera blends to player
                        │    9. Fade from black — player regains control
                        └─ _cinematicCamera null → fallback: ActivatePower(), lamp, sound, SetSolved()
  ESC blocked during cinematic via IPuzzleExitGuard.CanExitPuzzle()

Player exits puzzle
  └─ HandleExited()  →  _isOpen = false
                        CancelActiveDrag(), ElectricLever.SetInteractionEnabled(false)
                        PuzzleModeController.ExitPuzzleMode() restores camera, input, cursor
```

---

## Key Design Points

**PuzzleModeController delegation.** Camera blending, cursor management, FPS input blocking, ESC handling, and the puzzle inventory bar are all handled by `PuzzleModeController` — the same shared component used by all puzzles. `ElectricPuzzleController` subscribes to `OnEntered`, `OnExited`, and `OnSolved` events in `OnEnable()` and unsubscribes in `OnDisable()`.

**Fuse gates everything.** Without a fuse the lamp stays off, `TryInteractLever` returns early, and wires can still be moved — but the panel is treated as unpowered. Only `FinalizeFuseInsertion()` unlocks the lever and lights the lamp.

**Lever is locked outside puzzle mode.** `ElectricLever.CanInteract()` checks `_interactionEnabled && !_isPulled`. The flag is set to `true` only in `HandleEntered()` and back to `false` in `HandleExited()`, preventing the player from pulling the lever through the world camera before entering the puzzle.

**Fuse insertion is an animated mesh, not a prefab spawn.** `_fuseMesh` is an existing GameObject in the scene (hidden until insertion). `HandleDrop()` starts `AnimateFuseInsertion()` — a coroutine that Lerps the mesh from an offset position to the anchor with ease-out over `_fuseInsertDuration` (0.5s). A ghost preview (`_fuseGhostAlpha = 0.4`) shows a semi-transparent fuse while dragging over the anchor.

**Lamp is a material tint, not a light source.** `_lampRenderer` (MeshRenderer on `pSphere25`) instantiates a unique material in `Awake()` and toggles `_BaseColor` / `_EmissionColor` between red and green. HDR emission values (3.59) give a bright glow. The lamp turns green during the solved cinematic — not when the lever is clicked — to sync with the camera cut and `ActivatePower()`.

**Solved cinematic.** When the lever is pulled with correct wires, `HandleLeverPulled()` starts `SolvedCinematicRoutine()` — a coroutine that fades to black, cuts to `_cinematicCamera` (EllectricCamera), fades back in, then activates power + lamp + sound + lamp animation, holds the shot for `_lampAnimDuration`, fades to black again, and blends back to the player camera. ESC is blocked during the cinematic via `IPuzzleExitGuard`. If `_cinematicCamera` is null, the puzzle solves immediately without a cinematic (fallback). The `CinemachineBrain` blend is temporarily set to 0 for instant cuts hidden by fades, and restored to the original value for the exit transition.

**Wire reset is instant.** `ResetAllWires()` is called in `TryInteractLever` on the same frame the player clicks — before the lever animation starts. The player sees all wires vanish immediately.

`**_isPulling` flag prevents double-fire.** `OnPulled` fires only when the lever completes a *pull* animation (`_isPulling = true`). The return animation (`Reset()`) does not re-fire the event.

**Auto-find references.** `_lever` is found automatically via `GetComponentInChildren<ElectricLever>(includeInactive: true)` in `Awake()` if not assigned. `_referenceRenderer` (for Light Layers) is similarly auto-found.

**Wire material inheritance.** Each wire creates a `new Material(wireMaterial)` and overrides `_BaseColor` with the terminal's color. `Wire.mat` controls shared rendering properties. Smoothness is set to 0.5 for a rubber-like look.

**Wire sleep system.** Each wire monitors interior point velocity; after 6 consecutive stable frames (below `SleepThresholdSq`), simulation pauses entirely — saving CPU and eliminating micro-vibration. Any `Wake()` call (from `ConnectEnd` / `DisconnectEnd`) resumes simulation.

**Inter-wire repulsion at load only.** `repulsionRadius` (0.025) is applied during `PresettleWire` / `JointPresettle` so wires load without overlapping. At runtime repulsion is disabled to avoid fighting constraints and causing oscillation.

**Solved ambient loop.** After solving, a 3D `AudioSource` on `SolvedLoopAudio` plays a looping generator hum. 3D settings are configured in code at runtime: `spatialBlend = 1.0` (fully 3D), `Linear` rolloff, `minDistance = 3m` (full volume), `maxDistance = 6m` (fades to silence). The source is at world Y ≈ -4.6 (basement); the 1st floor is at Y ≈ 0 (4.6m above) and the 2nd floor at Y ≈ 6.2 (10.8m above). With maxDistance = 6m the sound is inaudible on both upper floors. Both distances are exposed as Inspector fields `_solvedLoopMinDistance` and `_solvedLoopMaxDistance`. The source is registered with `AudioManager` for mute/volume tracking.

---

## Save Data

`SaveId = "electric_puzzle"`

```json
{
  "isSolved": false,
  "wiresCorrect": false,
  "connections": [3, 5, -1, -1, -1, -1],
  "fuseInserted": true,
  "fuseItemId": "safeguard_01"
}
```

`connections[i]` is the neutral-terminal index connected to colored terminal `i`, or `-1` if disconnected. Partial progress and fuse state are saved on every change. On load, pending data is applied in `ApplyPendingLoad()` (called from `Start()`), wires are reconstructed, pre-settled via `ElectricWire.JointPresettle()`, and the fuse visual is restored via `ShowFuseMesh()`.

---

## Solution Configuration

Open `Assets/Data/ElectricPuzzleData.asset` in the Inspector.


| Field         | Description                                                                             |
| ------------- | --------------------------------------------------------------------------------------- |
| `_solution`   | `solution[i]` = index of the neutral terminal that colored terminal `i` must connect to |
| `_wireColors` | Visual color of each wire; index matches the colored terminal index                     |


Default solution: `[3, 5, 1, 4, 0, 2]`

Wire colors (from the asset):


| #   | Color  | RGBA               |
| --- | ------ | ------------------ |
| 0   | Red    | `(1, 0.1, 0.1, 1)` |
| 1   | Orange | `(1, 0.5, 0, 1)`   |
| 2   | Green  | `(0.16, 1, 0, 1)`  |
| 3   | White  | `(1, 1, 1, 1)`     |
| 4   | Blue   | `(0.1, 0.4, 1, 1)` |
| 5   | Black  | `(0, 0, 0, 1)`     |


---

## Prefab Setup

```
electric  (root — ElectricPuzzleController, BoxCollider, PuzzleModeController, PuzzleInteractable, Interactable Layer)
├── ElectricCamera            CinemachineCamera — assign to PuzzleModeController._puzzleCamera
├── pSphere25                 MeshRenderer — assign to _lampRenderer (material tinted red→green)
├── pCube17                   ElectricLever — auto-found as _lever in Awake()
├── pCube18                   MeshRenderer — auto-found as _referenceRenderer (Light Layers source)
├── Safeguardanchor           Drop zone for the fuse (SphereCollider, Interactable Layer)
│                             Assign to _fuseAnchorCollider and _fuseAnchorTransform
├── SafeGuard (3)             Fuse mesh GameObject — assign to _fuseMesh (hidden until inserted)
├── vfx_Sparks_01             ParticleSystem — assign to _wrongPullParticles
├── SolvedLoopAudio           AudioSource — assign to _solvedLoopSource (3D settings configured in code at runtime)
├── Camera
│   └── EllectricCamera       CinemachineCamera — assign to _cinematicCamera (solved cinematic shot)
├── LightSupport
│   ├── Point Light           Flashlight support light (enabled during puzzle mode)
│   └── Point Light (1)
├── Contacts
│   ├── Terminal_Colored_0..5  ElectricTerminal (Type = Colored) — assign to _coloredTerminals
│   └── Terminal_Neutral_0..5  ElectricTerminal (Type = Neutral) — assign to _neutralTerminals
└── [Wire_0..5]               Created at runtime by ElectricPuzzleController
```

---

## Inspector Fields — ElectricPuzzleController

### References


| Field               | Description                                                                            |
| ------------------- | -------------------------------------------------------------------------------------- |
| `_panel`            | Root panel GameObject in Canvas — opened/closed via UIManager (optional)               |
| `_coloredTerminals` | `ElectricTerminal[6]` — colored terminals in order 0..5                                |
| `_neutralTerminals` | `ElectricTerminal[6]` — neutral terminals in order 0..5                                |
| `_puzzleData`       | `ElectricPuzzleData` ScriptableObject — solution mapping and wire colors               |
| `_solvedObject`     | GameObject activated when the puzzle is fully solved (lever pulled with correct wires) |


### Lamp


| Field                | Description                                                                                 |
| -------------------- | ------------------------------------------------------------------------------------------- |
| `_lampRenderer`      | `Renderer` on the lamp mesh (`pSphere25`) — material tinted red→green based on puzzle state |
| `_lampRedColor`      | Base color of the lamp material in the unsolved state (default: dark red)                   |
| `_lampRedEmission`   | Emission color in the unsolved state (HDR, default: 3.59 red)                               |
| `_lampGreenColor`    | Base color when the puzzle is solved (default: dark green)                                  |
| `_lampGreenEmission` | Emission color when solved (HDR, default: 3.59 green)                                       |


### Lever


| Field                 | Description                                                                                   |
| --------------------- | --------------------------------------------------------------------------------------------- |
| `_lever`              | `ElectricLever` on `pCube17` — auto-found in `Awake()` if not assigned                        |
| `_wrongPullParticles` | `ParticleSystem` on `vfx_Sparks_01` — plays on incorrect lever pulls (requires fuse inserted) |


### Settings


| Field                | Description                                                                                   |
| -------------------- | --------------------------------------------------------------------------------------------- |
| `_terminalLayer`     | Layer mask for terminal colliders (Interactable Layer) — used for mouse raycasting            |
| `_wireCapPrefab`     | Prefab for visual caps at both wire ends (e.g. `WireCap.prefab`)                              |
| `_wireMaterial`      | Material for wire LineRenderers (e.g. `Wire.mat`) — cloned per wire for color tinting         |
| `_referenceRenderer` | Renderer to copy Light Layers from; auto-found in children if not assigned                    |
| `_wireSettings`      | `ElectricWireSettings` — simulation and rendering parameters (segments, slack, gravity, etc.) |


### Sounds


| Field                    | Description                                                                                                                  |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| `_fuseInsertClip`        | Played when the fuse is inserted                                                                                             |
| `_wireConnectClip`       | Played when a wire is connected to a neutral terminal                                                                        |
| `_wireDisconnectClip`    | Played when a wire is disconnected (picked up from a terminal)                                                               |
| `_solvedClip`            | Played when the puzzle is fully solved                                                                                       |
| `_wrongPullClip`         | Played when the lever is pulled with incorrect connections                                                                   |
| `_solvedLoopSource`      | Looping 3D AudioSource — generator hum after solving                                                                         |
| `_solvedLoopMinDistance` | 3D distance within which the solved loop plays at full volume (default: 3)                                                   |
| `_solvedLoopMaxDistance` | 3D distance at which the solved loop fades to silence (default: 6)                                                           |
| Volume fields            | `_fuseInsertVolume`, `_wireConnectVolume`, `_wireDisconnectVolume`, `_solvedVolume`, `_wrongPullVolume`, `_solvedLoopVolume` |


### Solved Cinematic


| Field                 | Description                                                                                          |
| --------------------- | ---------------------------------------------------------------------------------------------------- |
| `_cinematicCamera`    | `CinemachineCamera` (EllectricCamera) for the dramatic solved shot — assigned on the scene object    |
| `_lampAnimator`       | Animator that plays the lamp turn-on animation during the cinematic (assign when animation is ready) |
| `_lampAnimTrigger`    | Trigger parameter name for the lamp animation (default: `"PlayLampAnimation"`)                       |
| `_lampAnimDuration`   | How long to hold the cinematic shot while the lamp animation plays (default: 3s)                     |
| `_cutFadeDuration`    | Duration of the quick fade when cutting to the cinematic camera (default: 0.3s)                      |
| `_solvedFadeDuration` | Duration of the fade when returning to the player camera (default: 1s)                               |


### Fuse / Inventory


| Field                    | Description                                                                                          |
| ------------------------ | ---------------------------------------------------------------------------------------------------- |
| `_acceptedItems`         | `ItemData[]` of items the puzzle accepts (assign `Safeguard.asset`)                                  |
| `_fuseAnchorCollider`    | `SphereCollider` on `Safeguardanchor` — raycast target for fuse drop                                 |
| `_fuseAnchorTransform`   | `Transform` of `Safeguardanchor` — position for the fuse visual                                      |
| `_fuseMesh`              | In-scene GameObject (`SafeGuard (3)`) — shown as ghost during drag, animated into place on insertion |
| `_fuseInsertStartOffset` | Local position offset from anchor where the insertion animation starts (default: `(0, 0.15, -0.12)`) |
| `_fuseInsertDuration`    | Duration of the fuse insertion animation in seconds (default: 0.5)                                   |
| `_fuseGhostAlpha`        | Alpha (0–1) of the fuse mesh when shown as a ghost preview during drag (default: 0.4)                |


**Required setup for `Safeguard.asset`:** fill in `itemName` and `icon`, and register the item in `InventorySystem._allItems` so save/load can resolve it by `ItemId`.

---

## Cheat Tool

Menu path: `Tools/PuzzlesCheats/Solve Electric Puzzle`

Script: `Assets/Scripts/Editor/PuzzleCheats/ElectricPuzzleUnlockTool.cs`

Play Mode only. Uses reflection to set the puzzle state as if the player had inserted the fuse, connected all wires correctly, and pulled the lever:

1. Reads the solution from `_puzzleData` and the fuse item ID from `_acceptedItems`.
2. Sets pending-load fields and calls `ApplyPendingLoad()` — recreates all 6 wires between correct terminals.
3. Calls `ElectricWire.JointPresettle()` — wires settle into natural hanging shape.
4. Shows the fuse mesh, disables the anchor collider, refreshes visuals (lamp green, lever pulled).
5. Starts the solved ambient loop and saves state.
6. Calls `PuzzleModeController.SetSolved()` — if the puzzle is open, exits puzzle mode; if paused, defers until unpause.