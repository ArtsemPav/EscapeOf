## Overview

A pressure-balancing puzzle where the player toggles levers to move the dial arrow from 0° (start) to exactly 180° (target). Each lever has a hidden positive pressure magnitude — the player doesn't know which lever has which value and must experiment to find the right combination. If the arrow overshoots into the red zone at 300°, a reset fires: all levers snap OFF and the arrow sweeps back down through 180° to 0° — the long descending path, not the short wrap-around. This means the arrow passes through the target on its way home, creating dramatic tension. Levers lock during the reset cooldown. Each session the winning combination is chosen randomly and the pressure-to-angle scale shifts to match it — the puzzle is **always solvable by construction**. Solved state is saved and restored via the [@ id="/Pages/Private/Save System.md" label="Save System"].

---

## Components

### `PressureLever`

Placed on each lever GameObject. Handles visual rotation and sound. **Does not expose pressure values in the Inspector** — values are assigned at runtime by `PressurePuzzle.GenerateAndAssignLeverValues()`.


| Field            | Description                                                                                                     |
| ---------------- | --------------------------------------------------------------------------------------------------------------- |
| `_angleOnDelta`  | Z-axis rotation delta applied when the lever is toggled ON. OFF stays at the original editor placement rotation |
| `_rotationSpeed` | Lerp speed of the lever rotation animation                                                                      |
| `_switchClip`    | AudioClip played on toggle                                                                                      |
| `_switchVolume`  | Volume of the switch clip                                                                                       |
| `_textWhenOff`   | Interaction hint when the lever is OFF                                                                          |
| `_textWhenOn`    | Interaction hint when the lever is ON                                                                           |


**Public API**


| Method                   | Description                                                                                                                                                                                  |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SetStateQuiet(bool on)` | Sets state instantly without animation or events. Used by `PressurePuzzle` during initialization and reset                                                                                   |
| `SnapVisual()`           | Forces the visual transform to match the current `IsOn`. Called at the end of `PressurePuzzle.Start()` after randomization — guarantees correct visuals regardless of script execution order |
| `CanInteract()`          | Returns `false` when the puzzle is solved **or** during a pressure reset. `FPSController` skips the object entirely — no hint, no crosshair change                                           |


### `PressurePuzzle`

Placed on the root puzzle GameObject. Generates lever values, picks a random solution, drives the inertial pressure simulation, manages the danger zone and resets, and detects the solved state.

**References**


| Field     | Description                                              |
| --------- | -------------------------------------------------------- |
| `_arrow`  | Transform of the needle inside the dial (`screen/arrow`) |
| `_saveId` | Unique save ID — never change after first save           |


**Dial Settings**


| Field                  | Description                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------- |
| `_arrowBaseAngle`      | Base X rotation added to all arrow positions. Set so 0° = start on the dial face   |
| `_pressureSmoothSpeed` | How fast current pressure chases the target sum from levers. Lower = more inertial |
| `_solveAngleTolerance` | Degrees from 180° that count as solved. Keep small (1–10°)                         |


**Target & Danger**


| Field              | Description                                                                                 |
| ------------------ | ------------------------------------------------------------------------------------------- |
| `_solveAngle`      | Target angle the player must reach (default: 180°)                                          |
| `_dangerAngle`     | Red zone threshold — reaching this triggers a reset (default: 300°)                         |
| `_warningFraction` | Steam starts ramping up at this fraction of the danger angle (0–1, e.g. 0.8 = 240° at 300°) |


**Solution**


| Field                    | Description                                                                                                                                     |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `_minLeversOnInSolution` | Minimum levers ON in the randomly chosen solution. Same minimum applied to OFF levers. Requires at least `2 × value` total levers               |
| `_minFlipsFromSolution`  | Minimum lever flips to reach **any** valid solution from the all-OFF start. Enforced against all winning combinations, not only the primary one |
| `_minSolutionFraction`   | Minimum solution total as a fraction of max total. Prevents max angle from exceeding 360° (default: 0.5)                                        |
| `_maxSolutionFraction`   | Maximum solution total as a fraction of max total. Ensures danger zone is reachable (default: 0.6)                                              |


**Lever Value Generation**


| Field             | Description                                                                                                                     |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `_leverValueBase` | Magnitude of the smallest lever. Each lever gets `offValue = 0`, `onValue = +magnitude`                                         |
| `_leverValueStep` | Spacing between consecutive magnitudes. With 6 levers, base=5 step=5 → magnitudes: 5, 10, 15, 20, 25, 30 (shuffled per session) |


**Reset**


| Field                 | Description                                                     |
| --------------------- | --------------------------------------------------------------- |
| `_resetCooldown`      | Minimum seconds levers stay blocked after a reset               |
| `_resetPressureSpeed` | How slowly the arrow returns to 0° during reset. Lower = slower |
| `_resetSound`         | AudioClip played when a reset is triggered                      |
| `_resetSoundVolume`   | Volume of the reset sound                                       |


**Steam VFX**


| Field                 | Description                                                           |
| --------------------- | --------------------------------------------------------------------- |
| `_steamEmitters`      | Particle systems that ramp up as pressure approaches the danger angle |
| `_maxSteamEmission`   | Maximum emission rate when pressure is at the danger angle (e.g. 50)  |
| `_resetSteamEmission` | Emission rate forced during a reset (e.g. 200)                        |
| `_resetBurstCount`    | Instant particle burst on each emitter when a reset triggers          |


**Steam Audio**


| Field                 | Description                                                         |
| --------------------- | ------------------------------------------------------------------- |
| `_steamLoopClip`      | Looping ambient steam sound — volume scales with emission intensity |
| `_steamLoopMaxVolume` | Maximum volume of the steam loop at full emission                   |
| `_steamFadeClip`      | One-shot sound played when steam dissipates after a reset ends      |
| `_steamFadeVolume`    | Volume of the fade sound                                            |


**Events & Reward**


| Field            | Description                                          |
| ---------------- | ---------------------------------------------------- |
| `_onSolved`      | UnityEvent fired once on solve                       |
| `_rewardObjects` | GameObjects activated on solve (doors, lights, etc.) |


**Public Properties**


| Property      | Description                                            |
| ------------- | ------------------------------------------------------ |
| `IsSolved`    | `true` once the puzzle has been solved                 |
| `IsResetting` | `true` while levers are locked during a pressure reset |


### `PressureGauge`

Placed on the gauge collider (`screen`). Now **purely visual** — the arrow tracks pressure in real-time. Kept for backward compatibility but no longer interactive. `CanInteract()` always returns `false`.

---

## Scene Hierarchy

```
PreasurePuzzel              ← PressurePuzzle component
  Panel1                    ← panel mesh
    LiverHolder             ← lever base mesh
    stick1                  ← PressureLever, Interactable Layer
      Liver                 ← lever mesh + BoxCollider
  panel2 / stick2 / Liver
  ...
  panel6 / stick6 / Liver
  screen                    ← PressureGauge (visual only)
    MEDIDOR BASE2
      dialPreasure
        dialBody / dialCenter / DialGlass / tornilo
    arrow                   ← assign to PressurePuzzle._arrow
      dialArrow
  VFX
    steam                   ← ParticleSystem, assigned to _steamEmitters
    steam (1)
    steam (2)
