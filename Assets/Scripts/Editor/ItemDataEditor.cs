using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for ItemData.
/// Embeds an interactive 3D preview of the inspectionPrefab directly in the Inspector.
/// Drag inside the preview to rotate the model — the resulting angles are saved
/// automatically to previewRotation and useCustomPreviewRotation is enabled.
/// </summary>
[CustomEditor(typeof(ItemData))]
public class ItemDataEditor : Editor
{
    // ── Preview state ─────────────────────────────────────────────────────────

    private PreviewRenderUtility _preview;
    private GameObject _previewPivot;
    private GameObject _previewInstance;
    private GameObject _currentPrefab;

    private bool _isDragging;
    private Vector3 _drag; // x = pitch, y = yaw, z = roll

    // ── Serialized properties ─────────────────────────────────────────────────

    private SerializedProperty _useCustomProp;
    private SerializedProperty _rotationProp;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const float PreviewHeight   = 210f;
    private const float DragSensitivity = 0.4f;
    private const float CameraFov       = 30f;
    private const float CameraPadding   = 1.35f; // breathing room multiplier

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _useCustomProp = serializedObject.FindProperty("useCustomPreviewRotation");
        _rotationProp  = serializedObject.FindProperty("previewRotation");

        // Seed drag from whatever is already saved so the preview opens at the correct angle.
        SeedDragFromSaved();
    }

    private void OnDisable() => CleanupPreview();

    // ── Inspector GUI ─────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var item = (ItemData)target;

        if (item.inspectionPrefab == null)
        {
            CleanupPreview();
            serializedObject.ApplyModifiedProperties();
            return;
        }

        // Rebuild when the prefab reference changes.
        if (_currentPrefab != item.inspectionPrefab)
            BuildPreview(item.inspectionPrefab);

        if (_preview == null || _previewPivot == null)
        {
            EditorGUILayout.HelpBox("Preview could not be created for this prefab.", MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Drag: X/Y rotation  |  Shift + Drag: Z rotation", EditorStyles.centeredGreyMiniLabel);

        var previewRect = GUILayoutUtility.GetRect(1f, PreviewHeight);

        HandleDragInput(previewRect);

        // Apply current drag angles to the pivot so the camera sees the updated pose.
        _previewPivot.transform.rotation = Quaternion.Euler(_drag.x, _drag.y, _drag.z);

        if (Event.current.type == EventType.Repaint)
        {
            _preview.BeginPreview(previewRect, GUIStyle.none);
            _preview.camera.Render();
            _preview.EndAndDrawPreview(previewRect);
        }

        EditorGUILayout.HelpBox(
            "Drag  —  rotate X / Y\n" +
            "Shift + Drag  —  rotate Z\n" +
            "Values auto-save to Preview Rotation.",
            MessageType.None);

        serializedObject.ApplyModifiedProperties();
    }

    // ── Drag handling ─────────────────────────────────────────────────────────

    private void HandleDragInput(Rect previewRect)
    {
        var e = Event.current;

        switch (e.type)
        {
            case EventType.MouseDown when previewRect.Contains(e.mousePosition):
                _isDragging = true;
                e.Use();
                break;

            case EventType.MouseUp:
                _isDragging = false;
                break;

            case EventType.MouseDrag when _isDragging:
                if (e.shift)
                {
                    // Shift + horizontal drag → Z (roll)
                    _drag.z -= e.delta.x * DragSensitivity;
                }
                else
                {
                    // Free drag → X (pitch) and Y (yaw)
                    _drag.x = Mathf.Clamp(_drag.x + e.delta.y * DragSensitivity, -89f, 89f);
                    _drag.y -= e.delta.x * DragSensitivity;
                }

                _useCustomProp.boolValue   = true;
                _rotationProp.vector3Value = new Vector3(_drag.x, _drag.y, _drag.z);
                serializedObject.ApplyModifiedProperties();

                e.Use();
                Repaint();
                break;
        }
    }

    // ── Preview construction ──────────────────────────────────────────────────

    private void BuildPreview(GameObject prefab)
    {
        CleanupPreview();
        _currentPrefab = prefab;

        _preview = new PreviewRenderUtility();

        SetupCamera(_preview.camera);
        SetupLights();

        // Instantiate the prefab at world origin before parenting so we can measure bounds cleanly.
        _previewInstance          = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        _previewInstance.hideFlags = HideFlags.HideAndDontSave;

        var bounds = CalculateBounds(_previewInstance);

        // Create a pivot at the visual center of the model (world origin).
        // Parenting the instance to it and offsetting by -bounds.center ensures
        // the model's visual center aligns with the pivot, so rotation looks natural.
        _previewPivot          = new GameObject("PreviewPivot");
        _previewPivot.hideFlags = HideFlags.HideAndDontSave;

        _previewInstance.transform.SetParent(_previewPivot.transform);
        _previewInstance.transform.localPosition = -bounds.center;

        // AddSingleGO moves the pivot and the entire child hierarchy to the preview scene.
        _preview.AddSingleGO(_previewPivot);

        // Position the camera so the model fits in frame.
        float size     = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (size < 0.001f) size = 1f;

        float halfFovRad = CameraFov * 0.5f * Mathf.Deg2Rad;
        float distance   = size * 0.5f * CameraPadding / Mathf.Tan(halfFovRad);

        _preview.camera.transform.position = new Vector3(0f, 0f, -distance);
        _preview.camera.transform.LookAt(Vector3.zero);

        SeedDragFromSaved();
        _previewPivot.transform.rotation = Quaternion.Euler(_drag.x, _drag.y, _drag.z);
    }

    private void SetupCamera(Camera cam)
    {
        cam.fieldOfView   = CameraFov;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane  = 500f;
        cam.clearFlags    = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    }

    private void SetupLights()
    {
        _preview.lights[0].intensity = 1.4f;
        _preview.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
        _preview.lights[1].intensity = 0.6f;
        _preview.lights[1].transform.rotation = Quaternion.Euler(-20f, -20f, 0f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private void SeedDragFromSaved()
    {
        if (_rotationProp == null) return;
        _drag = _rotationProp.vector3Value;
    }

    private void CleanupPreview()
    {
        // Cleanup() destroys all GameObjects added via AddSingleGO (including children).
        _preview?.Cleanup();
        _preview         = null;
        _previewPivot    = null;
        _previewInstance = null;
        _currentPrefab   = null;
        _isDragging      = false;
    }
}
