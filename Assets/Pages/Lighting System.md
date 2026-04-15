## Overview

The lighting system controls all runtime lights in the scene through named zones. Each lamp registers itself independently, which makes the system fully compatible with prefabs — no scene hierarchy restructuring required.

**Power model:**
- Master power (щиток) OFF → all lights disabled regardless of switch states
- Master power ON → each zone reflects its own switch state independently
- Switch states and master power are persisted via `SaveManager`

---

## Scripts

```
Assets/Scripts/Lighting/
├── LightGroup.cs        — LightZone component + FlickerMode enum
├── LightingSystem.cs    — singleton manager, ISaveable
├── LightSwitch.cs       — per-room interactable switch
└── ElectricPanel.cs     — master breaker, IInteractable
```

---

## LightZone

**File:** `LightGroup.cs`  
**Component:** add directly on the `Light` GameObject, including inside prefabs.

| Field | Description |
|---|---|
| `Zone Id` | Zone name, e.g. `"Corridor"`, `"Room1"`. Case-sensitive. |
| `Flicker Mode` | `None` / `Constant` / `Occasional` |
| `Flicker Min Multiplier` | How dark flicker dips get (0 = blackout, 0.5 = half brightness) |
| `Flicker Frequency` | Times per second the intensity is randomised (2–50 Hz) |
| `Occasional Min/Max Interval` | Pause between flicker bursts (seconds) |
| `Occasional Min/Max Duration` | Length of each burst (seconds) |

Multiple lamps sharing the same `Zone Id` are controlled together. Each lamp has its own flicker settings, so lamps within one zone can flicker independently.

**Flicker modes:**

- `None` — steady light, no variation
- `Constant` — flickers on every tick while the light is on
- `Occasional` — long stable phase → short burst of rapid flicker → restore → repeat

Flicker stops automatically when the light is turned off (switch or panel) and resumes after it is turned back on. It does not conflict with the fade transition.

---

## LightingSystem

**File:** `LightingSystem.cs`  
Singleton. Add to one empty GameObject in the scene. Implements `ISaveable` — registers with `SaveManager` automatically.

| Field | Description |
|---|---|
| `Fade Duration` | Seconds for lights to fade in/out. `0` = instant. |

### Public API

```csharp
// Master power (щиток)
LightingSystem.Instance.SetPower(bool on);
LightingSystem.Instance.TogglePower();
bool powered = LightingSystem.Instance.IsPowered;

// Per-zone switch control
LightingSystem.Instance.SetZoneSwitch("Room1", true);
bool newState = LightingSystem.Instance.ToggleZoneSwitch("Corridor");
bool isOn = LightingSystem.Instance.GetZoneSwitchState("Storage");
bool isLit = LightingSystem.Instance.IsZoneLit("Room1"); // power AND switch both on

// Events
LightingSystem.Instance.OnPowerChanged      += (isPowered) => { };
LightingSystem.Instance.OnZoneSwitchChanged += (zoneId, isSwitchedOn) => { };
```

### Save data

`LightingSystem` serialises master power state and every zone's switch state into JSON via `ISaveable`. No manual setup required.

---

## LightSwitch

**File:** `LightSwitch.cs`  
Implements `IInteractable`. Add to any GameObject with a Collider.

| Field | Description |
|---|---|
| `Zone Id` | Zone this switch controls. Must match `LightZone.Zone Id`. |
| `Interact Hint` | Text shown in the interaction prompt when power is on. |
| `No Power Hint` | Text shown when master power is off. |
| `Switch Handle` | Optional Transform that rotates when toggled (lever/button mesh). |
| `Handle Rotation On/Off` | Local Euler angles for each handle state. |
| `Switch On/Off Clip` | Audio clips played on toggle. |

When master power is off, `Interact()` returns early — the switch is blocked.  
The interaction prompt automatically shows current state: `Выключатель [ВКЛ]` / `Выключатель [ВЫКЛ]`.

---

## ElectricPanel

**File:** `ElectricPanel.cs`  
Implements `IInteractable`. Controls master power for all zones simultaneously.

| Field | Description |
|---|---|
| `Hint Powered On/Off` | Interaction prompt text for each power state. |
| `Power Indicator` | Optional `Renderer` (e.g. small LED mesh) that changes material on power toggle. |
| `Indicator On/Off Material` | Materials assigned to the indicator per state. |
| `Power On/Off Clip` | Audio clips played when toggling power. |

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
  Zone Id      = "Room1"
  Switch Handle → (lamp lever Transform)
```

### 4 — Add the electric panel

```
ElectricPanel component
  Power Indicator        → (LED Renderer)
  Indicator On Material  → mat_led_green
  Indicator Off Material → mat_led_off
```

---

## Shadow atlas — recommended setup

URP shadow atlas fills up quickly with many spotlights. Per zone, designate **1–2 key lamps** to cast shadows (`Shadow Type = Soft Shadows`). Set all remaining lamps to `Shadow Type = No Shadows`.

When a zone is disabled (`light.enabled = false`), those lights exit the shadow atlas entirely, reducing GPU load automatically whenever rooms are unlit.

---

## Scripted events example

```csharp
// Power failure sequence
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
