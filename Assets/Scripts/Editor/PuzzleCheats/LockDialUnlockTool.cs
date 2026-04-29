using UnityEditor;
using UnityEngine;

namespace Bezi.Editor.Tools
{
    /// <summary>
    /// Editor tool to quickly unlock any LockDial in the scene.
    /// </summary>
    public static class LockDialUnlockTool
    {
        [MenuItem("Tools/PuzzlesCheats/Unlock Safe Lock Dial")]
        public static void UnlockSelectedLockDial()
        {
            // Try to get LockDial from selection first, if not - find all in scene
            LockDial[] targets;
            
            if (Selection.activeGameObject != null)
            {
                var lockDial = Selection.activeGameObject.GetComponentInChildren<LockDial>();
                if (lockDial != null)
                {
                    targets = new[] { lockDial };
                }
                else
                {
                    targets = Object.FindObjectsByType<LockDial>(FindObjectsSortMode.None);
                }
            }
            else
            {
                targets = Object.FindObjectsByType<LockDial>(FindObjectsSortMode.None);
            }

            if (targets == null || targets.Length == 0)
            {
                Debug.LogWarning("[LockDial Tool] No LockDial found in the current scene.");
                return;
            }

            foreach (var lockDial in targets)
            {
                if (lockDial.IsUnlocked)
                {
                    Debug.Log($"[LockDial Tool] '{lockDial.name}' is already unlocked.");
                    continue;
                }

                Undo.RecordObject(lockDial, "Unlock LockDial");
                
                var type = typeof(LockDial);
                var unlockMethod = type.GetMethod("Unlock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (unlockMethod != null)
                {
                    unlockMethod.Invoke(lockDial, null);
                    Debug.Log($"<color=green>[LockDial Tool] Successfully unlocked '{lockDial.name}'!</color>");
                }
                
                EditorUtility.SetDirty(lockDial);
            }
        }

        [MenuItem("Tools/Unlock LockDial", true)]
        public static bool ValidateUnlockSelectedLockDial()
        {
            // Always available if there is at least one LockDial in the scene
            return Object.FindFirstObjectByType<LockDial>() != null;
        }
    }
}
