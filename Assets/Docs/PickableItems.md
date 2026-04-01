# Pickable Items

A pickable item is a world object the player can pick up and place into the inventory.
Optionally shows a 3D inspection panel before picking up.

---

## Setup

### Step 1 — Create ItemData

`ItemData` is a ScriptableObject holding all item data.

Right-click in Project → **Create → Game → Item Data**

| Field | Description |
|---|---|
| **Item Name** | Shown in inventory and inspection panel |
| **Description** | Text the player reads during inspection |
| **Icon** | Inventory slot sprite |
| **Inspection Prefab** | 3D model shown in inspection view. If empty — item is picked up immediately |
| **Is Unique** | If enabled — a second copy cannot be picked up |

### Step 2 — Set Up the World Object

1. Add a 3D model to the scene
2. Add component `PickableItem`
3. Assign `ItemData`
4. Set layer to **Interactable Layer**
5. Ensure a **Collider** is present

---

## Items Inside Drawers or Containers

If a pickable item is a child of a `DrawerDrag` object, layer hierarchy must be:

```
cupboard_drawer   ← DrawerDrag,  Interactable Layer
└── FlashLight    ← PickableItem, Interactable Layer
```

The **container body** (desk, shelf, cabinet mesh) must be on **Default**, not Interactable Layer.

- Ray 1 (`Interactable Layer`) passes through the body and finds the item directly
- Ray 2 (`Default` + others) sees the body as an obstacle when the drawer is closed → blocks interaction

> `FPSController` searches for `IDraggable` only on the directly hit object.
> `DrawerDrag` on the parent drawer will NOT intercept interaction with child items.

---

## Interaction Flow

```
Player presses E → PickableItem.Interact()
  └─ inspectionPrefab set → ItemInspector shows 3D panel
       ├─ Player rotates item (LMB + drag)
       └─ E / Escape → item added to inventory, world object destroyed
  └─ no inspectionPrefab → item added to inventory immediately

Item in inventory → LMB on slot
  └─ InventoryItemPreview shows 3D preview in the right panel
       ├─ Player rotates item (LMB + drag over preview area)
       └─ Preview cleared when inventory is closed
```

While the inspection panel is open, `UIManager` locks player movement and shows the cursor.

---

## Hint Text

`"Взять [Name]"` is assembled automatically from `GameConfig.pickUpPrefix` and `ItemData.itemName`.
To change the word "Взять" globally — open `GameConfig` and update **Pick Up Prefix**.

---

## Inspection Prefab Setup

1. Create a Prefab from the item's 3D model
2. Assign it to `Inspection Prefab` in `ItemData`
3. Ensure a layer named **Inspection** exists in the project — it is used to render the preview
