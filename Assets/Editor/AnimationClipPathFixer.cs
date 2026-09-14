using UnityEngine;
using UnityEditor;
using Bezi;

/// <summary>
/// Custom Bezi action that retargets animation clips: prefixes transform paths
/// in all curves of the given .anim assets via the AnimationUtility API only.
/// Used when an Animator is moved to a parent object and clip paths must
/// include the extra hierarchy segment.
/// </summary>
public static class AnimationClipPathFixer
{
    /// <summary>
    /// Rewrites transform paths in the given animation clips: every curve path
    /// gets 'prefix' prepended (e.g. "tophead" → "Scull/tophead").
    /// Uses AnimationUtility so curve data and the serialized binding constant
    /// (generic path hashes) stay consistent.
    /// </summary>
    [BeziAction(
        "Prepends a hierarchy prefix to all transform curve paths in the given animation clips " +
        "via the AnimationUtility API. Use when an Animator moved to a parent GameObject " +
        "and clip paths need the extra segment.",
        IsReadOnly = false
    )]
    public static string RetargetClipPaths(string clipPaths, string prefix)
    {
        if (string.IsNullOrEmpty(clipPaths))
            return "clipPaths is empty";

        var details = new System.Text.StringBuilder();
        int fixedClips = 0;

        foreach (var rawPath in clipPaths.Split(';'))
        {
            var clipPath = rawPath.Trim();
            if (string.IsNullOrEmpty(clipPath)) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                details.AppendLine($"NOT FOUND: {clipPath}");
                continue;
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            int changedCurves = 0;

            foreach (var b in bindings)
            {
                if (string.IsNullOrEmpty(b.path) || b.path.StartsWith(prefix + "/"))
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, b);

                // Remove the old binding, then add the retargeted one.
                AnimationUtility.SetEditorCurve(clip, b, null);
                var rebased = b;
                rebased.path = prefix + "/" + b.path;
                AnimationUtility.SetEditorCurve(clip, rebased, curve);

                changedCurves++;
                details.AppendLine($"{clip.name}: '{b.path}' -> '{rebased.path}' [{b.propertyName}]");
            }

            if (changedCurves > 0)
            {
                EditorUtility.SetDirty(clip);
                fixedClips++;
            }
        }

        AssetDatabase.SaveAssets();
        return $"Retargeted {fixedClips} clips:\n{details}";
    }
}
