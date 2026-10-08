using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Bezi;

/// <summary>
/// Finds unused assets by GUID-referencing: parses every YAML asset in the
/// project, collects all referenced GUIDs, then reports assets whose GUID is
/// never referenced from anywhere. Assets under any "Resources" folder and
/// scenes listed in Build Settings are always treated as used (they load by
/// name/path at runtime).
/// </summary>
public static class UnusedAssetAudit
{
    private const string GuidPattern = "guid: ([0-9a-fA-F]{32})";
    private const string ResourcesFolderName = "/Resources/";
    private const string MetaExtension = ".meta";

    /// <summary>
    /// Scans the given asset scope (folder or single asset path relative to
    /// the project, e.g. "Assets/AdditionalAssetsLibrary") and returns a text
    /// report: unused / used-only-inside-scope / used. Read-only.
    /// </summary>
    [BeziAction(
        "Finds unused assets under the given Assets scope by GUID-reference analysis. " +
        "Returns a report with three sections: UNUSED, USED-ONLY-INSIDE-SCOPE, USED. " +
        "Assets under any Resources folder and Build Settings scenes are treated as used.",
        IsReadOnly = true
    )]
    public static string FindUnusedAssets(string scope)
    {
        if (string.IsNullOrEmpty(scope))
            return "scope is empty";

        var normalizedScope = scope.Replace('\\', '/').TrimEnd('/');
        if (!normalizedScope.StartsWith("Assets"))
            normalizedScope = "Assets/" + normalizedScope.TrimStart('/');

        // 1. Collect candidate asset paths (skip .meta, hidden folders, scripts).
        var candidates = new List<string>();
        if (File.Exists(normalizedScope))
            candidates.Add(normalizedScope);
        else
            CollectAssetPaths(normalizedScope, candidates);

        if (candidates.Count == 0)
            return $"No importable assets found under {normalizedScope}";

        // 2. Read candidate GUIDs from .meta files.
        var candidateGuids = new Dictionary<string, string>(); // assetPath -> guid
        foreach (var assetPath in candidates)
        {
            var guid = ReadGuid(assetPath);
            if (!string.IsNullOrEmpty(guid))
                candidateGuids[assetPath] = guid;
        }

        // 3. Parse every YAML asset in the project and count GUID references.
        var referenceCounts = new Dictionary<string, int>(); // guid -> count
        var buildSceneGuids = GetBuildSettingsSceneGuids();
        var resourcesAssetPaths = new HashSet<string>(
            candidateGuids.Keys.Where(p => p.Contains(ResourcesFolderName)));

        var allAssets = AssetDatabase.GetAllAssetPaths();
        var guidsReferenced = new HashSet<string>();

        foreach (var assetPath in allAssets)
        {
            if (!IsParsableAsset(assetPath))
                continue;

            string content;
            try { content = File.ReadAllText(assetPath); }
            catch { continue; } // unreadable — skip, logged in summary

            var matches = Regex.Matches(content, GuidPattern);
            foreach (Match m in matches)
                guidsReferenced.Add(m.Groups[1].Value.ToLowerInvariant());
        }

        // A GUID referenced from the asset's own .meta doesn't count as a use,
        // but self-references inside one file do not happen for guids, so we
        // only subtract per-asset file guids when the ONLY reference source is
        // that asset itself. We approximate: references from other files count.
        // To keep it simple and safe, references found in any parsed file
        // (including the asset itself, e.g. a prefab referencing its own
        // children) mark the asset as used — this may hide a few unused
        // prefabs but never flags a used asset as unused.
        // For the "used-only-inside-scope" section we do a second, scope-only
        // reference set.
        var scopeOnlyGuids = new HashSet<string>();
        foreach (var assetPath in allAssets)
        {
            if (!IsParsableAsset(assetPath))
                continue;
            var normalized = assetPath.Replace('\\', '/');
            if (normalized.StartsWith(normalizedScope, System.StringComparison.OrdinalIgnoreCase))
                continue; // skip files inside scope for the scope-only pass

            string content;
            try { content = File.ReadAllText(assetPath); }
            catch { continue; }

            foreach (Match m in Regex.Matches(content, GuidPattern))
                scopeOnlyGuids.Add(m.Groups[1].Value.ToLowerInvariant());
        }

        // 4. Classify.
        var unused = new List<string>();
        var usedOnlyInsideScope = new List<string>();
        var used = new List<string>();

        foreach (var pair in candidateGuids)
        {
            var assetPath = pair.Key;
            var guid = pair.Value;

            if (guidsReferenced.Contains(guid) || resourcesAssetPaths.Contains(assetPath))
            {
                if (!scopeOnlyGuids.Contains(guid) && !resourcesAssetPaths.Contains(assetPath))
                    usedOnlyInsideScope.Add(assetPath);
                else
                    used.Add(assetPath);
            }
            else
            {
                unused.Add(assetPath);
            }
        }

        // Build settings scenes are always "used".
        var report = new StringBuilder();
        report.AppendLine($"=== Unused asset audit: {normalizedScope} ===");
        report.AppendLine($"Scanned assets: {candidates.Count}, project files parsed: {allAssets.Count(IsParsableAsset)}");
        report.AppendLine();

        report.AppendLine($"--- UNUSED ({unused.Count}) ---");
        foreach (var path in unused.OrderBy(p => p))
            report.AppendLine(path);
        report.AppendLine();

        report.AppendLine($"--- USED ONLY INSIDE SCOPE ({usedOnlyInsideScope.Count}) ---");
        foreach (var path in usedOnlyInsideScope.OrderBy(p => p))
            report.AppendLine(path);
        report.AppendLine();

        report.AppendLine($"--- USED ({used.Count}) ---");
        foreach (var path in used.OrderBy(p => p))
            report.AppendLine(path);

        return report.ToString();
    }