```

The number of levers is determined by how many child GameObjects carry a `PressureLever` component. Add or remove sticks in the hierarchy — no code change needed.

---

## Session Lifecycle

```
Start()
  ├── Collect PressureLever children
  ├── Cache arrow euler (gimbal lock prevention)
  ├── Setup AudioSources
  ├── [if save says solved] RestoreSolvedState() → done
  │
  ├── GenerateAndAssignLeverValues()
  │     Magnitudes = [base, base+step, ..., base+(N-1)·step]
  │     Fisher-Yates shuffle → assigned in random order
  │     Each lever: offValue = 0, onValue = +magnitude
  │
  ├── Compute _maxTotal (sum of all magnitudes)
  │
  ├── PickRandomSolution()
  │     Random mask with minOn ≤ ON-count ≤ (N – minOn)
  │     Total must be in [_minSolutionFraction, _maxSolutionFraction) × maxTotal
  │     FindAllValidSolutions() → check minFlips ≥ _minFlipsFromSolution
  │     Stores _solutionTotal, _solutionMask
  │
  ├── All levers OFF → arrow at 0° (start)
  ├── Initialize pressure at 0° (no initial drift)
  └── SnapVisual() on every lever
```

---

## Inertial Pressure

`_currentArrowAngle` chases `_targetArrowAngle` (the live lever sum mapped to angle) via `SmoothDamp` at `_pressureSmoothSpeed`. The arrow angle is derived from the inertial pressure, not the instantaneous sum. This gives the player visual feedback as the needle creeps toward the danger zone — they can react and flip another lever before the threshold is crossed.

---

## Pressure-to-Angle Mapping

```csharp
float angle = pressure * (_solveAngle / _solutionTotal);
```

- `pressure = 0` (all OFF) → `0°` (start)
- `pressure = _solutionTotal` → `180°` (target, exactly)
- `pressure = _maxTotal` (all ON) → `maxTotal × 180 / solutionTotal`

The solution total is constrained to `[0.5 × maxTotal, 0.6 × maxTotal)` so that:

- Max angle < 360° (no visual wrap)
- Danger angle (300°) is reachable (player can overshoot)

No manual solvability check is needed. The Inspector HelpBox shows the magnitudes, solution range, and valid combination count.

---

## Danger Zone & Reset

A single danger threshold at `_dangerAngle` (300°):

```
0°  ─── start (all OFF, safe)
  │
180° ─── target (solve here)
  │
240° ─── steam starts (warningFraction × dangerAngle)
  │
300° ─── danger threshold (reset triggers)
  │
