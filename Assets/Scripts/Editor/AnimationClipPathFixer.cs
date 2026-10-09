using System.Linq;
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

    /// <summary>
    /// Extracts the 'mixamo.com' AnimationClip sub-asset from each Mixamo FBX model
    /// into a standalone .anim asset and retargets its curve paths with 'prefix'
    /// (e.g. "maidMain/Armature") so the clip can play on a different Animator hierarchy.
    /// </summary>
    [BeziAction(
        "Extracts the 'mixamo.com' AnimationClip sub-asset from each Mixamo FBX model into a " +
        "standalone .anim asset and prepends a hierarchy prefix to its curve paths so the clip " +
        "plays on a different Animator hierarchy. jobs format: 'sourceFbx|destAnim|loop' entries " +
        "separated by ';' where loop is 1 or 0.",
        IsReadOnly = false
    )]
    public static string ExtractAndRetargetMixamoClips(string jobs, string prefix)
    {
        if (string.IsNullOrEmpty(jobs))
            return "jobs is empty";

        var details = new System.Text.StringBuilder();
        int extracted = 0;

        foreach (var rawJob in jobs.Split(';'))
        {
            var job = rawJob.Trim();
            if (string.IsNullOrEmpty(job)) continue;

            var parts = job.Split('|');
            if (parts.Length < 2)
            {
                details.AppendLine($"BAD JOB (need 'sourceFbx|destAnim|loop'): {job}");
                continue;
            }

            var sourceFbx = parts[0].Trim();
            if (sourceFbx.StartsWith("/")) sourceFbx = sourceFbx.TrimStart('/');
            var destAnim = parts[1].Trim();
            if (destAnim.StartsWith("/")) destAnim = destAnim.TrimStart('/');
            var loopTime = parts.Length > 2 && parts[2].Trim() == "1";

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(sourceFbx);
            var sourceClip = allAssets.OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "mixamo.com" && !c.name.StartsWith("__preview__"));
            if (sourceClip == null)
            {
                details.AppendLine($"CLIP NOT FOUND in {sourceFbx} (sub-asset 'mixamo.com'). Found: {string.Join(", ", allAssets.OfType<AnimationClip>().Select(c => c.name))}");
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destAnim) != null)
                AssetDatabase.DeleteAsset(destAnim);

            var clip = Object.Instantiate(sourceClip);
            clip.name = System.IO.Path.GetFileNameWithoutExtension(destAnim);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, destAnim);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            int changedCurves = 0;
            foreach (var b in bindings)
            {
                if (string.IsNullOrEmpty(b.path) || b.path.StartsWith(prefix + "/"))
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, b);
                AnimationUtility.SetEditorCurve(clip, b, null);
                var rebased = b;
                // Source Mixamo FBX paths start with one or two variable root segments
                // ("Armature", "ParentNode", possibly "Armature/Armature") above the
                // "mixamorig:" skeleton. Cut everything above the first "mixamorig:" bone
                // and rebase onto the prefix ("maidMain/Armature").
                var sourcePath = b.path;
                var mixamoIndex = sourcePath.IndexOf("mixamorig:");
                var restPath = mixamoIndex > 0 ? sourcePath.Substring(mixamoIndex) : "";
                rebased.path = restPath.Length > 0 ? prefix + "/" + restPath : prefix;
                AnimationUtility.SetEditorCurve(clip, rebased, curve);
                changedCurves++;
            }

            EditorUtility.SetDirty(clip);
            extracted++;
            details.AppendLine($"{destAnim}: {bindings.Length} bindings, {changedCurves} retargeted with prefix '{prefix}', loop={loopTime}");
        }

        AssetDatabase.SaveAssets();
        return $"Extracted {extracted} clips:\n{details}";
    }

    /// <summary>
    /// Returns the distinct transform curve paths of the given .anim clip for diagnostics.
    /// </summary>
    [BeziAction(
        "Returns the distinct curve binding paths of the given animation clip asset, " +
        "one per line. Read-only diagnostics.",
        IsReadOnly = true
    )]
    public static string DumpClipBindings(string clipPath)
    {
        AnimationClip clip;
        var subIndex = clipPath.IndexOf('|');
        if (subIndex > 0)
        {
            var fbxPath = clipPath.Substring(0, subIndex).Trim();
            var subName = clipPath.Substring(subIndex + 1).Trim();
            if (fbxPath.StartsWith("/")) fbxPath = fbxPath.TrimStart('/');
            clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == subName);
        }
        else
        {
            if (clipPath.StartsWith("/")) clipPath = clipPath.TrimStart('/');
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        }
        if (clip == null) return $"NOT FOUND: {clipPath}";

        var bindings = AnimationUtility.GetCurveBindings(clip);
        var paths = bindings.Select(b => b.path).Distinct().ToList();
        return $"clip '{clip.name}': {bindings.Length} bindings, {paths.Count} distinct paths\n" + string.Join("\n", paths.Take(30));
    }

    /// <summary>
    /// Reports the first hips-rotation keys of each given .anim clip, verifying
    /// the clips carry distinct animation data bound to the rig.
    /// clipPaths separated by ';'. Read-only diagnostics.
    /// </summary>
    [BeziAction(
        "Returns the first rotation keys of the 'mixamorig:Hips' curve for each of the given " +
        ".anim clips to verify they carry distinct animation data. " +
        "jobs format: 'prefabPath|clip1;clip2;...'. Read-only diagnostics.",
        IsReadOnly = true
    )]
    public static string SampleClipsOnPrefab(string jobs)
    {
        var parts = jobs.Split('|');
        if (parts.Length < 2) return "BAD JOB";
        var prefabPath = parts[0].Trim();
        if (prefabPath.StartsWith("/")) prefabPath = prefabPath.TrimStart('/');

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return $"PREFAB NOT FOUND: {prefabPath}";

        var instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var hips = instance.GetComponentsInChildren<Transform>()
                .FirstOrDefault(t => t.name == "mixamorig:Hips");
            if (hips == null) return "HIPS NOT FOUND";

            var report = new System.Text.StringBuilder();
            foreach (var raw in parts[1].Split(';'))
            {
                var clipPath = raw.Trim();
                if (clipPath.StartsWith("/")) clipPath = clipPath.TrimStart('/');
                if (string.IsNullOrEmpty(clipPath)) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) { report.AppendLine($"{clipPath}: NOT FOUND"); continue; }

                var binding = AnimationUtility.GetCurveBindings(clip)
                    .FirstOrDefault(b => b.path.EndsWith("mixamorig:Hips") && b.propertyName == "m_LocalRotation.x");
                if (binding.propertyName == null)
                {
                    report.AppendLine($"{clip.name}: no hips rotation binding");
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = curve.keys.Take(3).Select(k => $"t={k.time:F2}v={k.value:F3}");
                report.AppendLine($"{clip.name}: hipsRotX keys [{string.Join(", ", keys)}]");
            }
            return report.ToString();
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Samples each given .anim clip onto a live scene object with an Animator,
    /// exactly like the Animation window preview does, and reports the hips world
    /// pose afterwards. Read-only diagnostics.
    /// </summary>
    [BeziAction(
        "Samples each given .anim clip onto the live scene object (Animation window preview mechanism) " +
        "and reports the 'mixamorig:Hips' world pose after sampling. jobs format: " +
        "'sceneObjectFullPath|clip1;clip2;...'. Read-only diagnostics.",
        IsReadOnly = true
    )]
    public static string SampleClipsOnSceneObject(string jobs)
    {
        var parts = jobs.Split('|');
        if (parts.Length < 2) return "BAD JOB";
        var objectPath = parts[0].Trim();

        var go = GameObject.Find(objectPath.TrimStart('/'));
        if (go == null) return $"OBJECT NOT FOUND: {objectPath}";

        var hips = go.GetComponentsInChildren<Transform>()
            .FirstOrDefault(t => t.name == "mixamorig:Hips");
        if (hips == null) return "HIPS NOT FOUND";

        var report = new System.Text.StringBuilder();
        foreach (var raw in parts[1].Split(';'))
        {
            var clipPath = raw.Trim();
            if (clipPath.StartsWith("/")) clipPath = clipPath.TrimStart('/');
            if (string.IsNullOrEmpty(clipPath)) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { report.AppendLine($"{clipPath}: NOT FOUND"); continue; }

            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.SampleAnimationClip(go, clip, clip.length * 0.5f);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            var wp = hips.position;
            var we = hips.rotation.eulerAngles;
            report.AppendLine($"{clip.name}: hips worldPos=({wp.x:F2}, {wp.y:F2}, {wp.z:F2}) worldEuler=({we.x:F0}, {we.y:F0}, {we.z:F0})");
        }
        return report.ToString();
    }

    /// <summary>
    /// Forces Unity to reimport the given assets and resave the given controller so
    /// dangling clip references (deleted/recreated .anim files) are re-linked.
    /// Use when the Animation window shows 'No Clip' after clips were rewritten on disk.
    /// </summary>
    [BeziAction(
        "Reimports the given asset paths (semicolon-separated, relative to project root) and " +
        "resaves the given AnimatorController asset so its states re-resolve motion references " +
        "after the referenced .anim clips were deleted and recreated on disk.",
        IsReadOnly = false
    )]
    public static string RefreshAndRelinkController(string assetPaths, string controllerPath)
    {
        foreach (var raw in assetPaths.Split(';'))
        {
            var path = raw.Trim();
            if (path.StartsWith("/")) path = path.TrimStart('/');
            if (string.IsNullOrEmpty(path)) continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        if (controllerPath.StartsWith("/")) controllerPath = controllerPath.TrimStart('/');
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
        if (controller == null) return $"CONTROLLER NOT FOUND: {controllerPath}";

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var report = new System.Text.StringBuilder($"controller '{controller.name}':\n");
        foreach (var layer in controller.layers)
            foreach (var state in layer.stateMachine.states)
                report.AppendLine($"  {state.state.name} => {(state.state.motion != null ? state.state.motion.name : "NULL")}");
        return report.ToString();
    }

    /// <summary>
    /// Samples one clip onto the live scene object and KEEPS Animation Mode on,
    /// so a subsequent Scene View capture shows the posed model. Diagnostics.
    /// </summary>
    [BeziAction(
        "Samples the given .anim clip onto the live scene object at the given time and keeps " +
        "Animation Mode on (call StopAnimationPreviewMode afterwards). Returns the hips world pose.",
        IsReadOnly = false
    )]
    public static string SampleClipKeepMode(string objectPath, string clipPath, float time)
    {
        var go = GameObject.Find(objectPath.TrimStart('/'));
        if (go == null) return $"OBJECT NOT FOUND: {objectPath}";

        var hips = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "mixamorig:Hips");
        if (hips == null) return "HIPS NOT FOUND";

        var p = clipPath.Trim();
        if (p.StartsWith("/")) p = p.TrimStart('/');
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (clip == null) return $"CLIP NOT FOUND: {clipPath}";

        AnimationMode.StartAnimationMode();
        AnimationMode.SampleAnimationClip(go, clip, time);

        var wp = hips.position;
        var we = hips.rotation.eulerAngles;
        return $"{clip.name}@{time:F2}s hips worldPos=({wp.x:F2}, {wp.y:F2}, {wp.z:F2}) worldEuler=({we.x:F0}, {we.y:F0}, {we.z:F0})";
    }

    /// <summary>
    /// Leaves Animation Mode, restoring the scene pose. Diagnostics helper.
    /// </summary>
    [BeziAction("Exits Animation Mode, restoring the scene pose after SampleClipKeepMode.", IsReadOnly = false)]
    public static string StopAnimationPreviewMode()
    {
        AnimationMode.StopAnimationMode();
        return "Animation Mode stopped";
    }

    /// <summary>
    /// Builds a Generic avatar from the LIVE scene object hierarchy with the skeleton root
    /// at the given path relative to the Animator root, and saves it as an .asset.
    /// Needed when the Animator sits above the model root (e.g. MaidHolder > maidMain),
    /// because a ModelImporter avatar rooted at the model root breaks curve binding.
    /// </summary>
    [BeziAction(
        "Builds a Generic Avatar from the live scene object hierarchy using AvatarBuilder.BuildGenericAvatar " +
        "with the given root bone path relative to the Animator root, and saves it to assetPath. " +
        "Fixes 'model stuck in T-pose' when the Animator root is above the model root.",
        IsReadOnly = false
    )]
    public static string BuildGenericAvatarAsset(string objectPath, string rootBonePath, string assetPath)
    {
        var go = GameObject.Find(objectPath.TrimStart('/'));
        if (go == null) return $"OBJECT NOT FOUND: {objectPath}";

        var rootBone = rootBonePath.Trim();
        if (rootBone.StartsWith("/")) rootBone = rootBone.TrimStart('/');
        if (go.transform.Find(rootBone) == null) return $"ROOT BONE NOT FOUND: {rootBone}";

        var avatar = AvatarBuilder.BuildGenericAvatar(go, rootBone);
        if (avatar == null) return "AVATAR BUILD FAILED";
        avatar.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        var savePath = assetPath.Trim();
        if (savePath.StartsWith("/")) savePath = savePath.TrimStart('/');
        AssetDatabase.DeleteAsset(savePath);
        AssetDatabase.CreateAsset(avatar, savePath);
        AssetDatabase.SaveAssets();
        return $"Avatar '{avatar.name}' saved to {savePath}";
    }
}
