using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Settings window for managing folder colors and shortcut slot assignments.
/// Open via Tools > Folder Highlighter > Settings.
/// </summary>
public class FolderHighlighterWindow : EditorWindow
{
    private const int MAX_SLOTS = 10;

    private Vector2 _colorsScroll;
    private Vector2 _slotsScroll;
    private Dictionary<string, Color> _colorCache;
    private string[] _slotCache;


    [MenuItem("Tools/Folder Highlighter/Settings")]
    private static void Open()
    {
        var window = GetWindow<FolderHighlighterWindow>("Folder Highlighter");
        window.minSize = new Vector2(400, 300);
    }

    private void OnEnable() => RefreshCache();
    private void OnFocus() => RefreshCache();

    private void RefreshCache()
    {
        _colorCache = FolderHighlighter.GetAllColors();
        _slotCache = FolderHighlighter.GetAllSlots();
    }

    private void OnGUI()
    {
        GUILayout.Space(5);
        DrawColorsSection();
        GUILayout.Space(15);
        DrawSlotsSection();
        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear All Colors", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog("Clear All Colors",
                "Remove color assignments from all folders?", "Yes", "Cancel"))
            {
                FolderHighlighter.ClearAllColors();
                RefreshCache();
            }
        }
        if (GUILayout.Button("Clear All Slots", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog("Clear All Slots",
                "Remove all shortcut slot assignments?", "Yes", "Cancel"))
            {
                FolderHighlighter.ClearAllSlots();
                RefreshCache();
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);
        if (GUILayout.Button("Refresh", GUILayout.Height(20)))
            RefreshCache();
    }

    private void DrawColorsSection()
    {
        GUILayout.Label("Folder Colors", EditorStyles.boldLabel);

        if (_colorCache == null || _colorCache.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No folders colored yet.\n" +
                "Right-click a folder in the Project window and select\n" +
                "'Folder Highlighter > Set Color...'",
                MessageType.Info);
            return;
        }

        _colorsScroll = EditorGUILayout.BeginScrollView(_colorsScroll, GUILayout.MaxHeight(200));

        foreach (var entry in _colorCache)
        {
            string path = AssetDatabase.GUIDToAssetPath(entry.Key);
            if (string.IsNullOrEmpty(path))
                path = "(missing folder)";

            EditorGUILayout.BeginHorizontal("box");

            Color newColor = EditorGUILayout.ColorField(entry.Value, GUILayout.Width(50));
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));

            if (newColor != entry.Value)
                FolderHighlighter.SetColor(entry.Key, newColor);

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                FolderHighlighter.RemoveColor(entry.Key);
                RefreshCache();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSlotsSection()
    {
        GUILayout.Label("Shortcut Slots", EditorStyles.boldLabel);
        GUILayout.Space(5);

        if (_slotCache == null)
            return;

        _slotsScroll = EditorGUILayout.BeginScrollView(_slotsScroll, GUILayout.MaxHeight(260));

        for (int i = 0; i < _slotCache.Length; i++)
        {
            EditorGUILayout.BeginHorizontal("box");

            // Key binding from ShortcutManager
            string shortcutId = i < 9 ? $"FolderJump/{i + 1}" : "FolderJump/0";
            string keyBinding = GetKeyBindingDisplay(shortcutId);

            EditorGUILayout.LabelField(keyBinding, EditorStyles.boldLabel, GUILayout.Width(80));

            string guid = _slotCache[i];
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    EditorGUILayout.LabelField("(folder missing)", EditorStyles.miniLabel);
                }
                else
                {
                    string folderName = System.IO.Path.GetFileName(path.TrimEnd('/'));
                    var labelStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                    EditorGUILayout.LabelField(folderName, labelStyle, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Go", GUILayout.Width(35)))
                        FolderHighlighter.NavigateToSlot(i);
                }

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    FolderHighlighter.ClearSlot(i);
                    RefreshCache();
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                EditorGUILayout.LabelField("(empty)", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(5);
        EditorGUILayout.LabelField(
            "Reassign keys: Edit > Shortcuts > category \"FolderJump\"",
            EditorStyles.miniLabel);
    }

    /// <summary>Reads the actual key binding for a shortcut ID from the ShortcutManager.</summary>
    private static string GetKeyBindingDisplay(string shortcutId)
    {
        try
        {
            var binding = ShortcutManager.instance.GetShortcutBinding(shortcutId);
            var sb = new StringBuilder();
            foreach (var combo in binding.keyCombinationSequence)
            {
                if (sb.Length > 0) sb.Append(" ");
                if ((combo.modifiers & ShortcutModifiers.Alt) != 0) sb.Append("Alt+");
                if ((combo.modifiers & ShortcutModifiers.Control) != 0) sb.Append("Ctrl+");
                if ((combo.modifiers & ShortcutModifiers.Shift) != 0) sb.Append("Shift+");
                sb.Append(combo.keyCode);
            }
            return sb.Length > 0 ? sb.ToString() : "—";
        }
        catch
        {
            return "—";
        }
    }
}
