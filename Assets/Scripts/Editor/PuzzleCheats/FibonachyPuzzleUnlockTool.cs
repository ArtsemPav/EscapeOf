using UnityEngine;
using UnityEditor;
using System.Reflection;

public static class FibonachyPuzzleUnlockTool
{
    [MenuItem("Tools/PuzzlesCheats/Unlock Mechanical Locks")]
    public static void UnlockAllLocks()
    {
        MechanicalLock[] locks = Object.FindObjectsByType<MechanicalLock>(FindObjectsSortMode.None);

        if (locks.Length == 0)
        {
            Debug.Log("[Cheat] No MechanicalLock components found in the current scene.");
            return;
        }

        int count = 0;
        foreach (var lockObj in locks)
        {
            UnlockLock(lockObj);
            count++;
        }

        Debug.Log($"[Cheat] Successfully processed {count} mechanical locks in the scene.");
    }

    private static void UnlockLock(MechanicalLock lockObj)
    {
        Undo.RecordObject(lockObj, "Cheat Unlock");

        // Ensure we have a PuzzleModeController
        PuzzleModeController controller = GetPrivateField<PuzzleModeController>(lockObj, "_puzzleController");
        if (controller == null)
        {
            controller = lockObj.GetComponent<PuzzleModeController>();
            if (controller == null) controller = lockObj.GetComponentInParent<PuzzleModeController>();
            
            if (controller != null)
            {
                SetPrivateField(lockObj, "_puzzleController", controller);
                EditorUtility.SetDirty(lockObj);
            }
        }

        if (controller != null)
        {
            Undo.RecordObject(controller, "Cheat Unlock Controller");
        }

        int[] correctCombination = GetPrivateField<int[]>(lockObj, "_correctCombination");
        LockCylinder[] cylinders = GetPrivateField<LockCylinder[]>(lockObj, "_cylinders");

        if (correctCombination != null && cylinders != null)
        {
            for (int i = 0; i < cylinders.Length; i++)
            {
                if (cylinders[i] != null && i < correctCombination.Length)
                {
                    Undo.RecordObject(cylinders[i], "Cheat Unlock Cylinder");
                    cylinders[i].SetValue(correctCombination[i]);
                    EditorUtility.SetDirty(cylinders[i]);
                }
            }
        }

        // Call Solve() via reflection
        MethodInfo solveMethod = typeof(MechanicalLock).GetMethod("Solve", BindingFlags.NonPublic | BindingFlags.Instance);
        solveMethod?.Invoke(lockObj, null);

        // Force trigger events if Solve didn't for any reason (e.g. if field was null inside)
        if (controller != null && !controller.IsSolved)
        {
            controller.SetSolved();
        }

        EditorUtility.SetDirty(lockObj);
    }

    private static T GetPrivateField<T>(object obj, string fieldName)
    {
        FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field?.GetValue(obj);
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(obj, value);
    }
}
