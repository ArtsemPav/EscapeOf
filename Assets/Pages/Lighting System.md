## Overview

The lighting system controls all runtime lights in the scene through named zones. Each lamp registers itself independently, which makes the system fully compatible with prefabs — no scene hierarchy restructuring required.

**Power chain:**

```
Generator restored (GeneratorPuzzleController)
  └─ LightingSystem.SetGeneratorReady(true)
       └─ Electric panel puzzle unlocked — fuse can be inserted

Electric panel puzzle solved (ElectricPuzzleController)
  └─ LightingSystem.ActivatePower()
       └─ SetPower(true) — general power ON for the first time
            ├─ LightZone — all room lights follow their switch states
            ├─ EmergencyLamp — red lamps turn OFF (power available)
            ├─ LightSwitch — room switches become interactive
            ├─ ElectricPanel — breaker becomes toggleable for scares
            └─ IPowerConsumer — any registered consumer reacts

ElectricPanel (after initial activation)
  └─ TogglePower() — scripted scares: power off → all dark, power on → restored
```

**Power model:**

- Power starts OFF — the electric panel puzzle must activate it first
- Generator must be restored before the electric panel puzzle can be solved
- After initial activation, `ElectricPanel` can toggle power on/off for scares
- When power is OFF → all lights disabled regardless of switch states, emergency lamps ON
- When power is ON → each zone reflects its own switch state independently, emergency lamps OFF
- Power state, generator readiness, and switch states are persisted via `SaveManager`

---

## Scripts

```
Assets/Scripts/Lighting/
├── LightGroup.cs        — LightZone component + FlickerMode enum
├── LightingSystem.cs    — singleton manager, ISaveable, power + generator state + IPowerConsumer registry
├── LightSwitch.cs       — per-room interactable switch
├── ElectricPanel.cs     — master breaker, IInteractable (locked until power is activated)
└── EmergencyLamp.cs     — red indicator lamp, IPowerConsumer

Assets/Scripts/Core/
└── IPowerConsumer.cs    — interface for devices that react to power state changes
```

---

## IPowerConsumer

**File:** `Assets/Scripts/Core/IPowerConsumer.cs`

Interface for any device that should react to general power state changes. Register with `LightingSystem.RegisterConsumer()` — the current state is delivered immediately upon registration, and again whenever master power changes.

```csharp
public interface IPowerConsumer
{
    void OnPowerStateChanged(bool isPowered);
}
```

