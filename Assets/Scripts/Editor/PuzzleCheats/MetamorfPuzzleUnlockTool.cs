using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// Editor tool to bypass the Metamorf puzzle mechanics for testing.
/// If invoked while the game is paused, the OnPuzzleSolved event is
/// deferred until timeScale is restored so the cinematic can play correctly.
/// </summary>
public static class MetamorfPuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Metamorf Puzzle";

    private static bool _pendingSolvedEvent;

    [MenuItem(MENU_PATH)]
    public static void SolveMetamorfPuzzle()
    {
        MetamorfPuzzleController controller = Object.FindFirstObjectByType<MetamorfPuzzleController>();

        if (controller == null)
        {
            Debug.LogWarning("[PuzzleCheats] MetamorfPuzzleController not found in the current scene.");
            return;
        }

        // 1. Set the solved flag
        FieldInfo solvedField = typeof(MetamorfPuzzleController)
            .GetField("_isSolved", BindingFlags.NonPublic | BindingFlags.Instance);

        if (solvedField != null)
        {
            solvedField.SetValue(controller, true);
        }

        // 2. Disable colliders assigned to the solved state
        MethodInfo disableCollidersMethod = typeof(MetamorfPuzzleController)
            .GetMethod("DisableCollidersOnSolved", BindingFlags.NonPublic | BindingFlags.Instance);

        if (disableCollidersMethod != null)
        {
            disableCollidersMethod.Invoke(controller, null);
        }

        // 3. Invoke OnPuzzleSolved — either immediately or after unpause
        if (Time.timeScale == 0f)
        {
            if (!_pendingSolvedEvent)
            {
                _pendingSolvedEvent = true;
                EditorApplication.update += WaitForUnpauseAndFireSolved;
                Debug.Log("<color=yellow>[PuzzleCheats] Metamorf Puzzle marked as solved. "
                          + "OnPuzzleSolved will fire when you unpause.</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] A pending solve is already waiting for unpause.");
            }
        }
        else
        {
            FireSolvedEvent(controller);
            Debug.Log("<color=green>[PuzzleCheats] Metamorf Puzzle has been force-solved!</color>");
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

            MetamorfPuzzleController controller = Object.FindFirstObjectByType<MetamorfPuzzleController>();

            if (controller != null)
            {
                FireSolvedEvent(controller);
                Debug.Log("<color=green>[PuzzleCheats] OnPuzzleSolved fired after unpause! "
                          + "Metamorf Puzzle has been force-solved!</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] MetamorfPuzzleController disappeared before unpause.");
            }
        }
    }

    /// <summary>Fires the OnPuzzleSolved event and saves the game state.</summary>
    private static void FireSolvedEvent(MetamorfPuzzleController controller)
    {
        if (controller.OnPuzzleSolved != null)
        {
            controller.OnPuzzleSolved.Invoke();
        }

        if (Application.isPlaying && SaveManager.Instance != null)
        {
            SaveManager.Instance.Save();
        }
    }
}
