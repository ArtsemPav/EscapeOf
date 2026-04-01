# Interaction System — Raycast Logic

## Overview

`FPSController.HandleInteractionDetection()` uses two sequential raycasts every frame.

```
Update()
 ├── HandleInteractionDetection()   // raycast → _currentInteractable
 └── HandleDragInteraction()
       ├── LMB pressed  → OnDragStart()
       ├── LMB held     → OnDrag(mouse.delta)
       └── LMB released → OnDragEnd()
```

---

## Ray 1 — Find Interactable

```csharp
Physics.Raycast(ray, out RaycastHit interactHit, interactDistance,
                interactableLayer, QueryTriggerInteraction.Ignore)
```

Targets **Interactable Layer** only. If nothing is hit — hint is cleared.

---

## Ray 2 — Obstacle Check

```csharp
int obstacleMask = ~interactableLayer.value & ~(1 << 2); // all except Interactable Layer + IgnoreRaycast
Physics.Raycast(ray, out RaycastHit _, interactHit.distance, obstacleMask, ...)
```

Checks for solid geometry between the camera and the found object.
If anything is in the way — interaction is blocked.

> This prevents picking up items through walls, closed drawers, or furniture bodies.

---

## Component Resolution Order

After both rays pass, the script resolves the script on the hit object in this order:

| Priority | Method | Purpose |
|---|---|---|
| 1 | `GetComponent<IDraggable>()` on hit object | Drawers, doors — collider is directly on them |
| 2 | `TryGetComponent<IInteractable>()` on hit object | PickableItem, code locks, etc. |
| 3 | `GetComponentInParent<IInteractable>()` | Levers / gauges with a child collider |

**CRITICAL:** `IDraggable` is searched **only on the directly hit object**, never via `GetComponentInParent`.
If parent-climbing were used, `DrawerDrag` on a drawer would wrongly intercept interaction
with items stored inside that drawer (e.g. `FlashLight` inside `cupboard_drawer`).

---

## Layer Rules

| Object | Layer | Reason |
|---|---|---|
| Drawers, doors, buttons, levers, pickable items | **Interactable Layer** | Ray 1 must see them |
| Furniture bodies, walls, shelves | **Default** | Ray 2 must see them as obstacles |
| Trigger zones | **Ignore Raycast** | Both rays skip them |

### WARNING — never put a furniture body on Interactable Layer

Ray 2 ignores `Interactable Layer` entirely. If the desk/shelf body is on that layer,
Ray 2 won't block interaction through its closed panels and the player will be able
to pick up items through solid geometry.

---

## IDraggable Interface

```csharp
void OnDragStart(Vector3 hitPoint); // LMB pressed — world hit point passed in
void OnDrag(Vector2 mouseDelta);    // every frame while LMB is held
void OnDragEnd();                   // LMB released
```

`hitPoint` is used to compute the correct open direction relative to where the player grabbed.

## IInteractable Interface

```csharp
string        GetInteractText();    // hint text shown when aiming
bool          IsPickable();         // whether the object can be picked up
CrosshairMode GetCrosshairMode();   // crosshair icon
```

---

## Implementations

| Script | Interface | Object |
|---|---|---|
| `DrawerDrag` | `IDraggable`, `IInteractable` | `cupboard_drawer` |
| `DoorInteraction` | `IDraggable`, `IInteractable` | `desk_door`, `locker` doors |
| `PickableItem` | `IInteractable` | All pickable world items |
| `PressureLever` | `IInteractable` | Levers (collider on child) |
