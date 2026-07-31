using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// Editor tool to bypass puzzle mechanics for testing.
/// If invoked while the game is paused, the OnPuzzleSolved event is
/// deferred until timeScale is restored so the cinematic can play correctly.
/// </summary>
public static class BoardPuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Board Puzzle";

    private static bool _pendingSolvedEvent;

    [MenuItem(MENU_PATH)]
    public static void SolveBoardPuzzle()
    {
        BoardPuzzleManager manager = Object.FindFirstObjectByType<BoardPuzzleManager>();

        if (manager == null)
        {
            Debug.LogWarning("[PuzzleCheats] BoardPuzzleManager not found in the current scene.");
            return;
        }

        // 1. Set the solved flag
        FieldInfo solvedField = typeof(BoardPuzzleManager)
            .GetField("_isSolved", BindingFlags.NonPublic | BindingFlags.Instance);

        if (solvedField != null)
        {
            solvedField.SetValue(manager, true);
        }

        // 2. Call the locking method
        MethodInfo lockMethod = typeof(BoardPuzzleManager)
            .GetMethod("LockAllCylinders", BindingFlags.NonPublic | BindingFlags.Instance);

        if (lockMethod != null)
        {
            lockMethod.Invoke(manager, null);
        }

        // 3. Invoke OnPuzzleSolved — either immediately or after unpause
        if (Time.timeScale == 0f)
        {
            if (!_pendingSolvedEvent)
            {
                _pendingSolvedEvent = true;
                EditorApplication.update += WaitForUnpauseAndFireSolved;
                Debug.Log("<color=yellow>[PuzzleCheats] Board Puzzle marked as solved. "
                          + "OnPuzzleSolved will fire when you unpause.</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] A pending solve is already waiting for unpause.");
            }
        }
        else
        {
            FireSolvedEvent(manager);
            Debug.Log("<color=green>[PuzzleCheats] Board Puzzle has been force-solved!</color>");
        }
    }

    private static void WaitForUnpauseAndFireSolved()
    {
        if (!EditorApplication.isPlaying)
        {
            _pendingSolvedEvent = false;
            EditorApplication.update -= WaitForUnpauseAndFireSolved;
            return;
        }

        if (Time.timeScale > 0f)
        {
            _pendingSolvedEvent = false;
            EditorApplication.update -= WaitForUnpauseAndFireSolved;

            BoardPuzzleManager manager = Object.FindFirstObjectByType<BoardPuzzleManager>();

            if (manager != null)
            {
                FireSolvedEvent(manager);
                Debug.Log("<color=green>[PuzzleCheats] OnPuzzleSolved fired after unpause! "
                          + "Board Puzzle has been force-solved!</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] BoardPuzzleManager disappeared before unpause.");
            }
        }
    }

    /// <summary>Fires the OnPuzzleSolved event.</summary>
    private static void FireSolvedEvent(BoardPuzzleManager manager)
    {
        if (manager.OnPuzzleSolved != null)
        {
            manager.OnPuzzleSolved.Invoke();
        }
    }
}
