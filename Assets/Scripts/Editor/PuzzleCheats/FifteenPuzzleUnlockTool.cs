using UnityEngine;
using UnityEditor;
using PuzzleGame;

namespace PuzzleGame.Editor
{
    /// <summary>
    /// Editor utility to automatically solve the Fifteen Puzzle in the scene.
    /// </summary>
    public static class FifteenPuzzleUnlockTool {
        private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Fifteen Puzzle";

        [MenuItem(MENU_PATH)]
        public static void SolvePuzzle()
        {
            FifteenPuzzleManager manager = Object.FindFirstObjectByType<FifteenPuzzleManager>();

            if (manager == null)
            {
                Debug.LogWarning("PuzzleManager not found in the current scene.");
                return;
            }

            // Record undo for all puzzle elements and the manager
            Undo.RecordObject(manager, "Auto Solve Puzzle");
            
            FifteenPuzzleElement[] elements = Object.FindObjectsByType<FifteenPuzzleElement>(FindObjectsSortMode.None);
            foreach (var element in elements)
            {
                Undo.RecordObject(element.transform, "Auto Solve Puzzle Position");
                Undo.RecordObject(element, "Auto Solve Puzzle Data");
            }

            manager.AutoSolve();
            
            // Mark the scene as dirty so changes are saved
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(manager);
                foreach (var element in elements)
                {
                    EditorUtility.SetDirty(element);
                }
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }

            Debug.Log("Fifteen Puzzle has been automatically solved.");
        }
    }
}
