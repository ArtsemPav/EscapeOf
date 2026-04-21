using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector для LoopPuzzlePowerCircuit.
/// Показывает: рубильники, матрицу питания прожекторов, матрицу смежности Lights Out,
/// код разблокировки S6, проверку решаемости и кнопку схемы цепи.
/// </summary>
[CustomEditor(typeof(LoopPuzzlePowerCircuit))]
public class LoopPuzzlePowerCircuitEditor : Editor
{
    // ── Serialized properties ──────────────────────────────────────────────────

    private SerializedProperty _switchesProp;
    private SerializedProperty _spotlightConfigsProp;
    private SerializedProperty _adjacencyProp;
    private SerializedProperty _sequenceProp;

    // ── Styles ─────────────────────────────────────────────────────────────────

    private static GUIStyle _headerStyle;
    private static GUIStyle _orLabelStyle;

    private static readonly Color ColorHeader      = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color ColorRowEven     = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color ColorRowOdd      = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ColorGroupBorder = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color ColorDiag        = new Color(0.12f, 0.12f, 0.12f, 1f);

    private const float ColSpotlight = 110f;
    private const float ColSwitch    = 34f;
    private const float ColOrLabel   = 28f;

    private string      _solvabilityResult = "";
    private MessageType _resultType        = MessageType.Info;

    private const string HelpFoldoutKey = "LoopPuzzlePowerCircuit_HelpOpen";

    private void OnEnable()
    {
        _switchesProp         = serializedObject.FindProperty("_switches");
        _spotlightConfigsProp = serializedObject.FindProperty("_spotlightConfigs");
        _adjacencyProp        = serializedObject.FindProperty("_adjacency");
        _sequenceProp         = serializedObject.FindProperty("_masterUnlockSequence");
    }

    // ── Inspector GUI ──────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        InitStyles();

        DrawHelpSection();
        EditorGUILayout.Space(8f);
        DrawSwitchesSection();
        EditorGUILayout.Space(8f);
        DrawSpotlightMatrixSection();
        EditorGUILayout.Space(8f);
        DrawAdjacencyMatrix();
        EditorGUILayout.Space(8f);
        DrawSolvabilitySection();
        EditorGUILayout.Space(8f);
        DrawDiagramButton();

