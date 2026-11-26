#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Unity Animation Hierarchy Editor
/// 
/// This utility will help you refactor your Unity animations.
/// Place the AnimationHierarchyEditor.cs file in YourProject/Editors/ folder to make it work. Then you'll be able to open Animation Hierarchy Editor window by clicking on Window > Animation Hierarchy Editor.
/// The editor will work if you select an animation clip.
/// 
/// Notes:
/// Added and updated by Chris Wingler for KMS 1.50.0
/// 
/// References:
/// https://github.com/s-m-k/Unity-Animation-Hierarchy-Editor
/// http://enemyhideout.com/2016/05/howto-remapping-the-animation-hierarchy-in-unity/
/// </summary>
public class AnimationHierarchyEditor : EditorWindow
{
    private int columnWidth;
    private const int minColumnWidth = 200;
    private const int labelColumnWidth = 200;
    private const int objColumnWidth = 200;
    private const int changeButtonWidth = 90;

    private Animator animatorObject;
    private List<AnimationClip> animationClips;
    private ArrayList pathsKeys;
    private Hashtable paths;

    private Dictionary<string, string> tempPathOverrides;

    private Vector2 animClipsScrollPos = Vector2.zero;
    private Vector2 referencesScrollPos = Vector2.zero;
    private float entryHeight = 18;

    private string sReplacementOldRoot;
    private string sReplacementNewRoot;
    private string sOriginalRoot = "Root";
    private string sNewRoot = "SomeNewObject/Root";

    [MenuItem("Window/Animation/Hierarchy Editor")]
    static void ShowWindow()
    {
        AnimationHierarchyEditor window = EditorWindow.GetWindow<AnimationHierarchyEditor>();
        window.OnSelectionChange();
    }

    public AnimationHierarchyEditor()
    {
        animationClips = new List<AnimationClip>();
        tempPathOverrides = new Dictionary<string, string>();
    }

    void OnSelectionChange()
    {
        if (Selection.objects.Length > 1)
        {
            Debug.Log("Length? " + Selection.objects.Length);
            animationClips.Clear();
            foreach (Object o in Selection.objects)
            {
                if (o is AnimationClip clip)
                    animationClips.Add(clip);
            }
        }
        else if (Selection.activeObject is AnimationClip clip)
        {
            animationClips.Clear();
            animationClips.Add(clip);
            FillModel();
        }
        else
        {
            animationClips.Clear();
        }

        this.Repaint();
    }

