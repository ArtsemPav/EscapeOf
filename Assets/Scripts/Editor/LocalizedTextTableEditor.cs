using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for LocalizedTextTable: searchable key/value list with
/// add/remove/sort buttons, an entry counter, and highlighting of duplicate
/// keys (red) and empty values (yellow). All edits go through
/// SerializedProperty, so Unity undo keeps working as usual.
/// </summary>
[CustomEditor(typeof(LocalizedTextTable))]
public class LocalizedTextTableEditor : Editor
{
    private const float KeyFieldWidth = 230f;
    private const float RemoveButtonWidth = 26f;
    private const float RowPadding = 34f;

    private static readonly Color DuplicateKeyColor = new Color(1f, 0.35f, 0.35f, 0.25f);
    private static readonly Color EmptyValueColor = new Color(1f, 0.85f, 0.3f, 0.25f);

    private SerializedProperty _entries;
    private string _search = "";
    private GUIStyle _wrapStyle;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        _entries = serializedObject.FindProperty("entries");

        DrawToolbar();
        DrawRows();

        serializedObject.ApplyModifiedProperties();
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.Space(2);
        _search = EditorGUILayout.TextField("Поиск (ключ/текст)", _search);

        int total = _entries.arraySize;
        int shown = 0;
        for (int i = 0; i < total; i++)
        {
            var entry = _entries.GetArrayElementAtIndex(i);
            if (MatchesSearch(entry.FindPropertyRelative("key").stringValue,
                              entry.FindPropertyRelative("value").stringValue))
                shown++;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(shown == total
            ? $"Записей: {total}"
            : $"Показано: {shown} из {total}");
        if (GUILayout.Button("Добавить", GUILayout.Width(80f))) AddEntry();
        if (GUILayout.Button("Сортировать", GUILayout.Width(90f))) SortByKey();
        EditorGUILayout.EndHorizontal();

        int duplicates = CountDuplicateKeys();
        if (duplicates > 0)
            EditorGUILayout.HelpBox($"Дубликаты ключей: {duplicates}. Игра берёт значение из первой записи.", MessageType.Warning);
    }

    // ── Rows ──────────────────────────────────────────────────────────────────

    private void DrawRows()
    {
        var keyCounts = BuildKeyCounts();
        int total = _entries.arraySize;

        if (total == 0)
            EditorGUILayout.LabelField("Таблица пуста — добавьте первую запись.");

        for (int i = 0; i < total; i++)
        {
            var entry = _entries.GetArrayElementAtIndex(i);
            var keyProp = entry.FindPropertyRelative("key");
            var valueProp = entry.FindPropertyRelative("value");
            string key = keyProp.stringValue ?? "";
            string value = valueProp.stringValue ?? "";

            if (!MatchesSearch(key, value)) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            Rect keyRect = EditorGUILayout.GetControlRect(GUILayout.Width(KeyFieldWidth));
            if (keyCounts.TryGetValue(key, out int count) && count > 1)
                EditorGUI.DrawRect(keyRect, DuplicateKeyColor);
            keyProp.stringValue = EditorGUI.TextField(keyRect, key);

            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(RemoveButtonWidth)))
            {
                _entries.InsertArrayElementAtIndex(i);
                var inserted = _entries.GetArrayElementAtIndex(i + 1);
                inserted.FindPropertyRelative("key").stringValue = "";
                inserted.FindPropertyRelative("value").stringValue = "";
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(RemoveButtonWidth)))
            {
                _entries.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            DrawValueField(valueProp, value);
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawValueField(SerializedProperty valueProp, string value)
    {
        if (_wrapStyle == null)
            _wrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        float width = EditorGUIUtility.currentViewWidth - RowPadding;
        float height = Mathf.Max(
            EditorGUIUtility.singleLineHeight * 1.6f,
            _wrapStyle.CalcHeight(new GUIContent(value), width));

        Rect valueRect = EditorGUILayout.GetControlRect(GUILayout.Height(height));
        if (string.IsNullOrEmpty(value))
            EditorGUI.DrawRect(valueRect, EmptyValueColor);
        valueProp.stringValue = EditorGUI.TextArea(valueRect, value, _wrapStyle);
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void AddEntry()
    {
        _entries.InsertArrayElementAtIndex(_entries.arraySize);
        var element = _entries.GetArrayElementAtIndex(_entries.arraySize - 1);
        element.FindPropertyRelative("key").stringValue = "";
        element.FindPropertyRelative("value").stringValue = "";
    }

    private void SortByKey()
    {
        int count = _entries.arraySize;
        var pairs = new List<(string key, string value)>(count);
        for (int i = 0; i < count; i++)
        {
            var entry = _entries.GetArrayElementAtIndex(i);
            pairs.Add((entry.FindPropertyRelative("key").stringValue,
                       entry.FindPropertyRelative("value").stringValue));
        }
        pairs.Sort((a, b) => string.Compare(a.key, b.key, System.StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < count; i++)
        {
            var entry = _entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("key").stringValue = pairs[i].key;
            entry.FindPropertyRelative("value").stringValue = pairs[i].value;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool MatchesSearch(string key, string value)
    {
        if (string.IsNullOrEmpty(_search)) return true;
        return ContainsIgnoreCase(key, _search) || ContainsIgnoreCase(value, _search);
    }

    private static bool ContainsIgnoreCase(string source, string search)
        => !string.IsNullOrEmpty(source)
           && source.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private Dictionary<string, int> BuildKeyCounts()
    {
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < _entries.arraySize; i++)
        {
            string key = _entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue;
            if (string.IsNullOrEmpty(key)) continue;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
        return counts;
    }

    private int CountDuplicateKeys()
    {
        int duplicates = 0;
        foreach (var pair in BuildKeyCounts())
            if (pair.Value > 1) duplicates += pair.Value - 1;
        return duplicates;
    }
}
