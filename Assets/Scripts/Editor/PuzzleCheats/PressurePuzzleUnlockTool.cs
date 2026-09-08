using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor cheat tool to instantly solve the Pressure Puzzle in the current scene.
/// Only works in Play Mode — the door cinematic will play after the solve.
/// </summary>
public static class PressurePuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Pressure Puzzle";

    [MenuItem(MENU_PATH)]
    public static void SolvePressurePuzzle()
    {
        PressurePuzzle puzzle = Object.FindFirstObjectByType<PressurePuzzle>();

        if (puzzle == null)
        {
            Debug.LogWarning("[PuzzleCheats] PressurePuzzle not found in the current scene.");
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PuzzleCheats] Pressure Puzzle can only be solved in Play Mode.");
            return;
        }

        if (puzzle.IsSolved)
        {
            Debug.Log("[PuzzleCheats] Pressure Puzzle is already solved.");
            return;
        }

        puzzle.AutoSolve();
    }
}
