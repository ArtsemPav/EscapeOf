using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility for resetting save progress without launching the game.
/// Accessible via Tools → Escape → Reset Save Progress.
/// </summary>
public static class SaveProgressEditor
{
    private const string SaveFolder      = "saves";
    private const string FilePrefix      = "slot_";
    private const string FileExtension   = ".json";
    private const int    DefaultSlot     = 0;
    private const int    BackupCount     = 2;

    [MenuItem("Tools/Escape/Reset Save Progress")]
    private static void ResetSaveProgress()
    {
        string saveDir  = Path.Combine(Application.persistentDataPath, SaveFolder);
        string mainPath = GetSlotPath(saveDir, DefaultSlot);

        bool anyExists = File.Exists(mainPath);
        for (int i = 1; i <= BackupCount && !anyExists; i++)
            anyExists = File.Exists(GetBackupPath(saveDir, DefaultSlot, i));

        if (!anyExists)
        {
            EditorUtility.DisplayDialog(
                "Сброс прогресса",
                "Сохранённых данных не найдено.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Сброс прогресса",
            $"Удалить файл сохранения?\n\n{mainPath}",
            "Удалить", "Отмена");

        if (!confirmed) return;

        int deleted = 0;
        deleted += TryDelete(mainPath);
        for (int i = 1; i <= BackupCount; i++)
            deleted += TryDelete(GetBackupPath(saveDir, DefaultSlot, i));

        Debug.Log($"[SaveProgressEditor] Удалено файлов: {deleted}. Прогресс сброшен.");
        EditorUtility.DisplayDialog(
            "Сброс прогресса",
            $"Готово. Удалено файлов: {deleted}.",
            "OK");
    }

    [MenuItem("Tools/Escape/Reset Save Progress", validate = true)]
    private static bool ValidateResetSaveProgress() => !EditorApplication.isPlaying;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetSlotPath(string dir, int slot)
        => Path.Combine(dir, $"{FilePrefix}{slot}{FileExtension}");

    private static string GetBackupPath(string dir, int slot, int n)
        => Path.Combine(dir, $"{FilePrefix}{slot}_bk{n}{FileExtension}");

    private static int TryDelete(string path)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            File.Delete(path);
            return 1;
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[SaveProgressEditor] Не удалось удалить {path}: {e.Message}");
            return 0;
        }
    }
}