    void OnGUI()
    {
        this.titleContent = new GUIContent("Animation Hierarchy Editor");

        if (Event.current.type == EventType.ValidateCommand)
        {
            switch (Event.current.commandName)
            {
                case "UndoRedoPerformed":
                    FillModel();
                    break;
            }
        }

        if (animationClips.Count > 0)
        {

            float windowWidth = position.width;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Referenced Animator (Root)", GUILayout.Width(labelColumnWidth));
            animatorObject = ((Animator)EditorGUILayout.ObjectField(
                animatorObject,
                typeof(Animator),
                true,
                GUILayout.Width(minColumnWidth))
            );
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Animation Clip(s)", GUILayout.Width(labelColumnWidth));

            animClipsScrollPos = GUILayout.BeginScrollView(animClipsScrollPos);
            EditorGUILayout.BeginVertical();
            for (int i = 0; i < animationClips.Count; i++)
            {
                animationClips[i] = ((AnimationClip)EditorGUILayout.ObjectField(
                    animationClips[i],
                    typeof(AnimationClip),
                    true,
                    GUILayout.Width(minColumnWidth))
                );
            }
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();

            EditorGUILayout.EndHorizontal();

            columnWidth = (int)windowWidth - labelColumnWidth - 50;
            if (columnWidth < minColumnWidth)
            {
                columnWidth = minColumnWidth;
            }

            var rect = EditorGUILayout.BeginHorizontal();
            Handles.color = Color.gray;
            Handles.DrawLine(new Vector2(rect.x - 15, rect.y + 15), new Vector2(rect.width + 15, rect.y + 15));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(20);

            EditorGUILayout.LabelField("Update path prefix");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Old path", GUILayout.Width(labelColumnWidth));
            sOriginalRoot = EditorGUILayout.TextField(sOriginalRoot, GUILayout.Width(columnWidth));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("New path", GUILayout.Width(labelColumnWidth));
            EditorGUILayout.BeginVertical();
            sNewRoot = EditorGUILayout.TextField(sNewRoot, GUILayout.Width(columnWidth));
            if (GUILayout.Button("Change all occurrences", GUILayout.Width(columnWidth)))
            {
                Debug.Log("O: " + sOriginalRoot + " N: " + sNewRoot);
                ReplaceRoot(sOriginalRoot, sNewRoot);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(20);

            columnWidth = columnWidth - changeButtonWidth + 10;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Object", GUILayout.Width(objColumnWidth));
            GUILayout.Label("Reference count", GUILayout.Width(changeButtonWidth));
            GUILayout.Label("Reference path", GUILayout.Width(columnWidth));
            EditorGUILayout.EndHorizontal();

            if (paths != null)
            {
                float maxWidth = columnWidth - 10;
                foreach (string path in pathsKeys)
                {
                    var textDimensions = GUI.skin.label.CalcSize(new GUIContent(path));
                    maxWidth = textDimensions.x > maxWidth ? textDimensions.x : maxWidth;
                    if (!tempPathOverrides.ContainsKey(path))
                        tempPathOverrides[path] = path;
                }

                referencesScrollPos = GUILayout.BeginScrollView(referencesScrollPos);
                EditorGUILayout.BeginVertical();
                foreach (string path in pathsKeys)
                {
                    EditorGUILayout.BeginHorizontal(GUILayout.Height(entryHeight));

                    GUICreatePathItem(path);

                    string pathOverride = EditorGUILayout.TextField(tempPathOverrides[path], GUILayout.Width(maxWidth), GUILayout.Height(entryHeight));
                    if (!path.Equals(pathOverride))
                        tempPathOverrides[path] = pathOverride;
                    GUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                GUILayout.EndScrollView();
            }
            GUILayout.Space(40);
        }
        else
        {
            EditorGUILayout.HelpBox("Please select an Animation Clip", MessageType.Info);
        }
    }

    void GUICreatePathItem(string path)
    {
        string newPath = path;
        GameObject obj = FindObjectInRoot(path);
        GameObject newObj;
        ArrayList properties = (ArrayList)paths[path];
        int count = properties != null ? properties.Count : 0;

        string pathOverride = path;

        if (tempPathOverrides.ContainsKey(path)) pathOverride = tempPathOverrides[path];


        Color standardColor = GUI.color;

        if (obj != null)
        {
            GUI.color = Color.green;
        }
        else
        {
            GUI.color = Color.red;
        }

        newObj = (GameObject)EditorGUILayout.ObjectField(
            obj,
            typeof(GameObject),
            true,
            GUILayout.Width(objColumnWidth)
            );

        GUI.color = standardColor;

        GUI.enabled = pathOverride != path;
        if (GUILayout.Button($"Change ({count})", GUILayout.Width(changeButtonWidth)))
        {
            newPath = pathOverride;
            tempPathOverrides.Remove(path);
        }
        GUI.enabled = true;

        try
        {
            if (obj != newObj)
            {
                UpdatePath(path, ChildPath(newObj));
            }

            if (newPath != path)
            {
                UpdatePath(path, newPath);
            }
        }
        catch (UnityException ex)
        {
            Debug.LogError(ex.Message);
        }
    }

    void OnInspectorUpdate()
    {
        this.Repaint();
    }

    void FillModel()
    {
        paths = new Hashtable();
        pathsKeys = new ArrayList();

        foreach (AnimationClip animationClip in animationClips)
        {
            FillModelWithCurves(AnimationUtility.GetCurveBindings(animationClip));
            FillModelWithCurves(AnimationUtility.GetObjectReferenceCurveBindings(animationClip));
        }
    }

    private void FillModelWithCurves(EditorCurveBinding[] curves)
    {
        foreach (EditorCurveBinding curveData in curves)
        {
            string key = curveData.path;

            if (paths.ContainsKey(key))
            {
                ((ArrayList)paths[key]).Add(curveData);
            }
            else
            {
                ArrayList newProperties = new ArrayList();
                newProperties.Add(curveData);
                paths.Add(key, newProperties);
                pathsKeys.Add(key);
            }
        }
    }

    void ReplaceRoot(string oldRoot, string newRoot)
    {
        float fProgress = 0.0f;
        sReplacementOldRoot = oldRoot;
        sReplacementNewRoot = newRoot;

        AssetDatabase.StartAssetEditing();

        for (int iCurrentClip = 0; iCurrentClip < animationClips.Count; iCurrentClip++)
        {
            AnimationClip animationClip = animationClips[iCurrentClip];
            Undo.RecordObject(animationClip, "Animation Hierarchy Root Change");

            for (int iCurrentPath = 0; iCurrentPath < pathsKeys.Count; iCurrentPath++)
            {
                string path = pathsKeys[iCurrentPath] as string;
                ArrayList curves = (ArrayList)paths[path];

                for (int i = 0; i < curves.Count; i++)
                {
                    EditorCurveBinding binding = (EditorCurveBinding)curves[i];

                    if (path.StartsWith(sReplacementOldRoot))
                    {
                        string sNewPath = path.Remove(0, sReplacementOldRoot.Length);
                        sNewPath = sReplacementNewRoot + sNewPath;

                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, binding);
                        if (curve != null)
                        {
                            AnimationUtility.SetEditorCurve(animationClip, binding, null);
                            binding.path = sNewPath;
                            AnimationUtility.SetEditorCurve(animationClip, binding, curve);
                        }
                        else
                        {
                            ObjectReferenceKeyframe[] objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(animationClip, binding);
                            AnimationUtility.SetObjectReferenceCurve(animationClip, binding, null);
                            binding.path = sNewPath;
                            AnimationUtility.SetObjectReferenceCurve(animationClip, binding, objectReferenceCurve);
                        }
                    }
                }

                // Update the progress meter
                float fChunk = 1f / animationClips.Count;
                fProgress = (iCurrentClip * fChunk) + fChunk * ((float)iCurrentPath / (float)pathsKeys.Count);

                EditorUtility.DisplayProgressBar(
                    "Animation Hierarchy Progress",
                    "How far along the animation editing has progressed.",
                    fProgress);
            }

        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();

        FillModel();
        this.Repaint();
    }

    void UpdatePath(string oldPath, string newPath)
    {
        if (paths[newPath] != null)
        {
            throw new UnityException("Path " + newPath + " already exists in that animation!");
        }
        AssetDatabase.StartAssetEditing();
        for (int iCurrentClip = 0; iCurrentClip < animationClips.Count; iCurrentClip++)
        {
            AnimationClip animationClip = animationClips[iCurrentClip];
            Undo.RecordObject(animationClip, "Animation Hierarchy Change");

            //recreating all curves one by one
            //to maintain proper order in the editor - 
            //slower than just removing old curve
            //and adding a corrected one, but it's more
            //user-friendly
            for (int iCurrentPath = 0; iCurrentPath < pathsKeys.Count; iCurrentPath++)
            {
                string path = pathsKeys[iCurrentPath] as string;
                ArrayList curves = (ArrayList)paths[path];

                for (int i = 0; i < curves.Count; i++)
                {
                    EditorCurveBinding binding = (EditorCurveBinding)curves[i];
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, binding);
                    ObjectReferenceKeyframe[] objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(animationClip, binding);


                    if (curve != null)
                        AnimationUtility.SetEditorCurve(animationClip, binding, null);
                    else
                        AnimationUtility.SetObjectReferenceCurve(animationClip, binding, null);

                    if (path == oldPath)
                        binding.path = newPath;

                    if (curve != null)
                        AnimationUtility.SetEditorCurve(animationClip, binding, curve);
                    else
                        AnimationUtility.SetObjectReferenceCurve(animationClip, binding, objectReferenceCurve);

                    float fChunk = 1f / animationClips.Count;
                    float fProgress = (iCurrentClip * fChunk) + fChunk * ((float)iCurrentPath / (float)pathsKeys.Count);

                    EditorUtility.DisplayProgressBar(
                        "Animation Hierarchy Progress",
                        "How far along the animation editing has progressed.",
                        fProgress);
                }
            }
        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();
        FillModel();
        this.Repaint();
    }

    GameObject FindObjectInRoot(string path)
    {
        if (animatorObject == null)
        {
            return null;
        }

        Transform child = animatorObject.transform.Find(path);

        if (child != null)
        {
            return child.gameObject;
        }
        else
        {
            return null;
        }
    }

    string ChildPath(GameObject obj, bool sep = false)
    {
        if (animatorObject == null)
        {
            throw new UnityException("Please assign Referenced Animator (Root) first!");
        }

        if (obj == animatorObject.gameObject)
        {
            return "";
        }
        else
        {
            if (obj.transform.parent == null)
            {
                throw new UnityException("Object must belong to " + animatorObject.ToString() + "!");
            }
            else
            {
                return ChildPath(obj.transform.parent.gameObject, true) + obj.name + (sep ? "/" : "");
            }
        }
    }
}

#endif //UNITY_EDITOR