using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// Editor cheat tool to individually unlock the Da Vinci puzzle in Room 4.
/// Finds the MechanicalLock by its save ID and triggers the full solve flow.
/// </summary>
public static class DavinchiPuzzleRoom4UnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Unlock Da Vinci (Room 4)";
    private const string TARGET_SAVE_ID = "davinchi_puzzle_room4";

    [MenuItem(MENU_PATH)]
    public static void UnlockDavinchi()
    {
        MechanicalLock target = FindLockBySaveId(TARGET_SAVE_ID);

        if (target == null)
        {
            Debug.LogWarning($"[PuzzleCheats] No MechanicalLock with save ID '{TARGET_SAVE_ID}' found.");
            return;
        }

        UnlockLock(target);
    }

    private static MechanicalLock FindLockBySaveId(string saveId)
    {
        MechanicalLock[] locks = Object.FindObjectsByType<MechanicalLock>(FindObjectsSortMode.None);

        foreach (var lockObj in locks)
        {
            string id = GetPrivateField<string>(lockObj, "_saveId");
            if (id == saveId)
                return lockObj;
        }

        return null;
    }

    private static void UnlockLock(MechanicalLock lockObj)
    {
        Undo.RecordObject(lockObj, "Cheat Unlock Da Vinci Room 4");

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

        MethodInfo solveMethod = typeof(MechanicalLock).GetMethod("Solve", BindingFlags.NonPublic | BindingFlags.Instance);
        solveMethod?.Invoke(lockObj, null);

        if (controller != null && !controller.IsSolved)
        {
            controller.SetSolved();
        }

        EditorUtility.SetDirty(lockObj);
        Debug.Log($"<color=green>[PuzzleCheats] Da Vinci (Room 4) has been force-solved!</color>");
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
