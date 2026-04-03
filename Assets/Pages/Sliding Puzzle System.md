## Overview

The Sliding Puzzle system is a dynamic 3D implementation of the classic "15 puzzle" game. It features automatic grid generation, shuffle logic, and full integration with the project's interaction system.

## Components

### PuzzleManager
The central controller attached to the `Boss Puzzle` object.
- **Grid Settings**: Configure `Width`, `Height`, and `Spacing`.
- **Logic**: Automatically detects all child objects with `PuzzleElement` and arranges them into a grid.
- **Shuffle**: Performs a series of valid moves at the start of the game to ensure the puzzle is solvable.

### PuzzleElement
Attached to each individual tile.
- **Interaction**: Implements `IInteractable`, allowing players to click or press interaction keys.
- **Movement**: Smoothly interpolates to the empty adjacent slot using `Vector3.Lerp`.
- **Validation**: Stores its `TargetIndex` to verify the win condition.

## Setup Instructions

1. **Hierarchy**: Place all tiles as children of a single GameObject (e.g., `Boss Puzzle`).
2. **Manager**: Add `PuzzleManager` to the parent object.
3. **Elements**: Add `PuzzleElement` and a `BoxCollider` to each tile.
4. **Layer**: Ensure all tiles are on the **Interactable Layer** for the interaction system to detect them.
5. **Win Condition**: The system checks if all elements are in their original birth-order positions.

## Controls

- **Mouse Click (LMB)**: Click on a tile adjacent to the empty space to move it.
- **Interact Key**: Use the standard interaction key while looking at a tile.

## Configuration

| Property | Description |
|---|---|
| **Width / Height** | Dimensions of the puzzle grid (e.g., 3x3, 4x4). |
| **Spacing** | Distance between the centers of the tiles. |
| **Move Speed** | How fast the tiles slide into the empty spot. |