To use: implement the interface in any `MonoBehaviour` and call `LightingSystem.Instance?.RegisterConsumer(this)` in `Start()` (not `Awake()` — ensures other components' `Awake()` runs first), `UnregisterConsumer(this)` in `OnDestroy()`.

**Current implementations:**


| Script                 | File                                        | How it reacts                                                                                                                                              |
| ---------------------- | ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EmergencyLamp`        | `Lighting/EmergencyLamp.cs`                 | Red light + emission ON when power off, OFF when power on                                                                                                  |
| `LoopPuzzleController` | `Puzzle/LoopPuzzle/LoopPuzzleController.cs` | Disables all puzzle components (buttons, TV, spotlights) when power off. See [@ id="/Pages/Private/Loop Puzzle System.md" label="Loop Puzzle System"] |
| `ElevatorController`   | `Interaction/ElevatorController.cs`         | Blocks `MoveToFloor` and silences ambient music when power off. All 6 `ElevatorButton` components show "Нет электричества" and become non-interactive.     |
| `LaptopOS`             | `Puzzle/LaptopScripts/LaptopOS.cs`          | Laptop becomes non-interactive when power off                                                                                                              |
| `DecalSlideshow`       | `Decal/DecalSlideshow.cs`                   | Decal slideshow stops when power off                                                                                                                       |
| `ReelSpinner`          | `Puzzle/Projector/ReelSpinner.cs`           | Film reel stops spinning when power off; resumes (if allowed via `SetSpinning`) when power on                                                              |
| `DuckSoundZone`        | `Interaction/DuckSoundZone.cs`              | Duck lure sound fades out and stops entirely when power off                                                                                                |


---

## LightingSystem

**File:** `LightingSystem.cs`
Singleton. Add to one empty GameObject in the scene. Implements `ISaveable` — registers with `SaveManager` automatically.


| Field           | Description                                       |
| --------------- | ------------------------------------------------- |
| `Fade Duration` | Seconds for lights to fade in/out. `0` = instant. |


### Public API

```csharp
// Master power (щиток)
LightingSystem.Instance.SetPower(bool on);
LightingSystem.Instance.TogglePower();
bool powered = LightingSystem.Instance.IsPowered;

// Generator readiness
LightingSystem.Instance.SetGeneratorReady(true);
bool generatorReady = LightingSystem.Instance.IsGeneratorReady;

// Power activation (called by ElectricPuzzleController on solve)
LightingSystem.Instance.ActivatePower();
bool activated = LightingSystem.Instance.IsPowerActivated;

// Per-zone switch control
LightingSystem.Instance.SetZoneSwitch("Room1", true);
bool newState = LightingSystem.Instance.ToggleZoneSwitch("Corridor");
bool isOn = LightingSystem.Instance.GetZoneSwitchState("Storage");
bool isLit = LightingSystem.Instance.IsZoneLit("Room1"); // power AND switch both on

// Initial switch state for a new game (no save). Called by LightSwitch.Start();
// ignored if the zone already has a state (save was loaded).
LightingSystem.Instance.InitializeZoneSwitch("Room1", true);

// Performance suppression (used by RoomVisibilityManager, not persisted to saves)
LightingSystem.Instance.SetZoneRenderSuppressed("Corridor", true);
bool suppressed = LightingSystem.Instance.IsZoneRenderSuppressed("Corridor");

// Power consumer registration
LightingSystem.Instance.RegisterConsumer(myConsumer);
LightingSystem.Instance.UnregisterConsumer(myConsumer);

// Events
LightingSystem.Instance.OnPowerChanged         += (isPowered) => { };
LightingSystem.Instance.OnZoneSwitchChanged    += (zoneId, isSwitchedOn) => { };
LightingSystem.Instance.OnGeneratorReadyChanged += (isReady) => { };
```

### Save data

`LightingSystem` serialises master power state, generator readiness, power activation flag, and every zone's switch state into JSON via `ISaveable`. No manual setup required.

Backwards compatibility: old saves without `isGeneratorReady`/`isPowerActivated` are auto-migrated — if power was on, both flags are set to `true`.

---

## LightZone

**File:** `LightGroup.cs`
**Component:** add directly on the `Light` GameObject, including inside prefabs.


| Field                         | Description                                                     |
| ----------------------------- | --------------------------------------------------------------- |
| `Zone Id`                     | Zone name, e.g. `"Corridor"`, `"Room1"`. Case-sensitive.        |
| `Flicker Mode`                | `None` / `Constant` / `Occasional`                              |
| `Flicker Min Multiplier`      | How dark flicker dips get (0 = blackout, 0.5 = half brightness) |
| `Flicker Frequency`           | Times per second the intensity is randomised (2–50 Hz)          |
| `Occasional Min/Max Interval` | Pause between flicker bursts (seconds)                          |
| `Occasional Min/Max Duration` | Length of each burst (seconds)                                  |


Multiple lamps sharing the same `Zone Id` are controlled together. Each lamp has its own flicker settings, so lamps within one zone can flicker independently.

**Flicker modes:**

- `None` — steady light, no variation
- `Constant` — flickers on every tick while the light is on
- `Occasional` — long stable phase → short burst of rapid flicker → restore → repeat

Flicker stops automatically when the light is turned off (switch or panel) and resumes after it is turned back on. It does not conflict with the fade transition.

---

## EmergencyLamp

**File:** `EmergencyLamp.cs`
Implements `IPowerConsumer`. Lamp that signals power status. By default works in emergency mode (ON when power is OFF), but can also act as a normal light (`_activeWhenPowered = true`).


| Power state                | Emergency mode (`_activeWhenPowered = false`) | Normal mode (`_activeWhenPowered = true`) |
| -------------------------- | --------------------------------------------- | ----------------------------------------- |
| OFF (no electricity)       | Red light + glass emission ON — red glow      | OFF                                       |
| ON (electricity available) | OFF — emission disabled                       | Red light + glass emission ON             |


All emergency lamps (`_activeWhenPowered = false`) share a single material (`lightRed.mat`). Emission is toggled directly on the shared material via `_EMISSION` keyword and `_EmissionColor` — every lamp reacts simultaneously. Lamps with `_activeWhenPowered = true` get an instantiated material to avoid conflicting with emergency lamps.

The lamp doubles as a **door lock indicator**: `SetLocked()` (red) / `SetUnlocked()` (green) change both the light color and the glass emission color. Wire them to `DoorInteraction.OnLocked` / `OnUnlocked`.

**Component placement:** on the lamp root GameObject. Auto-finds `Point Light` and `LampGlass` renderer in children.


| Field                 | Description                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------------ |
| `Active When Powered` | `false` = emergency mode (ON when power OFF, default). `true` = normal light (ON when power ON). |
| `Emergency Light`     | Red `Light` component. Auto-found in children.                                                   |
| `Lamp Glass Renderer` | `Renderer` on LampGlass mesh. Auto-found in children (`LampGlass`).                              |
| `Locked Color`        | Light color when the monitored door is locked. Default: red.                                     |
| `Unlocked Color`      | Light color when the monitored door is unlocked. Default: green.                                 |


**Scene setup:** `EmergencyLamp` is placed on all 7 room lamps:

```
/Env/2ndFloor/Room 1/lamp          ← EmergencyLamp
/Env/2ndFloor/Room 2/Props/lamp    ← EmergencyLamp
/Env/2ndFloor/Room 3/Props/lamp    ← EmergencyLamp
/Env/2ndFloor/Room 4/lamp          ← EmergencyLamp
/Env/2ndFloor/Room 5/Props/lamp    ← EmergencyLamp
/Env/2ndFloor/Room 6/lamp          ← EmergencyLamp
/Env/2ndFloor/Room 7/lamp          ← EmergencyLamp
```

Each lamp hierarchy:

```
lamp (root — EmergencyLamp)
├── Lamp          (MeshRenderer, LampGuard.mat)
├── LampBase      (MeshRenderer, LampGuard.mat)
├── LampGlass     (MeshRenderer, lightRed.mat)
└── Point Light   (UnityEngine.Light — red Spot)
```

---

## ElevatorController (power integration)

**File:** `Interaction/ElevatorController.cs`
Implements `IPowerConsumer` (in addition to `ISaveable`). The elevator cannot move and its ambient music stays silent until master power is on.


| Power state | Elevator behaviour                                  |
| ----------- | --------------------------------------------------- |
| OFF         | `MoveToFloor` blocked; ambient loop volume = 0      |
| ON          | `MoveToFloor` works normally; ambient loop restored |


The `HasPower` property reflects the current master power state. When power is off, the buttons remain **interactive** (`CanInteract()` always true, crosshair Hand) — pressing plays the interaction, but `MoveToFloor` does nothing because it checks `HasPower` internally. The prompt shows `_noPowerHint` («Нет электричества») via `GetInteractText()`.

**Inspector field:**


| Field          | Description                                                                    |
| -------------- | ------------------------------------------------------------------------------ |
| `_noPowerHint` | Text shown by elevator buttons when power is off. Default: "Нет электричества" |


**Scene objects:**

```
/Env/Lift/Elevator                                      ← ElevatorController (IPowerConsumer)
├── Button1                                             ← ElevatorButton (floor 2, inside cab)
├── Button1 (1)                                         ← ElevatorButton (floor 1, inside cab)
├── Button1 (2)                                         ← ElevatorButton (floor 0, inside cab)
/Env/2ndFloor/Corridor2stF/props/elevator_switch (1)    ← ElevatorButton (floor 2, call from 2nd floor)
/Env/1stFloor/Corridor1st/props/elevator_switch (1)     ← ElevatorButton (floor 1, call from 1st floor)
/Env/Basement/CorridorBasement/Props/elevator_switch (1) ← ElevatorButton (floor 0, call from basement)
```

---

## LightSwitch

**File:** `LightSwitch.cs`
Implements `IInteractable`. Add to any GameObject with a Collider.


| Field                    | Description                                                                                                                                                  |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Zone Id`                | Zone this switch controls. Must match `LightZone.Zone Id`.                                                                                                   |
| `Default Switch State`   | Switch state for a new game (no save). Pushed to `LightingSystem` via `InitializeZoneSwitch()` in `Start()`. Ignored when save data exists. Default: `true`. |
| `Interact Hint`          | Text shown in the interaction prompt when power is on.                                                                                                       |
| `No Power Hint`          | Text shown when master power is off. Default: «Нет питания».                                                                                                 |
| `Switch Handle`          | Optional Transform that rotates when toggled (lever/button mesh).                                                                                            |
| `Handle Rotation On/Off` | Local Euler angles for each handle state.                                                                                                                    |
| `Switch On/Off Clip`     | Audio clips played on toggle.                                                                                                                                |


The switch is **never blocked** — `CanInteract()` is always `true`. Without master power the switch still flips physically (animation + click sound) and its state is still tracked: lights will turn on when power is restored if the switch is in the ON position. The prompt shows `No Power Hint` when power is off, and `Выключатель [ВКЛ]` / `Выключатель [ВЫКЛ]` when power is on.

---

## ElectricPanel

**File:** `ElectricPanel.cs`
Implements `IInteractable`. Controls master power for all zones simultaneously.

**Locked until power is activated:** the breaker can only be toggled after the electric panel puzzle has activated power at least once (`IsPowerActivated`) AND the generator is still running (`IsGeneratorReady`). Before that, interaction is blocked and the hint shows "Щиток [Нет питания]".


| Field                       | Description                                                                      |
| --------------------------- | -------------------------------------------------------------------------------- |
| `Hint Powered On/Off`       | Interaction prompt text for each power state.                                    |
| `Hint Not Activated`        | Text shown before the electric panel puzzle has been solved.                     |
| `Power Indicator`           | Optional `Renderer` (e.g. small LED mesh) that changes material on power toggle. |
| `Indicator On/Off Material` | Materials assigned to the indicator per state.                                   |
| `Power On/Off Clip`         | Audio clips played when toggling power.                                          |


Saving is handled entirely by `LightingSystem` — `ElectricPanel` only drives the interaction and visuals.

---

## Scene setup

### 1 — LightingSystem GameObject

Create an empty GameObject, add `LightingSystem`. One per scene.

### 2 — Label lamps

Open a prefab. On each `Light` GameObject, add `LightZone` and set `Zone Id`:

```
lamp_B (1)  →  LightZone  ZoneId="Corridor"   FlickerMode=Occasional
lamp_B (2)  →  LightZone  ZoneId="Corridor"   FlickerMode=None
lamp_ceil   →  LightZone  ZoneId="Room1"      FlickerMode=Constant
```

### 3 — Add a switch per room

Any interactable GameObject in the room:

```
LightSwitch component
  Zone Id = "Room1"
  Switch Handle → (lamp lever Transform)
```

### 4 — Add the electric panel

```
ElectricPanel component
  Power Indicator → (LED Renderer)
  Indicator On Material  → mat_led_green
  Indicator Off Material → mat_led_off
```

### 5 — Emergency lamps

`EmergencyLamp` is already placed on all 7 room lamps. No manual setup required — the component auto-finds `Point Light` and `LampGlass` in children.

---

## ShadowAtlas — recommended setup

URP shadow atlas fills up quickly with many spotlights. Per zone, designate **1–2 key lamps** to cast shadows (`Shadow Type = Soft Shadows`). Set all remaining lamps to `Shadow Type = No Shadows`.

When a zone is disabled (`light.enabled = false`), those lights exit the shadow atlas entirely, reducing GPU load automatically whenever rooms are unlit.

---

## Scripted events example

```csharp
// Power failure scare sequence
LightingSystem.Instance.SetPower(false);
await Task.Delay(3000);
LightingSystem.Instance.SetPower(true);

// Only the storage room lights come back on
LightingSystem.Instance.SetZoneSwitch("Corridor", false);
LightingSystem.Instance.SetZoneSwitch("Storage", true);

// React to zone changes for atmosphere
LightingSystem.Instance.OnZoneSwitchChanged += (id, isOn) =>
{
    if (id == "Corridor" && !isOn)
        AudioManager.Instance?.PlaySFX(electricCutClip);
};
```