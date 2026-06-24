## Overview

Performance culling that toggles room **geometry** and **light zones** based on the player's
physical location. As the player walks through `RoomTrigger` volumes, only the rooms those
triggers list stay rendered; everything else is suppressed. This is a manual whitelist system
driven by triggers — **not** Unity Occlusion Culling.

**Key principles:**

- A room stays visible only while the player occupies a trigger that lists it.
- The active set is the **UNION** of every trigger the player currently overlaps, so half-stepping
  between rooms never culls geometry that is still in view (no "holes in walls" / skybox voids).
- Geometry is toggled via `Renderer.enabled` (GameObjects stay active — colliders, logic, audio keep running).
- Lights are handled centrally by `LightingSystem` because a `ZoneId` can be shared across rooms.

> CRITICAL: Do **NOT** bake Unity Occlusion Culling for this scene. Baked occlusion overrides
> `Renderer.enabled` and culls geometry visible through doorways, regardless of this system. The
> scene's `OcclusionCullingData` was removed for this reason.

---

## Scripts

```
Assets/Scripts/Room/
├── RoomVisibilityManager.cs  — singleton, owns the active-room set and applies culling
├── RoomController.cs         — per-room: collects renderers/zones, configures its triggers
└── RoomTrigger.cs            — trigger volume; reports player enter/exit to the manager
```

Related: `LightingSystem.SetZoneRenderSuppressed(zoneId, suppressed)` is what actually toggles lights per zone.

---

## RoomVisibilityManager

**File:** `RoomVisibilityManager.cs` — singleton. One per scene (e.g. `/Env/RoomVisibilityManager`).

| Field            | Description                                                          |
| ---------------- | ------------------------------------------------------------------- |
| `Starting Rooms` | Rooms rendered at game start, before the player enters any trigger. |
| `Debug Logging`  | Logs occupied triggers + visible rooms on each change. Off by default. |

**How it works:**

- `Awake` caches all `RoomController`s (`FindObjectsByType`) and all managed `ZoneId`s.
- Maintains `_occupiedTriggers` — a dictionary of every `RoomTrigger` the player currently overlaps,
  mapped to the rooms it keeps visible.
- `EnterTrigger` / `ExitTrigger` update that dictionary, then `RecomputeActiveRooms` rebuilds the
  active set as the **union** of all occupied triggers' lists.
- `ApplyActiveRooms`:
  - Geometry: `room.SetGeometryActive(activeRooms.Contains(room))` for every room.
  - Lights: a zone is lit if **any** active room owns it; otherwise `LightingSystem` suppresses it.
- If the player leaves all triggers (gap between volumes), the **last** active set is kept to avoid flicker.

---

## RoomController

**File:** `RoomController.cs` — one per room GameObject.

| Field           | Description                                                                    |
| --------------- | ----------------------------------------------------------------------------- |
| `Local Volume`  | Optional per-room post-processing `Volume`.                                    |
| `Trigger Zones` | Array of `{ Trigger, VisibleRooms }`. Each entry wires one `RoomTrigger` to the rooms it should keep rendered. |

**Responsibilities (on `Awake`):**

- Collects interactable colliders (objects implementing `IInteractable`) — for `Lock`/`Unlock`.
- Collects distinct `ZoneId`s from child `LightZone`s → exposed via `ZoneIds`.
- Collects renderers that are **enabled at startup** (authored-disabled renderers are left alone) → `_managedRenderers`.
- `ConfigureTriggers()` injects each trigger with its room list. Entries referencing the **same trigger
  within this room** are merged.

`SetGeometryActive(bool)` toggles only the managed renderers. `Lock()` / `Unlock()` toggle interactable colliders.

### TriggerZone — multiple zones per room

A single room can own several triggers, each lighting a different group of rooms. Used for long
corridors split into segments. Example: `Corridor1st` has 3 triggers, each listing different far rooms.

---

## RoomTrigger

**File:** `RoomTrigger.cs` — requires a `Collider` with `isTrigger = true`.

| Field  | Description                                                                 |
| ------ | --------------------------------------------------------------------------- |
| `Room` | Fallback room used if no list is configured. Auto-filled from parent on `Reset`. |

- Its room list is supplied by the **owning `RoomController`** via `Configure(owner, visibleRooms)`.
  The owner room is always auto-included, so it never needs to be in the list manually.
- `Configure` is **additive (merge)**: if several `RoomController`s reference the same trigger, their
  lists combine instead of the last caller overwriting the others. This prevents a room's list from
  being silently wiped by a stray reference.
- `OnTriggerEnter` / `OnTriggerExit` / `OnDisable` report to the manager. Player is detected via
  `FPSController` in the colliding object's parent.

---

## Authoring rules & gotchas

1. **A trigger should belong to exactly one room.** Each `RoomController._triggerZones` entry must point
   at *its own* `VisibilityTrigger`, never another room's. A stray cross-room reference used to wipe the
   owner's list (the additive `Configure` now neutralizes the symptom, but fix the reference anyway).
   To audit: build a `triggerPath → owners[]` map from all `RoomController._triggerZones`; any trigger
   with more than one owner is a misconfiguration.

2. **A room's list must include everything visible through its doorways**, not just itself and neighbors.
   From inside a room you can see down a corridor; if those far rooms aren't listed, they pop to skybox
   when you fully enter. Match a room's list to what its adjacent corridor segment shows.

3. **Trigger volumes should cover the whole room interior**, including the doorway threshold, so the
   player never stands in a spot covered by no trigger (or only a neighbor's trigger).

4. **Never bake Occlusion Culling** for this scene (see Overview).

5. Light zones are shared (e.g. stairs lights use `ZoneId="Corridor"`). A zone lights up if any active
   room owns it — see the Lighting System page.

---

## Debugging

- Enable `Debug Logging` on `RoomVisibilityManager` to log occupied triggers + visible rooms per change.
- A room "ignored" despite being listed is almost always one of:
  - its trigger is also referenced by another room (overwrite — now merged),
  - the room isn't actually in the *occupied* trigger's list,
  - baked occlusion culling is hiding it,
  - `_managedRenderers` is empty (renderers authored-disabled at `Awake`).
