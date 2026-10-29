using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Inspector of MaidApparition with an edit-mode preview of the two final poses
/// (hands on the body bag / hands on the glass). Nothing is saved: the preview is reverted
/// before saving, entering Play Mode and script reloads.
/// </summary>
[CustomEditor(typeof(MaidApparition))]
public class MaidApparitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var maid = (MaidApparition)target;

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Preview (edit mode)", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Preview works in edit mode only.", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Start pose")) maid.EditorPreview(MaidApparition.SpotId.Bag, true);
            if (GUILayout.Button("Preview: Bag")) maid.EditorPreview(MaidApparition.SpotId.Bag);
            if (GUILayout.Button("Preview: Glass")) maid.EditorPreview(MaidApparition.SpotId.Glass);
            using (new EditorGUI.DisabledScope(!maid.IsEditorPreviewing))
                if (GUILayout.Button("Reset pose")) maid.EditorResetPreview();
        }

        if (!maid.IsEditorPreviewing)
        {
            EditorGUILayout.HelpBox(
                "Preview shows the final pose (hands on the surface). Change the values above or the Hand Offsets and the pose updates live. " +
                "Bag preview: you can also move the MaidHolder instance - her position is kept.", MessageType.None);
            return;
        }

        if (changed)
            maid.EditorPreview(maid.EditorPreviewSpot, maid.EditorPreviewStart);

        maid.EditorHandGaps(out float left, out float right);
        EditorGUILayout.HelpBox(
            $"Spot: {maid.EditorPreviewSpot}    lean: {maid.EditorLean:0}°\n" +
            $"Gap hand→target:  left {FormatGap(left)},  right {FormatGap(right)}\n" +
            "Yellow = left hand target, magenta = right (Scene view gizmos). A gap above ~2 cm means she cannot reach: " +
            "raise Max Lean, or move her closer.",
            MessageType.Info);
        Repaint();
    }

    private static string FormatGap(float metres) => metres < 0f ? "-" : $"{metres * 100f:0.0} cm";
}

/// <summary>Reverts every active maid preview before anything could save the posed bones.</summary>
[InitializeOnLoad]
public static class MaidPreviewGuard
{
    private static double _last;

    static MaidPreviewGuard()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ResetAll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) ResetAll();
        };
        EditorSceneManager.sceneSaving += (scene, path) => ResetAll();
        PrefabStage.prefabSaving += go => ResetAll();
        EditorApplication.quitting += ResetAll;
        EditorApplication.update += Tick;
    }

    private static void ResetAll()
    {
        var list = MaidApparition.EditorPreviews;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] != null) list[i].EditorResetPreview();
        list.Clear();
    }

    /// <summary>Keeps the pose in sync with anchors / hand targets moved in the Scene view.</summary>
    private static void Tick()
    {
        var list = MaidApparition.EditorPreviews;
        if (list.Count == 0 || Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup - _last < 0.1) return;
        _last = EditorApplication.timeSinceStartup;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null) { list.RemoveAt(i); continue; }
            list[i].EditorPreview(list[i].EditorPreviewSpot, list[i].EditorPreviewStart);
        }
        SceneView.RepaintAll();
    }
}
