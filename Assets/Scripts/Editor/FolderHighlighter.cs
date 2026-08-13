using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Editor tool for coloring folders in the Project window and navigating
/// to bookmarked folders via keyboard shortcuts.
/// </summary>
public class FolderHighlighter
{
    private const string PREF_COLORS = "FolderHighlighter.Colors";
    private const string PREF_SLOTS = "FolderHighlighter.Slots";
    private const int MAX_SLOTS = 10;
    private const float FILL_ALPHA_LIST = 0.35f;
    private const float FILL_ALPHA_GRID = 0.20f;
    private const float BAR_ALPHA = 0.9f;
    private const int BAR_WIDTH = 3;

    private static readonly Color[] PRESET_COLORS =
    {
        new Color(1f, 0.35f, 0.35f, 1f),
        new Color(1f, 0.6f, 0.25f, 1f),
        new Color(1f, 0.9f, 0.3f, 1f),
        new Color(0.3f, 0.85f, 0.4f, 1f),
        new Color(0.3f, 0.8f, 0.95f, 1f),
        new Color(0.4f, 0.55f, 1f, 1f),
        new Color(0.7f, 0.4f, 1f, 1f),
        new Color(1f, 0.4f, 0.75f, 1f),
        new Color(0.65f, 0.65f, 0.65f, 1f),
    };

    private static readonly string[] PRESET_NAMES =
    {
        "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Gray"
    };

    private static Dictionary<string, Color> _folderColors;
    private static string[] _shortcutSlots;


    #region --- Data Types ---

    [Serializable]
    private class ColorEntry
    {
        public string guid;
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [Serializable]
    private class ColorList
    {
        public List<ColorEntry> entries = new List<ColorEntry>();
    }

    [Serializable]
    private class SlotData
    {
        public string[] guids;
    }

    #endregion


    #region --- Initialization ---

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        LoadData();
        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    #endregion


    #region --- Data Persistence ---

    private static void LoadData()
    {
        _folderColors = new Dictionary<string, Color>();

        string colorJson = EditorPrefs.GetString(PREF_COLORS, "");
        if (!string.IsNullOrEmpty(colorJson))
        {
            try
            {
                var list = JsonUtility.FromJson<ColorList>(colorJson);
                if (list != null)
                {
                    foreach (var entry in list.entries)
                    {
                        _folderColors[entry.guid] = new Color(entry.r, entry.g, entry.b, entry.a);
                    }
                }
            }
            catch { }
        }

        _shortcutSlots = new string[MAX_SLOTS];
        string slotJson = EditorPrefs.GetString(PREF_SLOTS, "");
        if (!string.IsNullOrEmpty(slotJson))
        {
            try
            {
                var slots = JsonUtility.FromJson<SlotData>(slotJson);
                if (slots != null && slots.guids != null)
                {
                    for (int i = 0; i < Math.Min(slots.guids.Length, MAX_SLOTS); i++)
                    {
                        _shortcutSlots[i] = slots.guids[i] ?? "";
                    }
                }
            }
            catch { }
        }
    }

    private static void SaveColors()
    {
        var list = new ColorList();
        foreach (var kvp in _folderColors)
        {
            list.entries.Add(new ColorEntry
            {
                guid = kvp.Key,
                r = kvp.Value.r,
                g = kvp.Value.g,
                b = kvp.Value.b,
                a = kvp.Value.a
            });
        }
        EditorPrefs.SetString(PREF_COLORS, JsonUtility.ToJson(list));
    }

    private static void SaveSlots()
    {
        var data = new SlotData { guids = _shortcutSlots };
        EditorPrefs.SetString(PREF_SLOTS, JsonUtility.ToJson(data));
    }

    #endregion


    #region --- Project Window Drawing ---

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        if (_folderColors == null)
            LoadData();

        if (_folderColors == null || !_folderColors.TryGetValue(guid, out var color))
            return;

        bool isListMode = selectionRect.height <= 32f;
        float alpha = isListMode ? FILL_ALPHA_LIST : FILL_ALPHA_GRID;

        var oldColor = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, alpha);
        GUI.DrawTexture(selectionRect, EditorGUIUtility.whiteTexture);

        if (isListMode)
        {
            GUI.color = new Color(color.r, color.g, color.b, BAR_ALPHA);
            var barRect = new Rect(selectionRect.x, selectionRect.y, BAR_WIDTH, selectionRect.height);
            GUI.DrawTexture(barRect, EditorGUIUtility.whiteTexture);
        }

