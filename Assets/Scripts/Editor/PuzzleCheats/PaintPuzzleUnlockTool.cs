using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor cheat tool to instantly solve the Paint (Loop) Puzzle in the current scene.
/// Only works in Play Mode — the puzzle cinematic will play after the solve.
/// </summary>
public static class PaintPuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Paint Puzzle";

    [MenuItem(MENU_PATH)]
    public static void SolvePaintPuzzle()
    {
        LoopPuzzleController controller = Object.FindFirstObjectByType<LoopPuzzleController>();

        if (controller == null)
        {
            Debug.LogWarning("[PuzzleCheats] LoopPuzzleController not found in the current scene.");
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PuzzleCheats] Paint Puzzle can only be solved in Play Mode.");
            return;
        }

        if (controller.IsSolved)
        {
            Debug.Log("[PuzzleCheats] Paint Puzzle is already solved.");
            return;
        }

        controller.AutoSolve();
    }
}