        serializedObject.ApplyModifiedProperties();
    }

    // ── Help section ───────────────────────────────────────────────────────────

    private void DrawHelpSection()
    {
        bool isOpen = EditorPrefs.GetBool(HelpFoldoutKey, false);
        bool newOpen = EditorGUILayout.BeginFoldoutHeaderGroup(isOpen, "📖  Справка по настройке загадки");
        if (newOpen != isOpen) EditorPrefs.SetBool(HelpFoldoutKey, newOpen);

        if (newOpen)
        {
            EditorGUILayout.HelpBox(
                "ОБЗОР\n" +
                "Компонент управляет загадкой с рубильниками S1–S6 и прожекторами.\n" +
                "• S6 — кнопка питания. Игрок нажимает первым. Разблокирует S1–S5.\n" +
                "• S1–S5 — загадка Lights Out: каждое нажатие переключает соседей по матрице смежности.\n" +
                "• Прожекторы — получают питание по правилам OR-of-AND от состояний S1–S5.\n" +
                "• LoopPuzzleController следит за прожекторами и показывает символы когда все условия выполнены.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "ШАГ 1 — РУБИЛЬНИКИ\n" +
                "Перетащи LoopPuzzleButton объекты в список «Рубильники».\n" +
                "• Последний в списке = S6 (кнопка питания). Всегда доступен игроку.\n" +
                "• Все остальные = S1..S5. Заблокированы до нажатия S6.\n" +
                "Порядок важен: индекс в списке = индекс в матрицах ниже.",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "ШАГ 2 — ЛОГИКА ПРОЖЕКТОРОВ\n" +
                "Каждая строка = один прожектор. Логика: OR между строками, AND внутри строки.\n" +
                "• Галочка в ячейке [L, Sx] = прожектор L требует рубильник Sx включённым.\n" +
                "• Несколько строк у одного прожектора = достаточно выполнить любую из них (ИЛИ).\n\n" +
                "Пример: L2 горит когда (S1 И S2), ИЛИ когда (S4).\n" +
                "→ Строка 1: поставь галочки S1, S2.\n" +
                "→ Нажми «+ ИЛИ-группа», строка 2: поставь галочку S4.\n\n" +
                "Внимание: пустая строка (без галочек) = прожектор всегда включён. Удали её через ✕.",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "ШАГ 3 — СМЕЖНОСТЬ LIGHTS OUT\n" +
                "Матрица N×N для S1–S5. Показывает какие рубильники переключаются вместе.\n" +
                "• Галочка [i, j] = нажатие Si переключает Sj (и наоборот — матрица симметрична).\n" +
                "• Диагональ (серые ячейки) — самопереключение, всегда включено, не редактируется.\n" +
                "• S6 не участвует в матрице — он только запускает загадку.\n\n" +
                "Совет по сложности:\n" +
                "  Лёгкая  — каждый рубильник влияет на 1 соседа (цепочка).\n" +
                "  Средняя — каждый влияет на 2 соседа (звезда или пятиугольник).\n" +
                "  Сложная — каждый влияет на 3 соседа (перекрёстные связи).",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "ШАГ 4 — ПРОВЕРКА РЕШАЕМОСТИ\n" +
                "Нажми «Проверить (GF(2) анализ)» — редактор найдёт все возможные решения.\n" +
                "• Показывает конечное состояние S1–S5 и какие кнопки нужно нажать.\n" +
                "• Одно решение — идеально для загадки.\n" +
                "• Несколько решений — загадка решается несколькими способами (легче).\n" +
                "• «ЗАГАДКА НЕ РЕШАЕМА» — текущая комбинация матрицы и правил прожекторов\n" +
                "  не имеет решения. Измени матрицу смежности или правила активации.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "ИГРОВОЙ ПРОЦЕСС (что происходит в рантайме)\n" +
                "1. Игрок нажимает S6 → S1–S5 разблокируются.\n" +
                "2. Игрок нажимает S1–S5 — каждое нажатие переключает рубильник И его соседей.\n" +
                "3. Когда нужная комбинация S1–S5 активна → все прожекторы получают питание.\n" +
                "4. LoopPuzzleController проверяет высоту картин и цвета линз → показывает символы.\n" +
                "5. Все символы видны → дверь открывается.\n\n" +
                "Если символы не появляются:\n" +
                "  • Проверь что isSolved=false в LoopPuzzleController (контекстное меню → Reset Puzzle).\n" +
                "  • Убедись что все прожекторы назначены в LoopPuzzleController._conditions.\n" +
                "  • Проверь что картины на правильной высоте и линзы установлены.",
                MessageType.None);
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ── Switches ───────────────────────────────────────────────────────────────

    private void DrawSwitchesSection()
    {
        EditorGUILayout.LabelField("Рубильники", _headerStyle);
        EditorGUILayout.Space(2f);

        EditorGUI.indentLevel++;
        int count = _switchesProp.arraySize;
        for (int i = 0; i < count; i++)
        {
            string label = i == count - 1 ? $"S{i + 1} (Мастер)" : $"S{i + 1}";
            EditorGUILayout.PropertyField(_switchesProp.GetArrayElementAtIndex(i), new GUIContent(label));
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

    // ── Spotlight power matrix ─────────────────────────────────────────────────

    private void DrawSpotlightMatrixSection()
    {
        EditorGUILayout.LabelField("Логика питания прожекторов", _headerStyle);
        EditorGUILayout.HelpBox("S6 мастер управляет всеми прожекторами глобально — здесь не нужен.", MessageType.None);
        EditorGUILayout.Space(4f);

        int switchCount = _switchesProp.arraySize - 1; // S1–S5, мастер S6 исключён

        DrawHeaderRow(switchCount);
        EditorGUILayout.Space(1f);

        int configCount = _spotlightConfigsProp.arraySize;
        for (int ci = 0; ci < configCount; ci++)
        {
            DrawSpotlightRow(_spotlightConfigsProp.GetArrayElementAtIndex(ci), ci, switchCount);
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

    private void DrawHeaderRow(int switchCount)
    {
        Rect headerRect = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(headerRect, ColorHeader);

        float x = headerRect.x + ColSpotlight + ColOrLabel;
        for (int s = 0; s < switchCount; s++)
        {
            var sw = _switchesProp.GetArrayElementAtIndex(s).objectReferenceValue as LoopPuzzleButton;
            string swName = sw != null ? sw.gameObject.name.Replace("Button_", "") : $"S{s + 1}";
            GUI.Label(new Rect(x, headerRect.y, ColSwitch, headerRect.height), swName, _headerStyle);
            x += ColSwitch;
        }
    }

    private void DrawSpotlightRow(SerializedProperty configProp, int rowIndex, int switchCount)
    {
        var spotlightProp   = configProp.FindPropertyRelative("spotlight");
        var activationRules = configProp.FindPropertyRelative("activationRules");
        int ruleCount       = activationRules.arraySize;

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
        Color bgColor = rowIndex % 2 == 0 ? ColorRowEven : ColorRowOdd;

        for (int ri = 0; ri < ruleCount; ri++)
        {
            var requirementsProp = activationRules.GetArrayElementAtIndex(ri).FindPropertyRelative("requirements");

            Rect rowRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowRect, bgColor);
            EditorGUI.DrawRect(new Rect(rowRect.x + ColSpotlight, rowRect.y, 2f, rowRect.height), ColorGroupBorder);

            float x = rowRect.x + ColSpotlight;

            if (ri > 0)
                GUI.Label(new Rect(x, rowRect.y + 3f, ColOrLabel, rowRect.height), "ИЛИ", _orLabelStyle);
            x += ColOrLabel;

            for (int s = 0; s < switchCount; s++)
            {
                int reqIndex = FindRequirementIndex(requirementsProp, s);
                bool hasReq  = reqIndex >= 0;
                bool mustOn  = hasReq && requirementsProp.GetArrayElementAtIndex(reqIndex).FindPropertyRelative("mustBeOn").boolValue;

                Rect cell = new Rect(x + 7f, rowRect.y + 3f, ColSwitch - 4f, 16f);

                EditorGUI.BeginChangeCheck();
                bool newChecked = EditorGUI.Toggle(cell, hasReq && mustOn);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newChecked) SetRequirement(requirementsProp, s, true);
                    else            RemoveRequirement(requirementsProp, s);
                }
                x += ColSwitch;
            }

            Rect removeRect = new Rect(x + 2f, rowRect.y + 3f, 20f, 16f);
            if (GUI.Button(removeRect, "✕"))
            {
                activationRules.DeleteArrayElementAtIndex(ri);
                break;
            }
        }

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

    // ── Adjacency matrix ───────────────────────────────────────────────────────

    private void DrawAdjacencyMatrix()
    {
        EditorGUILayout.LabelField("Lights Out — смежность S1–S5", _headerStyle);
        EditorGUILayout.HelpBox(
            "✓ в ячейке [i,j]: нажатие Si переключает Sj (и наоборот). Мастер S6 не включается.",
            MessageType.None);

        int n = _switchesProp.arraySize - 1; // exclude master
        if (n <= 0) { EditorGUILayout.LabelField("Добавь хотя бы 2 рубильника + мастер."); return; }

        // Sync adjacency array size to n (non-master switches only).
        while (_adjacencyProp.arraySize < n)
            _adjacencyProp.InsertArrayElementAtIndex(_adjacencyProp.arraySize);
        while (_adjacencyProp.arraySize > n)
            _adjacencyProp.DeleteArrayElementAtIndex(_adjacencyProp.arraySize - 1);

        bool[,] adj = new bool[n, n];
        for (int i = 0; i < n; i++)
        {
            var nbProp = _adjacencyProp.GetArrayElementAtIndex(i).FindPropertyRelative("neighborIndices");
            for (int k = 0; k < nbProp.arraySize; k++)
            {
                int nb = nbProp.GetArrayElementAtIndex(k).intValue;
                if (nb >= 0 && nb < n) { adj[i, nb] = true; adj[nb, i] = true; }
            }
        }

        string[] names = GetNonMasterNames(n);
        const float colW = 34f, labelW = 36f;

        Rect headerRect = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(headerRect, ColorHeader);
        float x = headerRect.x + labelW;
        for (int j = 0; j < n; j++)
            GUI.Label(new Rect(x + j * colW + 4f, headerRect.y + 2f, colW, 16f), names[j], EditorStyles.miniLabel);

        bool changed = false;
        for (int i = 0; i < n; i++)
        {
            Rect rowRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowRect, i % 2 == 0 ? ColorRowEven : ColorRowOdd);
            GUI.Label(new Rect(rowRect.x + 2f, rowRect.y + 3f, labelW, 16f), names[i], EditorStyles.miniLabel);

            for (int j = 0; j < n; j++)
            {
                Rect cell = new Rect(rowRect.x + labelW + j * colW + 9f, rowRect.y + 4f, 14f, 14f);
                if (i == j) { EditorGUI.DrawRect(new Rect(cell.x - 2f, cell.y - 2f, cell.width + 4f, cell.height + 4f), ColorDiag); continue; }

                EditorGUI.BeginChangeCheck();
                bool v = EditorGUI.Toggle(cell, adj[i, j]);
                if (EditorGUI.EndChangeCheck()) { adj[i, j] = v; adj[j, i] = v; changed = true; }
            }
        }

        if (changed)
        {
            for (int i = 0; i < n; i++)
            {
                var nbProp = _adjacencyProp.GetArrayElementAtIndex(i).FindPropertyRelative("neighborIndices");
                nbProp.ClearArray();
                for (int j = 0; j < n; j++)
                    if (i != j && adj[i, j]) { nbProp.InsertArrayElementAtIndex(nbProp.arraySize); nbProp.GetArrayElementAtIndex(nbProp.arraySize - 1).intValue = j; }
            }
        }
    }

    // ── Unlock sequence ────────────────────────────────────────────────────────

    private void DrawSequenceSection()
    {
        EditorGUILayout.LabelField("Код разблокировки S6", _headerStyle);
        EditorGUILayout.HelpBox("Рубильники в нужном порядке. Неверное нажатие сбрасывает прогресс.", MessageType.None);

        int n = _switchesProp.arraySize - 1;
        string[] names = GetNonMasterNames(n);

        int count = _sequenceProp.arraySize;
        for (int k = 0; k < count; k++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Шаг {k + 1}", GUILayout.Width(50f));
                var elem = _sequenceProp.GetArrayElementAtIndex(k);
                int cur  = Mathf.Clamp(elem.intValue, 0, Mathf.Max(0, n - 1));
                int next = EditorGUILayout.Popup(cur, names, GUILayout.Width(80f));
                if (next != cur) elem.intValue = next;
                if (GUILayout.Button("✕", GUILayout.Width(22f))) { _sequenceProp.DeleteArrayElementAtIndex(k); break; }
            }
        }

        if (GUILayout.Button("+ Шаг", GUILayout.Width(80f)) && n > 0)
        {
            _sequenceProp.InsertArrayElementAtIndex(count);
            _sequenceProp.GetArrayElementAtIndex(count).intValue = 0;
        }

        if (count > 0)
        {
            var sb = new StringBuilder("Код: ");
            for (int k = 0; k < count; k++)
            {
                int idx = _sequenceProp.GetArrayElementAtIndex(k).intValue;
                if (k > 0) sb.Append(" → ");
                sb.Append(idx >= 0 && idx < names.Length ? names[idx] : "?");
            }
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.helpBox);
        }
    }

    // ── Solvability ────────────────────────────────────────────────────────────

    private void DrawSolvabilitySection()
    {
        EditorGUILayout.LabelField("Проверка решаемости", _headerStyle);
        if (GUILayout.Button("Проверить (GF(2) анализ)")) RunSolvabilityCheck();
        if (!string.IsNullOrEmpty(_solvabilityResult))
            EditorGUILayout.HelpBox(_solvabilityResult, _resultType);
    }

    private void RunSolvabilityCheck()
    {
        var circuit = (LoopPuzzlePowerCircuit)target;
        int n = circuit.SwitchCount - 1; // exclude master

        if (n <= 0) { _solvabilityResult = "Нет рубильников S1–S5."; _resultType = MessageType.Warning; return; }

        bool[,] A      = circuit.BuildAdjacencyMatrix();
        string[] names = GetNonMasterNames(n);
        var solutions  = new List<(bool[] target, bool[] presses)>();

        for (int mask = 0; mask < (1 << n); mask++)
        {
            bool[] state = new bool[n];
            for (int i = 0; i < n; i++) state[i] = (mask >> i & 1) == 1;
            if (!circuit.CheckAllPoweredWith(state)) continue;
            bool[] presses = SolveGF2(A, state, n);
            if (presses != null) solutions.Add((state, presses));
        }

        if (solutions.Count == 0)
        {
            _solvabilityResult = "ЗАГАДКА НЕ РЕШАЕМА.\nИзмените правила активации или матрицу смежности.";
            _resultType = MessageType.Error;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Загадка решаема. Найдено {solutions.Count} решение(й):\n");
        foreach (var (target, presses) in solutions)
        {
            var tSb = new StringBuilder("Конечное состояние: ");
            for (int i = 0; i < n; i++) tSb.Append($"{names[i]}={(target[i] ? "ON" : "OFF")} ");
            sb.AppendLine(tSb.ToString());

            var pSb = new StringBuilder("Нажать: ");
            bool any = false;
            for (int i = 0; i < n; i++) { if (!presses[i]) continue; if (any) pSb.Append(", "); pSb.Append(names[i]); any = true; }
            if (!any) pSb.Append("(никакие)");
            sb.AppendLine(pSb.ToString());
            sb.AppendLine();
        }

        _solvabilityResult = sb.ToString();
        _resultType        = solutions.Count == 1 ? MessageType.Info : MessageType.Warning;
    }

    // ── Circuit diagram button ─────────────────────────────────────────────────

    private void DrawDiagramButton()
    {
        if (GUILayout.Button("Открыть схему цепи"))
            CircuitDiagramWindow.Open((LoopPuzzlePowerCircuit)target);
    }

    // ── GF(2) solver ──────────────────────────────────────────────────────────

    private static bool[] SolveGF2(bool[,] A, bool[] b, int n)
    {
        int[,] aug = new int[n, n + 1];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) aug[i, j] = A[i, j] ? 1 : 0; aug[i, n] = b[i] ? 1 : 0; }

        int[] colForRow = new int[n];
        for (int i = 0; i < n; i++) colForRow[i] = -1;

        int pivotRow = 0;
        for (int col = 0; col < n && pivotRow < n; col++)
        {
            int found = -1;
            for (int r = pivotRow; r < n; r++) if (aug[r, col] == 1) { found = r; break; }
            if (found == -1) continue;
            for (int j = 0; j <= n; j++) { int t = aug[pivotRow, j]; aug[pivotRow, j] = aug[found, j]; aug[found, j] = t; }
            colForRow[pivotRow] = col;
            for (int r = 0; r < n; r++) if (r != pivotRow && aug[r, col] == 1) for (int j = 0; j <= n; j++) aug[r, j] ^= aug[pivotRow, j];
            pivotRow++;
        }

        for (int r = 0; r < n; r++) if (colForRow[r] == -1 && aug[r, n] == 1) return null;

        bool[] x = new bool[n];
        for (int r = 0; r < n; r++) if (colForRow[r] >= 0) x[colForRow[r]] = aug[r, n] == 1;
        return x;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private string[] GetNonMasterNames(int n)
    {
        var names = new string[n];
        for (int i = 0; i < n; i++)
        {
            var sw = _switchesProp.GetArrayElementAtIndex(i).objectReferenceValue as LoopPuzzleButton;
            names[i] = sw != null ? sw.gameObject.name.Replace("Button_", "") : $"S{i + 1}";
        }
        return names;
    }

    private static int FindRequirementIndex(SerializedProperty requirements, int switchIndex)
    {
        for (int i = 0; i < requirements.arraySize; i++)
            if (requirements.GetArrayElementAtIndex(i).FindPropertyRelative("switchIndex").intValue == switchIndex)
                return i;
        return -1;
    }

    private static void SetRequirement(SerializedProperty requirements, int switchIndex, bool mustBeOn)
    {
        int existing = FindRequirementIndex(requirements, switchIndex);
        if (existing >= 0) { requirements.GetArrayElementAtIndex(existing).FindPropertyRelative("mustBeOn").boolValue = mustBeOn; return; }
        requirements.InsertArrayElementAtIndex(requirements.arraySize);
        var req = requirements.GetArrayElementAtIndex(requirements.arraySize - 1);
        req.FindPropertyRelative("switchIndex").intValue = switchIndex;
        req.FindPropertyRelative("mustBeOn").boolValue   = mustBeOn;
    }

    private static void RemoveRequirement(SerializedProperty requirements, int switchIndex)
    {
        int idx = FindRequirementIndex(requirements, switchIndex);
        if (idx >= 0) requirements.DeleteArrayElementAtIndex(idx);
    }

    private static void InitStyles()
    {
        if (_headerStyle != null) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
        _orLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
    }
}
