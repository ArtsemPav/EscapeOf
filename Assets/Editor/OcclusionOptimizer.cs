using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class OcclusionOptimizer
{
    [MenuItem("Tools/Optimize Scene Occlusion Flags")]
    public static void Optimize()
    {
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int optimizedCount = 0;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
            
            // If the object is marked as Occluder Static
            if (flags.HasFlag(StaticEditorFlags.OccluderStatic))
            {
                Vector3 size = r.bounds.size;
                float volume = size.x * size.y * size.z;

                // If the object is too small (Volume < 0.125 cubic meters, e.g. 50x50x50 cm)
                if (volume < 0.125f)
                {
                    Undo.RecordObject(r.gameObject, "Optimize Occlusion Flags");
                    
                    // Remove OccluderStatic but keep/set OccludeeStatic
                    flags &= ~StaticEditorFlags.OccluderStatic;
                    flags |= StaticEditorFlags.OccludeeStatic;
                    
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                    optimizedCount++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Occlusion optimization complete! Removed Occluder Static flag from {optimizedCount} small objects.");
    }
}