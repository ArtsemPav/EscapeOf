using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scans Assets/Data/Items for ItemData assets and Assets/Data/Recipes
/// for CraftingRecipe assets, then writes them into every InventorySystem found in the
/// open scenes. Refreshes automatically when files in those folders change (via InventoryAssetWatcher).
/// Use Tools > Inventory > Refresh Items and Recipes for an on-demand refresh.
/// </summary>
public static class InventoryAutoPopulate
{
    private const string ItemsFolder   = "Assets/Data/Items";
    private const string RecipesFolder = "Assets/Data/Recipes";

    /// <summary>Scans Data folders and updates InventorySystem in the open scene(s).</summary>
    [MenuItem("Tools/Inventory/Refresh Items and Recipes")]
    public static void Refresh()
    {
        var items   = LoadAll<ItemData>(ItemsFolder);
        var recipes = LoadAll<CraftingRecipe>(RecipesFolder);

        var systems = Object.FindObjectsByType<InventorySystem>(FindObjectsSortMode.None);
        if (systems.Length == 0)
        {
            Debug.LogWarning("[InventoryAutoPopulate] No InventorySystem found in open scenes. Open the scene first.");
            return;
        }

        foreach (var system in systems)
        {
            var so = new SerializedObject(system);
            WriteArray(so, "_allItems", items);
            WriteArray(so, "recipes",   recipes);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(system);
        }

        Debug.Log($"[InventoryAutoPopulate] Synced {items.Count} item(s), {recipes.Count} recipe(s) → InventorySystem.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<T> LoadAll<T>(string folder) where T : Object
    {
        var result = new List<T>();
        var guids  = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
        foreach (var guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null)
                result.Add(asset);
        }
        return result;
    }

    private static void WriteArray<T>(SerializedObject so, string propName, List<T> values) where T : Object
    {
        var prop = so.FindProperty(propName);
        if (prop == null || !prop.isArray) return;
        prop.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}

/// <summary>
/// Watches Assets/Data/Items and Assets/Data/Recipes for any file changes
/// (add / delete / rename / move) and triggers an automatic refresh.
/// </summary>
public class InventoryAssetWatcher : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (TouchesInventoryFolders(imported)  ||
            TouchesInventoryFolders(deleted)   ||
            TouchesInventoryFolders(moved)     ||
            TouchesInventoryFolders(movedFrom))
        {
            InventoryAutoPopulate.Refresh();
        }
    }

    private static bool TouchesInventoryFolders(string[] paths)
    {
        foreach (var p in paths)
            if (p.StartsWith("Assets/Data/Items") || p.StartsWith("Assets/Data/Recipes"))
                return true;
        return false;
    }
}
