using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for LoopPuzzlePowerCircuit.
/// Renders switch names as column headers and spotlights as rows,
/// with checkboxes for AND-groups and [+OR] buttons for additional rule groups.
/// </summary>
[CustomEditor(typeof(LoopPuzzlePowerCircuit))]
public class LoopPuzzlePowerCircuitEditor : Editor
{
    // ── Styles ─────────────────────────────────────────────────────────────────

    private static GUIStyle _headerStyle;
    private static GUIStyle _orLabelStyle;
    private static GUIStyle _spotlightLabelStyle;

    private static readonly Color ColorRowEven     = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color ColorRowOdd      = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ColorGroupBorder = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color ColorHeader      = new Color(0.15f, 0.15f, 0.15f, 1f);

    // Column widths
    private const float ColSpotlight = 110f;
    private const float ColSwitch    = 34f;
    private const float ColOrLabel   = 28f;
    private const float ColButtons   = 56f;

    // ── Serialized properties ──────────────────────────────────────────────────

    private SerializedProperty _switchesProp;
    private SerializedProperty _spotlightConfigsProp;

    private void OnEnable()
    {
        _switchesProp          = serializedObject.FindProperty("_switches");
        _spotlightConfigsProp  = serializedObject.FindProperty("_spotlightConfigs");
    }

    // ── Inspector GUI ──────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        InitStyles();

        DrawSwitchesSection();
        EditorGUILayout.Space(6f);
        DrawMatrixSection();

