using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility to find and remove all missing script components from the loaded scene.
/// Run via Tools → Remove Missing Scripts.
/// </summary>
public static class MissingScriptCleaner
{
    [MenuItem("Tools/Remove Missing Scripts")]
    public static void RemoveMissingScripts()
    {
        int removedTotal = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                removedTotal += ProcessGameObject(root);
        }

        if (removedTotal > 0)
        {
            Debug.Log($"[MissingScriptCleaner] Removed {removedTotal} missing script(s).");
            EditorApplication.ExecuteMenuItem("File/Save");
        }
        else
        {
            Debug.Log("[MissingScriptCleaner] No missing scripts found.");
        }
    }

    private static int ProcessGameObject(GameObject go)
    {
        int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        foreach (Transform child in go.transform)
            count += ProcessGameObject(child.gameObject);

        return count;
    }
}
