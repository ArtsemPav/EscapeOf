using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validates that every key registered in <see cref="LocalizationKeys"/> exists in both
/// the Ru and En text tables. Run via Tools → Localization → Validate Keys.
/// Use after adding new keys or when merging localization tables.
/// </summary>
public static class LocalizationKeysValidator
{
    private const string RuTablePath = "Assets/Data/Localization/Resources/Localization/Ru.asset";
    private const string EnTablePath = "Assets/Data/Localization/Resources/Localization/En.asset";

    /// <summary>Checks every LocalizationKeys constant against both text tables and reports missing entries.</summary>
    [MenuItem("Tools/Localization/Validate Keys")]
    public static void ValidateKeys()
    {
        var ru = AssetDatabase.LoadAssetAtPath<LocalizedTextTable>(RuTablePath);
        var en = AssetDatabase.LoadAssetAtPath<LocalizedTextTable>(EnTablePath);

        if (ru == null || en == null)
        {
            Debug.LogError($"[LocalizationValidator] Tables not found: Ru={(ru == null ? "missing" : "ok")}, En={(en == null ? "missing" : "ok")}.");
            return;
        }

        var missing = new List<string>();
        CheckAllConstants(ru, "Ru", missing);
        CheckAllConstants(en, "En", missing);

        if (missing.Count == 0)
        {
            Debug.Log($"[LocalizationValidator] OK — all {typeof(LocalizationKeys).GetFields().Length} keys present in Ru and En.", ru);
        }
        else
        {
            foreach (string entry in missing)
                Debug.LogError($"[LocalizationValidator] {entry}");
        }
    }

    /// <summary>Reflects over all LocalizationKeys string constants and checks each against one table.</summary>
    private static void CheckAllConstants(LocalizedTextTable table, string tableName, List<string> missing)
    {
        var fields = typeof(LocalizationKeys).GetFields();
        foreach (var field in fields)
        {
            if (field.FieldType != typeof(string)) continue;
            string key = (string)field.GetValue(null);
            if (!table.TryGet(key, out _))
                missing.Add($"Missing key '{key}' ({field.Name}) in {tableName} table.");
        }
    }
}
