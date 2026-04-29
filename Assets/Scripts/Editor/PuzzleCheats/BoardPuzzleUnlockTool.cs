using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// Editor tool to bypass puzzle mechanics for testing.
/// </summary>
public static class BoardPuzzleUnlockTool
{
    [MenuItem("Tools/PuzzlesCheats/Solve Board Puzzle")]
    public static void SolveBoardPuzzle()
    {
        // Find the puzzle manager in the scene
        BoardPuzzleManager manager = Object.FindFirstObjectByType<BoardPuzzleManager>();

        if (manager == null)
        {
            Debug.LogWarning("[PuzzleCheats] BoardPuzzleManager not found in the current scene.");
            return;
        }

        // Using reflection to set private state and trigger internal logic
        // 1. Set the solved flag
        FieldInfo solvedField = typeof(BoardPuzzleManager).GetField("_isSolved", BindingFlags.NonPublic | BindingFlags.Instance);
        if (solvedField != null)
        {
            solvedField.SetValue(manager, true);
        }

        // 2. Call the locking method
        MethodInfo lockMethod = typeof(BoardPuzzleManager).GetMethod("LockAllCylinders", BindingFlags.NonPublic | BindingFlags.Instance);
        if (lockMethod != null)
        {
            lockMethod.Invoke(manager, null);
        }

        // 3. Invoke the public UnityEvent to trigger linked gameplay systems
        if (manager.OnPuzzleSolved != null)
        {
            manager.OnPuzzleSolved.Invoke();
        }

        Debug.Log("<color=green>[PuzzleCheats] Board Puzzle has been force-solved!</color>");
    }
}
