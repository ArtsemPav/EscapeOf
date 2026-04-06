using UnityEngine;
using UnityEditor;
using PuzzleGame;

namespace PuzzleGame.Editor
{
    /// <summary>
    /// Editor utility to automatically solve the Boss Puzzle in the scene.
    /// </summary>
    public static class PuzzleSolverUtility
    {
        private const string MENU_PATH = "Tools/Solve Boss Puzzle";

        [MenuItem(MENU_PATH)]
        public static void SolvePuzzle()
        {
            PuzzleManager manager = Object.FindFirstObjectByType<PuzzleManager>();

            if (manager == null)
            {
                Debug.LogWarning("PuzzleManager not found in the current scene.");
                return;
            }

            // Record undo for all puzzle elements and the manager
            Undo.RecordObject(manager, "Auto Solve Puzzle");
            
            PuzzleElement[] elements = Object.FindObjectsByType<PuzzleElement>(FindObjectsSortMode.None);
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

            Debug.Log("Boss Puzzle has been automatically solved.");
        }
    }
}
