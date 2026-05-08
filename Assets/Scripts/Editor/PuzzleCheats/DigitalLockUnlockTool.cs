using UnityEngine;
using UnityEditor;
using System.Reflection;

public static class DigitalLockUnlockTool
{
    [MenuItem("Tools/PuzzlesCheats/Unlock Digital Locks")]
    public static void UnlockAllLocks()
    {
        DigitalLockSystem[] locks = Object.FindObjectsByType<DigitalLockSystem>(FindObjectsSortMode.None);

        if (locks.Length == 0)
        {
            Debug.Log("[Cheat] No DigitalLockSystem components found in the current scene.");
            return;
        }

        int count = 0;
        foreach (var lockSystem in locks)
        {
            UnlockLock(lockSystem);
            count++;
        }

        Debug.Log($"[Cheat] Successfully processed {count} digital locks in the scene.");
    }

    private static void UnlockLock(DigitalLockSystem lockSystem)
    {
        Undo.RecordObject(lockSystem, "Cheat Unlock");

        // Ensure we have a PuzzleModeController
        PuzzleModeController controller = GetPrivateField<PuzzleModeController>(lockSystem, "_puzzleController");
        if (controller == null)
        {
            controller = lockSystem.GetComponent<PuzzleModeController>();
            if (controller == null) controller = lockSystem.GetComponentInParent<PuzzleModeController>();
            
            if (controller != null)
            {
                SetPrivateField(lockSystem, "_puzzleController", controller);
                EditorUtility.SetDirty(lockSystem);
            }
        }

        if (controller != null)
        {
            Undo.RecordObject(controller, "Cheat Unlock Controller");
        }

        // Get the correct code
        string correctCode = GetPrivateField<string>(lockSystem, "_correctCode");

        // Set the current input to the correct code
        SetPrivateField(lockSystem, "_currentInput", correctCode);
        
        // Reset temporary message flag
        SetPrivateField(lockSystem, "_isDisplayingTemporaryMessage", false);

        // Call Submit
        MethodInfo submitMethod = typeof(DigitalLockSystem).GetMethod("Submit", BindingFlags.Public | BindingFlags.Instance);
        if (submitMethod == null)
            submitMethod = typeof(DigitalLockSystem).GetMethod("Submit", BindingFlags.NonPublic | BindingFlags.Instance);
        
        submitMethod?.Invoke(lockSystem, null);

        // Force trigger events if Submit didn't (e.g. if field was null inside)
        if (controller != null && !controller.IsSolved)
        {
            controller.SetSolved();
        }

        EditorUtility.SetDirty(lockSystem);
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
