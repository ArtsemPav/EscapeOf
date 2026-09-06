using UnityEngine;
using UnityEditor;
using Bezi;

/// <summary>
/// Custom Bezi action that sets the Center and Size of an OcclusionPortal
/// via SerializedObject, because these properties are Inspector-only and
/// not exposed in the Unity scripting API.
/// </summary>
public static class OcclusionPortalConfigurator
{
    private const float DEFAULT_PORTAL_WIDTH = 2f;
    private const float DEFAULT_PORTAL_HEIGHT = 3f;
    private const float DEFAULT_PORTAL_DEPTH = 1f;

    /// <summary>
    /// Sets the Center and Size of the OcclusionPortal on the given GameObject.
    /// The portal's bounding box should cover the entire door opening so baked
    /// occlusion culling does not hide geometry visible through the doorway.
    /// </summary>
    [BeziAction(
        "Sets the Center and Size of the OcclusionPortal component on a GameObject. " +
        "Use this to cover the full door opening so baked occlusion culling does not " +
        "cull geometry visible through the doorway.",
        IsReadOnly = false
    )]
    public static string SetOcclusionPortalSize(
        string gameObjectPath,
        float centerX = 0f,
        float centerY = 0f,
        float centerZ = 0f,
        float sizeX = DEFAULT_PORTAL_WIDTH,
        float sizeY = DEFAULT_PORTAL_HEIGHT,
        float sizeZ = DEFAULT_PORTAL_DEPTH
    )
    {
        var go = GameObject.Find(gameObjectPath);
        if (go == null)
            return $"GameObject not found: {gameObjectPath}";

        var portal = go.GetComponent<OcclusionPortal>();
        if (portal == null)
            return $"OcclusionPortal not found on: {gameObjectPath}";

        var serializedObject = new SerializedObject(portal);
        var centerProp = serializedObject.FindProperty("m_Center");
        var sizeProp = serializedObject.FindProperty("m_Size");

        if (centerProp != null)
        {
            centerProp.vector3Value = new Vector3(centerX, centerY, centerZ);
        }

        if (sizeProp != null)
        {
            sizeProp.vector3Value = new Vector3(sizeX, sizeY, sizeZ);
        }

        serializedObject.ApplyModifiedProperties();

        return $"OcclusionPortal on '{gameObjectPath}' set: " +
               $"center=({centerX}, {centerY}, {centerZ}), " +
               $"size=({sizeX}, {sizeY}, {sizeZ})";
    }
}