        serializedObject.ApplyModifiedProperties();
    }

    // ── Switches section ───────────────────────────────────────────────────────

    private void DrawSwitchesSection()
    {
        EditorGUILayout.LabelField("Рубильники", _headerStyle);
        EditorGUILayout.Space(2f);

        EditorGUI.indentLevel++;
        int count = _switchesProp.arraySize;
        for (int i = 0; i < count; i++)
        {
            var elem = _switchesProp.GetArrayElementAtIndex(i);
            string label = i == count - 1 ? $"S{i + 1} (Мастер)" : $"S{i + 1}";
            EditorGUILayout.PropertyField(elem, new GUIContent(label));
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(2f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ Рубильник", GUILayout.Width(110f)))
            {
                _switchesProp.InsertArrayElementAtIndex(_switchesProp.arraySize);
                _switchesProp.GetArrayElementAtIndex(_switchesProp.arraySize - 1).objectReferenceValue = null;
            }
            if (_switchesProp.arraySize > 0 && GUILayout.Button("- Рубильник", GUILayout.Width(110f)))
                _switchesProp.DeleteArrayElementAtIndex(_switchesProp.arraySize - 1);
        }
    }

    // ── Matrix section ─────────────────────────────────────────────────────────

    private void DrawMatrixSection()
    {
        EditorGUILayout.LabelField("Логика питания прожекторов", _headerStyle);
        EditorGUILayout.Space(4f);

        int switchCount = _switchesProp.arraySize;

        // Header row: switch names
        DrawHeaderRow(switchCount);
        EditorGUILayout.Space(1f);

        // One row per spotlight config
        int configCount = _spotlightConfigsProp.arraySize;
        for (int ci = 0; ci < configCount; ci++)
        {
            var configProp = _spotlightConfigsProp.GetArrayElementAtIndex(ci);
            DrawSpotlightRow(configProp, ci, switchCount);
            EditorGUILayout.Space(2f);
        }

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("+ Прожектор", GUILayout.Width(120f)))
        {
            _spotlightConfigsProp.InsertArrayElementAtIndex(_spotlightConfigsProp.arraySize);
            var newConfig = _spotlightConfigsProp.GetArrayElementAtIndex(_spotlightConfigsProp.arraySize - 1);
            newConfig.FindPropertyRelative("spotlight").objectReferenceValue = null;
            newConfig.FindPropertyRelative("activationRules").ClearArray();
        }
    }

    // ── Header row ─────────────────────────────────────────────────────────────

    private void DrawHeaderRow(int switchCount)
    {
        Rect headerRect = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(headerRect, ColorHeader);

        float x = headerRect.x + ColSpotlight + ColOrLabel;
        for (int s = 0; s < switchCount; s++)
        {
            var sw = _switchesProp.GetArrayElementAtIndex(s).objectReferenceValue as LoopPuzzleButton;
            string swName = sw != null ? sw.gameObject.name : $"S{s + 1}";
            // Shorten to fit: strip "Button_" prefix
            swName = swName.Replace("Button_", "");

            Rect cell = new Rect(x, headerRect.y, ColSwitch, headerRect.height);
            GUI.Label(cell, swName, _headerStyle);
            x += ColSwitch;
        }
    }

    // ── One spotlight row ──────────────────────────────────────────────────────

    private void DrawSpotlightRow(SerializedProperty configProp, int rowIndex, int switchCount)
    {
        var spotlightProp     = configProp.FindPropertyRelative("spotlight");
        var activationRules   = configProp.FindPropertyRelative("activationRules");
        int ruleCount         = activationRules.arraySize;

        // Spotlight label + remove button row
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(spotlightProp, GUIContent.none, GUILayout.Width(ColSpotlight));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(22f)))
            {
                _spotlightConfigsProp.DeleteArrayElementAtIndex(rowIndex);
                return;
            }
        }

        EditorGUILayout.Space(2f);

        // Draw each OR-group (AND row)
        Color bgColor = rowIndex % 2 == 0 ? ColorRowEven : ColorRowOdd;

        for (int ri = 0; ri < ruleCount; ri++)
        {
            var ruleProp        = activationRules.GetArrayElementAtIndex(ri);
            var requirementsProp = ruleProp.FindPropertyRelative("requirements");

            Rect rowRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowRect, bgColor);
            DrawGroupBorderLeft(rowRect);

            float x = rowRect.x + ColSpotlight;

            // OR label
            if (ri > 0)
            {
                Rect orRect = new Rect(x, rowRect.y + 3f, ColOrLabel, rowRect.height);
                GUI.Label(orRect, "ИЛИ", _orLabelStyle);
            }
            x += ColOrLabel;

            // Switch checkboxes
            for (int s = 0; s < switchCount; s++)
            {
                // Find existing requirement for this switch
                int reqIndex = FindRequirementIndex(requirementsProp, s);
                bool hasReq  = reqIndex >= 0;
                bool mustOn  = hasReq && requirementsProp.GetArrayElementAtIndex(reqIndex).FindPropertyRelative("mustBeOn").boolValue;

                Rect cellRect = new Rect(x + 7f, rowRect.y + 3f, ColSwitch - 4f, 16f);

                EditorGUI.BeginChangeCheck();
                // Tri-state: unchecked = not in rule, checked = mustBeOn=true
                bool newChecked = EditorGUI.Toggle(cellRect, hasReq && mustOn);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newChecked)
                    {
                        // Add or update requirement: mustBeOn = true
                        SetRequirement(requirementsProp, s, true);
                    }
                    else
                    {
                        // Remove requirement for this switch
                        RemoveRequirement(requirementsProp, s);
                    }
                }

                x += ColSwitch;
            }

            // Remove OR-group button
            Rect removeRect = new Rect(x + 2f, rowRect.y + 3f, 20f, 16f);
            if (GUI.Button(removeRect, "✕"))
            {
                activationRules.DeleteArrayElementAtIndex(ri);
                break;
            }
        }

        // Add OR-group button
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(ColSpotlight + ColOrLabel);
            if (GUILayout.Button("+ ИЛИ-группа", GUILayout.Width(100f)))
            {
                activationRules.InsertArrayElementAtIndex(ruleCount);
                activationRules.GetArrayElementAtIndex(ruleCount).FindPropertyRelative("requirements").ClearArray();
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static int FindRequirementIndex(SerializedProperty requirements, int switchIndex)
    {
        for (int i = 0; i < requirements.arraySize; i++)
        {
            var req = requirements.GetArrayElementAtIndex(i);
            if (req.FindPropertyRelative("switchIndex").intValue == switchIndex)
                return i;
        }
        return -1;
    }

    private static void SetRequirement(SerializedProperty requirements, int switchIndex, bool mustBeOn)
    {
        int existing = FindRequirementIndex(requirements, switchIndex);
        if (existing >= 0)
        {
            requirements.GetArrayElementAtIndex(existing).FindPropertyRelative("mustBeOn").boolValue = mustBeOn;
        }
        else
        {
            requirements.InsertArrayElementAtIndex(requirements.arraySize);
            var newReq = requirements.GetArrayElementAtIndex(requirements.arraySize - 1);
            newReq.FindPropertyRelative("switchIndex").intValue  = switchIndex;
            newReq.FindPropertyRelative("mustBeOn").boolValue    = mustBeOn;
        }
    }

    private static void RemoveRequirement(SerializedProperty requirements, int switchIndex)
    {
        int idx = FindRequirementIndex(requirements, switchIndex);
        if (idx >= 0)
            requirements.DeleteArrayElementAtIndex(idx);
    }

    private static void DrawGroupBorderLeft(Rect rowRect)
    {
        EditorGUI.DrawRect(new Rect(rowRect.x + ColSpotlight, rowRect.y, 2f, rowRect.height), ColorGroupBorder);
    }

    // ── Style init ─────────────────────────────────────────────────────────────

    private static void InitStyles()
    {
        if (_headerStyle != null) return;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize  = 11
        };

        _orLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            normal    = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };

        _spotlightLabelStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
    }
}