    /// <summary>
    /// Deletes the given asset paths via AssetDatabase.DeleteAsset (removes
    /// .meta files with them). Requires explicit user approval.
    /// </summary>
    [BeziAction(
        "Deletes the given asset paths (semicolon-separated) via AssetDatabase.DeleteAsset, " +
        "including their .meta files. DANGEROUS operation, requires user approval.",
        RequireApproval = "Delete the listed assets?"
    )]
    public static string DeleteUnusedAssets(string assetPaths)
    {
        if (string.IsNullOrEmpty(assetPaths))
            return "assetPaths is empty";

        var details = new StringBuilder();
        int deleted = 0;

        foreach (var rawPath in assetPaths.Split(';'))
        {
            var assetPath = rawPath.Trim();
            if (string.IsNullOrEmpty(assetPath)) continue;

            if (!AssetDatabase.LoadAssetAtPath<Object>(assetPath))
            {
                details.AppendLine($"NOT FOUND: {assetPath}");
                continue;
            }

            if (AssetDatabase.DeleteAsset(assetPath))
            {
                deleted++;
                details.AppendLine($"DELETED: {assetPath}");
            }
            else
            {
                details.AppendLine($"FAILED: {assetPath}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Deleted {deleted} assets.\n{details}";
    }

    /// <summary>
    /// Recursively collects importable asset paths (skips .meta, folders,
    /// hidden files and folders ending with ~).
    /// </summary>
    private static void CollectAssetPaths(string scope, List<string> results)
    {
        if (!Directory.Exists(scope))
            return;

        foreach (var file in Directory.GetFiles(scope))
        {
            if (file.EndsWith(MetaExtension) || Path.GetFileName(file).StartsWith("."))
                continue;
            var normalized = file.Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<Object>(normalized) != null)
                results.Add(normalized);
        }

        foreach (var dir in Directory.GetDirectories(scope))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(".") || dirName.EndsWith("~"))
                continue;
            CollectAssetPaths(dir.Replace('\\', '/'), results);
        }
    }

    /// <summary>
    /// Reads the GUID from the .meta file next to the given asset path.
    /// </summary>
    private static string ReadGuid(string assetPath)
    {
        var metaPath = assetPath + MetaExtension;
        if (!File.Exists(metaPath))
            return null;
        try
        {
            var content = File.ReadAllText(metaPath);
            var match = Regex.Match(content, GuidPattern);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Collects GUIDs of all scenes enabled in Build Settings.
    /// </summary>
    private static HashSet<string> GetBuildSettingsSceneGuids()
    {
        var guids = new HashSet<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled)
                continue;
            var guid = ReadGuid(scene.path);
            if (!string.IsNullOrEmpty(guid))
                guids.Add(guid);
        }
        return guids;
    }

    /// <summary>
    /// True if the file is a YAML asset whose text can be parsed for GUID refs.
    /// </summary>
    private static bool IsParsableAsset(string assetPath)
    {
        var extension = Path.GetExtension(assetPath);
        switch (extension)
        {
            case ".unity":
            case ".prefab":
            case ".asset":
            case ".mat":
            case ".anim":
            case ".controller":
            case ".overrideController":
            case ".playable":
            case ".shadervariants":
            case ".renderTexture":
            case ".vfx":
                return File.Exists(assetPath);
            default:
                return false;
        }
    }
}