~343° ─── max angle (all ON, past danger)
```

**Edge-crossing detection** prevents infinite reset loops. Reset fires only on the **transition** from safe to dangerous — not while already past the threshold.

### Reset Sequence

1. `TriggerReset()` — all levers snap to OFF, `_targetArrowAngle = 0°`, reset sound plays
2. `UpdateReset()` — arrow SmoothDamps from current (≥300°) **down** to 0°, passing through 180°
3. Steam forced to maximum emission during reset, fading to zero over cooldown
4. Levers stay locked (`CanInteract() → false`) until `_resetCooldown` elapses
5. Unlocked — player starts over from 0° (all OFF)

### Reset Path

The arrow takes the **long descending path**: 300° → 250° → … → 180° → … → 0°. It does NOT wrap around (300° → 360° → 0°). This is the natural SmoothDamp behavior — the angle simply decreases toward 0.

### Steam VFX Scaling

Steam emission ramps linearly from 0 to `_maxSteamEmission` as the arrow approaches the danger angle. The ramp starts at `_warningFraction × _dangerAngle` (e.g. 240° at 0.8 × 300°) and reaches maximum at the threshold (300°). During a reset, steam is forced to maximum and fades over the cooldown. On solve, all steam stops.

---

## Minimum Flips Guarantee

`_minFlipsFromSolution` is enforced against **all** valid combinations found by `FindAllValidSolutions()`, preventing a 1-flip shortcut through a secondary winning combination. Since the start is always all-OFF (mask 0), the flips to a solution = number of ON levers in that solution.

---

## Interaction Gating

The system uses `IInteractable.CanInteract()`. `FPSController.HandleInteractionDetection()` checks it first: when `false`, the object is fully excluded from the interaction pipeline.


| Component       | `CanInteract() = false` when              |
| --------------- | ----------------------------------------- |
| `PressureLever` | `puzzle.IsSolved` OR `puzzle.IsResetting` |
| `PressureGauge` | Always (visual only)                      |


---

## Solve Detection

Solve fires in `Update()` the moment both the current and target arrow angles are within `_solveAngleTolerance` of `_solveAngle` (180°) — no need to wait for full `SmoothDamp` convergence. The `_targetArrowAngle` condition prevents a false positive when the arrow merely passes through 180° en route to a different target. During reset, `_solveLocked` blocks solve detection even though the arrow passes through 180°.

---

## Save System Integration

On solve, lever states (the winning combination) are serialized alongside `isSolved`. On restore:

- `LoadSaveData()` stores both flags before `Start()` runs
- `RestoreSolvedState()` applies saved lever positions, snaps arrow to 180°, stops all steam, activates rewards — no events fired

The player sees the exact lever combination they used to solve the puzzle.

---

## Dial Design

The dial face should be painted with zones:


| Zone    | Angle range      | Color  | Meaning                    |
| ------- | ---------------- | ------ | -------------------------- |
| Safe    | 0° – 180°        | Green  | Start to target zone       |
| Target  | 180° ± tolerance | Green  | Solve here                 |
| Warning | 240° – 300°      | Yellow | Steam starts ramping up    |
| Danger  | 300°+            | Red    | Reset triggers on crossing |


---

## Setup Guide

1. Create a root GameObject → add `PressurePuzzle`
2. Add child lever GameObjects with `PressureLever` on **Interactable Layer**
3. Set `_angleOnDelta` per lever (typically –180°)
4. Assign `arrow` Transform to `PressurePuzzle._arrow`
5. Set `_arrowBaseAngle` so the arrow visually points to 0° at start
6. Add steam `ParticleSystem` components to `_steamEmitters`
7. Assign `_resetSound` AudioClip
8. Add GameObjects to `_rewardObjects`
9. Set `_solveAngle` = 180° and `_dangerAngle` = 300° to match the physical dial
10. Tune `_pressureSmoothSpeed` — lower values make pressure more inertial (harder)
11. Tune `_resetCooldown` and `_resetPressureSpeed` for desired reset feel
12. Tune `_leverValueBase` and `_leverValueStep` — Inspector shows resulting magnitudes and range
13. Set `_minLeversOnInSolution` (default: 2)
14. Set `_minFlipsFromSolution` (default: 3)
15. Keep `_solveAngleTolerance` small (1–10°)
16. Adjust `_minSolutionFraction` / `_maxSolutionFraction` if using different magnitudes
17. Set a unique `_saveId`

---

## Console Output

```
[PressurePuzzle] Lever magnitudes: [20, 5, 30, 10, 25, 15]
[PressurePuzzle] 3 valid solution(s) found within ±10° of 180°.
[PressurePuzzle] Solution: 3/6 ON, total=55, mask=011010
[PressurePuzzle] 6 levers. MaxTotal=105. Solution total=55 → 180°. Danger at 300°. Max angle=343.6°. Valid solutions: 3.
[PressurePuzzle] Pressure reset triggered!
[PressurePuzzle] Reset complete — levers unlocked.
[PressurePuzzle] Solved!
```