        GUI.color = oldColor;
    }

    #endregion


    #region --- Context Menu: Color ---

    [MenuItem("Assets/Folder Highlighter/Set Color...", false, 20)]
    private static void SetColorMenu()
    {
        string guid = GetSelectedFolderGuid();
        if (string.IsNullOrEmpty(guid)) return;

        Color current = _folderColors.TryGetValue(guid, out var c) ? c : Color.white;
        ColorPickerWindow.Show(guid, current, ApplyColor);
    }

    [MenuItem("Assets/Folder Highlighter/Set Color...", true)]
    private static bool SetColorValidate() => IsFolderSelected();

    /// <summary>Applies a color to a folder by GUID and persists it.</summary>
    private static void ApplyColor(string guid, Color color)
    {
        _folderColors[guid] = color;
        SaveColors();
        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem("Assets/Folder Highlighter/Clear Color", false, 21)]
    private static void ClearColorMenu()
    {
        string guid = GetSelectedFolderGuid();
        if (string.IsNullOrEmpty(guid)) return;

        _folderColors.Remove(guid);
        SaveColors();
        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem("Assets/Folder Highlighter/Clear Color", true)]
    private static bool ClearColorValidate()
    {
        if (_folderColors == null) LoadData();
        if (!IsFolderSelected()) return false;
        string guid = GetSelectedFolderGuid();
        return guid != null && _folderColors != null && _folderColors.ContainsKey(guid);
    }

    #endregion


    #region --- Context Menu: Shortcut Slots ---

    [MenuItem("Assets/Folder Highlighter/Assign to Shortcut...", false, 30)]
    private static void AssignSlotMenu()
    {
        string guid = GetSelectedFolderGuid();
        if (string.IsNullOrEmpty(guid)) return;
        SlotAssignWindow.Show(guid, _shortcutSlots, AssignToSlot);
    }

    [MenuItem("Assets/Folder Highlighter/Assign to Shortcut...", true)]
    private static bool AssignSlotValidate() => IsFolderSelected();

    #endregion


    #region --- Keyboard Shortcuts ---

    [Shortcut("FolderJump/1", KeyCode.Alpha1, ShortcutModifiers.Alt)]
    private static void GoToSlot0() => GoToSlot(0);

    [Shortcut("FolderJump/2", KeyCode.Alpha2, ShortcutModifiers.Alt)]
    private static void GoToSlot1() => GoToSlot(1);

    [Shortcut("FolderJump/3", KeyCode.Alpha3, ShortcutModifiers.Alt)]
    private static void GoToSlot2() => GoToSlot(2);

    [Shortcut("FolderJump/4", KeyCode.Alpha4, ShortcutModifiers.Alt)]
    private static void GoToSlot3() => GoToSlot(3);

    [Shortcut("FolderJump/5", KeyCode.Alpha5, ShortcutModifiers.Alt)]
    private static void GoToSlot4() => GoToSlot(4);

    [Shortcut("FolderJump/6", KeyCode.Alpha6, ShortcutModifiers.Alt)]
    private static void GoToSlot5() => GoToSlot(5);

    [Shortcut("FolderJump/7", KeyCode.Alpha7, ShortcutModifiers.Alt)]
    private static void GoToSlot6() => GoToSlot(6);

    [Shortcut("FolderJump/8", KeyCode.Alpha8, ShortcutModifiers.Alt)]
    private static void GoToSlot7() => GoToSlot(7);

    [Shortcut("FolderJump/9", KeyCode.Alpha9, ShortcutModifiers.Alt)]
    private static void GoToSlot8() => GoToSlot(8);

    [Shortcut("FolderJump/0", KeyCode.Alpha0, ShortcutModifiers.Alt)]
    private static void GoToSlot9() => GoToSlot(9);

    #endregion


    #region --- Helpers ---

    private static bool IsFolderSelected()
    {
        if (_folderColors == null) LoadData();

        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0) return false;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) return true;
        }
        return false;
    }

    private static string GetSelectedFolderGuid()
    {
        if (_folderColors == null) LoadData();

        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0) return null;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) return guid;
        }
        return null;
    }

    private static void AssignToSlot(int slot, string guid)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return;
        _shortcutSlots[slot] = guid ?? "";
        SaveSlots();
    }

    /// <summary>Selects the folder, focuses the Project window, and opens its contents.</summary>
    private static void GoToSlot(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return;
        string guid = _shortcutSlots[slot];
        if (string.IsNullOrEmpty(guid)) return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning($"[FolderHighlighter] Slot {slot + 1} points to a missing folder (GUID: {guid}).");
            return;
        }

        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (obj == null) return;

        EditorUtility.FocusProjectWindow();
        ShowFolderContentsInProjectWindow(obj);
    }

    /// <summary>Opens the folder in the Project window so its contents are visible.</summary>
    private static void ShowFolderContentsInProjectWindow(UnityEngine.Object folderObj)
    {
        var browserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
        if (browserType == null) return;

        var browsers = UnityEngine.Resources.FindObjectsOfTypeAll(browserType);
        if (browsers == null || browsers.Length == 0) return;

        int instanceID = folderObj.GetInstanceID();

        foreach (var browser in browsers)
        {
            EnsureTwoColumnMode(browserType, browser);

            bool invoked = false;
            var methods = browserType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "ShowFolderContents");

            foreach (var method in methods)
            {
                var p = method.GetParameters();
                if (TryInvokeShowFolderContents(method, browser, p, instanceID))
                {
                    invoked = true;
                    break;
                }
            }

            if (!invoked)
            {
                var setFolder = browserType.GetMethod("SetFolder",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);
                setFolder?.Invoke(browser, new object[] { instanceID });
            }

            return;
        }
    }

    /// <summary>Attempts to invoke a ShowFolderContents overload with the given instance ID.</summary>
    private static bool TryInvokeShowFolderContents(MethodInfo method, object browser, ParameterInfo[] p, int instanceID)
    {
        try
        {
            object idArg = ConvertInstanceIdParameter(p[0].ParameterType, instanceID);

            if (p.Length == 2 && p[1].ParameterType == typeof(bool))
                method.Invoke(browser, new object[] { idArg, false });
            else if (p.Length == 1)
                method.Invoke(browser, new object[] { idArg });
            else
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Converts an instance ID to the parameter type expected by ShowFolderContents.</summary>
    private static object ConvertInstanceIdParameter(Type paramType, int instanceID)
    {
        if (paramType == typeof(int))
            return instanceID;

        // Unity 6+ may use an EntityId struct instead of int
        var constructor = paramType.GetConstructor(new[] { typeof(int) });
        if (constructor != null)
            return constructor.Invoke(new object[] { instanceID });

        // Try implicit conversion from int
        var implicitOp = paramType.GetMethod("op_Implicit",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null);
        if (implicitOp != null)
            return implicitOp.Invoke(null, new object[] { instanceID });

        return instanceID;
    }

    /// <summary>Switches the Project browser to two-column mode if it isn't already.</summary>
    private static void EnsureTwoColumnMode(Type browserType, object browser)
    {
        var serializedObject = new SerializedObject((EditorWindow)browser);
        var viewModeProp = serializedObject.FindProperty("m_ViewMode");
        if (viewModeProp == null) return;

        // 1 = two-column mode
        if (viewModeProp.enumValueIndex != 1)
        {
            var setTwoColumns = browserType.GetMethod("SetTwoColumns",
                BindingFlags.Instance | BindingFlags.NonPublic);
            setTwoColumns?.Invoke(browser, null);
        }
    }

    #endregion


    #region --- Public Accessors (for Settings Window) ---

    /// <summary>Returns a copy of all folder color assignments.</summary>
    public static Dictionary<string, Color> GetAllColors() => new Dictionary<string, Color>(_folderColors);

    /// <summary>Sets or updates the color for a folder by GUID.</summary>
    public static void SetColor(string guid, Color color)
    {
        _folderColors[guid] = color;
        SaveColors();
        EditorApplication.RepaintProjectWindow();
    }

    /// <summary>Removes the color assignment for a folder by GUID.</summary>
    public static void RemoveColor(string guid)
    {
        _folderColors.Remove(guid);
        SaveColors();
        EditorApplication.RepaintProjectWindow();
    }

    /// <summary>Clears all folder color assignments.</summary>
    public static void ClearAllColors()
    {
        _folderColors.Clear();
        SaveColors();
        EditorApplication.RepaintProjectWindow();
    }

    /// <summary>Returns a copy of all shortcut slot assignments (GUIDs).</summary>
    public static string[] GetAllSlots() => (string[])_shortcutSlots.Clone();

    /// <summary>Clears the folder assignment for a specific slot.</summary>
    public static void ClearSlot(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return;
        _shortcutSlots[slot] = "";
        SaveSlots();
    }

    /// <summary>Clears all shortcut slot assignments.</summary>
    public static void ClearAllSlots()
    {
        for (int i = 0; i < MAX_SLOTS; i++)
            _shortcutSlots[i] = "";
        SaveSlots();
    }

    /// <summary>Navigates to the folder assigned to the given slot.</summary>
    public static void NavigateToSlot(int slot) => GoToSlot(slot);

    #endregion


    #region --- Color Picker Window ---

    private class ColorPickerWindow : EditorWindow
    {
        private string _guid;
        private Color _color;
        private Action<string, Color> _onApply;

        /// <summary>Shows the color picker utility window.</summary>
        public static void Show(string guid, Color currentColor, Action<string, Color> onApply)
        {
            var window = CreateInstance<ColorPickerWindow>();
            window._guid = guid;
            window._color = currentColor;
            window._onApply = onApply;
            window.titleContent = new GUIContent("Folder Color");
            window.minSize = new Vector2(240, 240);
            window.maxSize = new Vector2(240, 240);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Label("Preset Colors", EditorStyles.boldLabel);

            for (int i = 0; i < PRESET_COLORS.Length; i++)
            {
                if (i % 3 == 0) EditorGUILayout.BeginHorizontal();

                var oldColor = GUI.color;
                GUI.color = PRESET_COLORS[i];
                if (GUILayout.Button(PRESET_NAMES[i], GUILayout.Height(26)))
                {
                    _color = PRESET_COLORS[i];
                    _onApply?.Invoke(_guid, _color);
                    Close();
                }
                GUI.color = oldColor;

                if (i % 3 == 2 || i == PRESET_COLORS.Length - 1)
                    EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("Custom Color", EditorStyles.boldLabel);
            _color = EditorGUILayout.ColorField(_color);

            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                _onApply?.Invoke(_guid, _color);
                Close();
            }
            if (GUILayout.Button("Cancel"))
                Close();
            EditorGUILayout.EndHorizontal();
        }
    }

    #endregion


    #region --- Slot Assign Window ---

    private class SlotAssignWindow : EditorWindow
    {
        private string _guid;
        private string[] _slots;
        private Action<int, string> _onAssign;

        /// <summary>Shows the slot assignment utility window.</summary>
        public static void Show(string guid, string[] slots, Action<int, string> onAssign)
        {
            var window = CreateInstance<SlotAssignWindow>();
            window._guid = guid;
            window._slots = slots;
            window._onAssign = onAssign;
            window.titleContent = new GUIContent("Assign Shortcut");
            window.minSize = new Vector2(360, 400);
            window.maxSize = new Vector2(360, 400);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            string folderPath = AssetDatabase.GUIDToAssetPath(_guid);
            string folderName = System.IO.Path.GetFileName(folderPath.TrimEnd('/'));

            GUILayout.Label("Folder:", EditorStyles.boldLabel);
            GUILayout.Label(folderName, EditorStyles.boldLabel);
            GUILayout.Space(10);
            GUILayout.Label("Choose a slot (Alt+1 through Alt+0 to navigate):", EditorStyles.wordWrappedLabel);
            GUILayout.Space(5);

            for (int i = 0; i < MAX_SLOTS; i++)
            {
                EditorGUILayout.BeginHorizontal("box");

                string keyHint = i < 9 ? $"Alt+{i + 1}" : "Alt+0";
                EditorGUILayout.LabelField(keyHint, EditorStyles.boldLabel, GUILayout.Width(45));

                string existingGuid = _slots[i];
                if (!string.IsNullOrEmpty(existingGuid))
                {
                    if (existingGuid == _guid)
                    {
                        GUILayout.Label("(this folder)", EditorStyles.miniBoldLabel);
                    }
                    else
                    {
                        string path = AssetDatabase.GUIDToAssetPath(existingGuid);
                        if (string.IsNullOrEmpty(path))
                        {
                            GUILayout.Label("(missing)", EditorStyles.miniLabel);
                        }
                        else
                        {
                            string name = System.IO.Path.GetFileName(path.TrimEnd('/'));
                            GUILayout.Label(name, EditorStyles.miniLabel);
                        }
                    }
                }
                else
                {
                    GUILayout.Label("(empty)", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Assign", GUILayout.Width(55)))
                {
                    _onAssign?.Invoke(i, _guid);
                    _slots[i] = _guid;
                    Repaint();
                }

                if (!string.IsNullOrEmpty(existingGuid))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(45)))
                    {
                        _onAssign?.Invoke(i, null);
                        _slots[i] = "";
                        Repaint();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Close"))
                Close();
        }
    }

    #endregion
}
