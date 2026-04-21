using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the full circuit diagram with bus-lane routing and layered arcs:
///   — Spotlight nodes (top row), each with its own border color.
///   — Horizontal bus lanes between the two rows — one lane per spotlight.
///     Rule lines travel vertically to their lane, then horizontally to the spotlight.
///     This prevents all lines from merging into a single cord.
///   — Switch nodes (bottom row).
///   — Adjacency arcs drawn BELOW the switch row, layered by node distance:
///     adjacent = shallow arc, far apart = deeper arc, each distance has its own color.
///   — Solid lines = first AND-group of a rule, dashed = subsequent OR-groups.
/// </summary>
public class CircuitDiagramWindow : EditorWindow
{
    // ── State ──────────────────────────────────────────────────────────────────

    private LoopPuzzlePowerCircuit _circuit;
    private Texture2D              _diagramTex;
    private bool                   _needsRegen = true;
    private Vector2                _scroll;

    // ── Layout ─────────────────────────────────────────────────────────────────

    private const int TexW       = 920;
    private const int TexH       = 580;
    private const int NodeW      = 56;
    private const int NodeH      = 28;
    private const int SpotlightY = 80;          // spotlights near top  (low Y in texture = top of display)
    private const int SwitchY    = 400;          // switches below centre (high Y = bottom of display)
    private const int PadX       = 90;

    // ── Colors ─────────────────────────────────────────────────────────────────

    private static readonly Color BgColor         = new Color(0.08f, 0.08f, 0.12f);
    private static readonly Color GridColor        = new Color(0.16f, 0.16f, 0.22f);
    private static readonly Color SwitchFill      = new Color(0.10f, 0.36f, 0.10f);
    private static readonly Color SwitchBorder    = new Color(0.28f, 0.90f, 0.28f);
    private static readonly Color SpotFill        = new Color(0.10f, 0.20f, 0.46f);
    private static readonly Color LabelColor      = new Color(0.50f, 0.50f, 0.56f);
    private static readonly Color TextColor       = Color.white;
    private static readonly Color JunctionColor   = Color.white;

    // One color per spotlight (L1–L6+).
    private static readonly Color[] SpotlightColors =
    {
        new Color(1.00f, 0.48f, 0.10f),  // L1 — orange
        new Color(0.10f, 0.85f, 1.00f),  // L2 — cyan
        new Color(0.78f, 0.20f, 1.00f),  // L3 — violet
        new Color(0.18f, 1.00f, 0.36f),  // L4 — lime
        new Color(1.00f, 0.90f, 0.10f),  // L5 — yellow
        new Color(1.00f, 0.20f, 0.50f),  // L6 — rose
    };

    // One color per adjacency-distance level (1 = adjacent, 4+ = far).
    private static readonly Color[] ArcColors =
    {
        new Color(1.00f, 0.96f, 0.14f),  // dist 1 — bright yellow
        new Color(1.00f, 0.72f, 0.08f),  // dist 2 — gold
        new Color(0.98f, 0.48f, 0.06f),  // dist 3 — amber
        new Color(0.95f, 0.28f, 0.06f),  // dist 4+ — orange-red
    };

    // ── Bitmap font (3×5 pixels per glyph) ────────────────────────────────────

    private static readonly Dictionary<char, byte[]> Font = new()
    {
        ['0'] = new byte[] { 0b110, 0b101, 0b101, 0b101, 0b110 },
        ['1'] = new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 },
        ['2'] = new byte[] { 0b110, 0b001, 0b010, 0b100, 0b111 },
        ['3'] = new byte[] { 0b110, 0b001, 0b011, 0b001, 0b110 },
        ['4'] = new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 },
        ['5'] = new byte[] { 0b111, 0b100, 0b110, 0b001, 0b110 },
        ['6'] = new byte[] { 0b011, 0b100, 0b110, 0b101, 0b110 },
        ['7'] = new byte[] { 0b111, 0b001, 0b010, 0b010, 0b010 },
        ['8'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b110 },
        ['9'] = new byte[] { 0b110, 0b101, 0b111, 0b001, 0b110 },
        ['A'] = new byte[] { 0b010, 0b101, 0b111, 0b101, 0b101 },
        ['B'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b110 },
        ['C'] = new byte[] { 0b011, 0b100, 0b100, 0b100, 0b011 },
        ['D'] = new byte[] { 0b110, 0b101, 0b101, 0b101, 0b110 },
        ['E'] = new byte[] { 0b111, 0b100, 0b110, 0b100, 0b111 },
        ['F'] = new byte[] { 0b111, 0b100, 0b110, 0b100, 0b100 },
        ['G'] = new byte[] { 0b011, 0b100, 0b111, 0b101, 0b011 },
        ['H'] = new byte[] { 0b101, 0b101, 0b111, 0b101, 0b101 },
        ['I'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b111 },
        ['J'] = new byte[] { 0b001, 0b001, 0b001, 0b101, 0b010 },
        ['K'] = new byte[] { 0b101, 0b110, 0b100, 0b110, 0b101 },
        ['L'] = new byte[] { 0b100, 0b100, 0b100, 0b100, 0b111 },
        ['M'] = new byte[] { 0b101, 0b111, 0b101, 0b101, 0b101 },
        ['N'] = new byte[] { 0b101, 0b111, 0b111, 0b101, 0b101 },
        ['O'] = new byte[] { 0b010, 0b101, 0b101, 0b101, 0b010 },
        ['P'] = new byte[] { 0b110, 0b101, 0b110, 0b100, 0b100 },
        ['R'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b101 },
        ['S'] = new byte[] { 0b011, 0b100, 0b010, 0b001, 0b110 },
        ['T'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b010 },
        ['U'] = new byte[] { 0b101, 0b101, 0b101, 0b101, 0b111 },
        ['V'] = new byte[] { 0b101, 0b101, 0b101, 0b101, 0b010 },
        ['W'] = new byte[] { 0b101, 0b101, 0b101, 0b111, 0b010 },
        ['X'] = new byte[] { 0b101, 0b101, 0b010, 0b101, 0b101 },
        ['Y'] = new byte[] { 0b101, 0b101, 0b010, 0b010, 0b010 },
        ['Z'] = new byte[] { 0b111, 0b001, 0b010, 0b100, 0b111 },
        ['-'] = new byte[] { 0b000, 0b000, 0b111, 0b000, 0b000 },
        ['+'] = new byte[] { 0b000, 0b010, 0b111, 0b010, 0b000 },
        [' '] = new byte[] { 0b000, 0b000, 0b000, 0b000, 0b000 },
    };

    // ── Open ───────────────────────────────────────────────────────────────────

    public static void Open(LoopPuzzlePowerCircuit circuit)
    {
        var win = GetWindow<CircuitDiagramWindow>("Circuit Diagram");
        win._circuit    = circuit;
        win._needsRegen = true;
        win.minSize     = new Vector2(640f, 520f);
        win.Show();
    }

    // ── GUI ────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_circuit == null)
        {
            EditorGUILayout.HelpBox("LoopPuzzlePowerCircuit не задан.", MessageType.Error);
            return;
        }

        if (_needsRegen) { RegenerateDiagram(); _needsRegen = false; }

        // Toolbar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Обновить",     EditorStyles.toolbarButton, GUILayout.Width(80f)))  _needsRegen = true;
        if (GUILayout.Button("Экспорт PNG…", EditorStyles.toolbarButton, GUILayout.Width(100f))) ExportPng();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Legend — adjacency arc levels
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Lights Out:", EditorStyles.miniLabel, GUILayout.Width(62f));
        string[] distLabels = { "смежн.", "ч/з 1", "ч/з 2", "ч/з 3+" };
        for (int d = 0; d < ArcColors.Length; d++)
        {
            DrawSwatch(ArcColors[d]);
            GUILayout.Label(distLabels[d], EditorStyles.miniLabel);
            GUILayout.Space(6f);
        }
        GUILayout.Space(12f);
        GUILayout.Label("Питание:", EditorStyles.miniLabel, GUILayout.Width(50f));
        int ln = _circuit.SpotlightConfigCount;
        for (int ci = 0; ci < ln; ci++)
        {
            DrawSwatch(SpotlightColors[ci % SpotlightColors.Length]);
            GUILayout.Label($"L{ci + 1}", EditorStyles.miniLabel);
            GUILayout.Space(6f);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Legend — line style
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("─────  AND-группа 1 (основная)     - - -  AND-группа 2+ (ИЛИ альтернатива)     ●  точка соединения",
            EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        // Diagram
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_diagramTex != null)
        {
            Rect r = GUILayoutUtility.GetRect(TexW, TexH, GUILayout.Width(TexW), GUILayout.Height(TexH));
            GUI.DrawTexture(r, _diagramTex, ScaleMode.ScaleToFit);
        }
        EditorGUILayout.EndScrollView();
    }

    private static void DrawSwatch(Color c)
    {
        Rect r = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
        EditorGUI.DrawRect(r, c);
    }

    // ── Diagram generation ─────────────────────────────────────────────────────

    private void RegenerateDiagram()
    {
        if (_diagramTex != null) DestroyImmediate(_diagramTex);
        _diagramTex = GenerateTexture(_circuit);
    }

    private static Texture2D GenerateTexture(LoopPuzzlePowerCircuit circuit)
    {
        var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Fill(tex, BgColor);

        int sn = circuit.SwitchCount - 1;
        int ln = circuit.SpotlightConfigCount;

        Vector2Int[] swPos = ComputePositions(sn, PadX, SwitchY,    TexW);
        Vector2Int[] spPos = ComputePositions(ln, PadX, SpotlightY, TexW);

        // ── Bus lane guide lines ───────────────────────────────────────────────
        for (int ci = 0; ci < ln; ci++)
        {
            int laneY = BusLaneY(ci, ln);
            DrawHLine(tex, PadX / 2, TexW - PadX / 2, laneY, GridColor);
            // Lane label on the left margin
            Color lc = SpotlightColors[ci % SpotlightColors.Length];
            DrawText(tex, $"L{ci + 1}", PadX / 2 - 26, laneY + 3, lc, 1);
        }

        // ── Adjacency arcs below switches, layered by distance ─────────────────
        bool[,] adj = circuit.BuildAdjacencyMatrix();
        // Draw deepest arcs first so shallow ones paint on top
        for (int dist = sn - 1; dist >= 1; dist--)
        {
            Color ac = ArcColors[Mathf.Min(dist - 1, ArcColors.Length - 1)];
            for (int i = 0; i < sn; i++)
            {
                int j = i + dist;
                if (j < sn && adj[i, j])
                    DrawLayeredArc(tex, swPos[i], swPos[j], ac, 2, dist);
            }
        }

        // ── Rule lines routed through bus lanes ────────────────────────────────
        for (int ci = 0; ci < ln; ci++)
        {
            Color rc    = SpotlightColors[ci % SpotlightColors.Length];
            int   laneY = BusLaneY(ci, ln);
            var   cfg   = circuit.GetSpotlightConfig(ci);
            if (cfg.activationRules == null) continue;

            for (int ri = 0; ri < cfg.activationRules.Length; ri++)
            {
                var rule = cfg.activationRules[ri];
                if (rule.requirements == null) continue;
                bool dashed = ri > 0;

                // Collect unique switch indices in this OR-group
                var usedSwitches = new HashSet<int>();
                foreach (var req in rule.requirements)
                    if (req.switchIndex >= 0 && req.switchIndex < sn)
                        usedSwitches.Add(req.switchIndex);

                foreach (int si in usedSwitches)
                    DrawBusLine(tex, swPos[si], spPos[ci], laneY, rc, 2, dashed);
            }
        }

        // ── Nodes (drawn on top of lines) ─────────────────────────────────────
        for (int i = 0; i < sn; i++)
        {
            var p = swPos[i];
            DrawFilledRect(tex, p.x - NodeW / 2, p.y - NodeH / 2, NodeW, NodeH, SwitchFill);
            DrawRectBorder(tex, p.x - NodeW / 2, p.y - NodeH / 2, NodeW, NodeH, SwitchBorder);
            DrawCenteredText(tex, $"S{i + 1}", p.x, p.y, TextColor, 2);
        }
        for (int i = 0; i < ln; i++)
        {
            var   p      = spPos[i];
            Color border = SpotlightColors[i % SpotlightColors.Length];
            DrawFilledRect(tex, p.x - NodeW / 2, p.y - NodeH / 2, NodeW, NodeH, SpotFill);
            DrawRectBorder(tex, p.x - NodeW / 2, p.y - NodeH / 2, NodeW, NodeH, border);
            DrawCenteredText(tex, $"L{i + 1}", p.x, p.y, TextColor, 2);
        }

        // ── Section labels ─────────────────────────────────────────────────────
        if (sn > 0) DrawText(tex, "SWITCHES", 4, SwitchY    + NodeH / 2 + 10, LabelColor, 1);
        if (ln > 0) DrawText(tex, "LIGHTS",   4, SpotlightY - NodeH / 2 - 10, LabelColor, 1);

        tex.Apply();
        return tex;
    }

    // ── Bus lane routing ───────────────────────────────────────────────────────

    /// <summary>Returns the Y of spotlight ci's horizontal bus lane.</summary>
    private static int BusLaneY(int ci, int ln) =>
        Mathf.RoundToInt(SpotlightY + (SwitchY - SpotlightY) * (float)(ci + 1) / (ln + 1));

    /// <summary>
    /// Draws a routed connection: switch → vertical to bus lane → horizontal → vertical to spotlight.
    /// Solid for the primary AND-group, dashed for subsequent OR-groups.
    /// </summary>
    private static void DrawBusLine(Texture2D tex, Vector2Int sw, Vector2Int sp,
                                    int laneY, Color c, int thickness, bool dashed)
    {
        int swEdge = sw.y - NodeH / 2;  // top edge of switch node
        int spEdge = sp.y + NodeH / 2;  // bottom edge of spotlight node

        if (dashed)
        {
            DrawDashedLine(tex, sw.x, swEdge, sw.x,  laneY, c, thickness);
            DrawDashedLine(tex, sw.x, laneY,  sp.x,  laneY, c, thickness);
            DrawDashedLine(tex, sp.x, spEdge, sp.x,  laneY, c, thickness);
        }
        else
        {
            DrawLine(tex, sw.x, swEdge, sw.x, laneY, c, thickness);
            DrawLine(tex, sw.x, laneY,  sp.x, laneY, c, thickness);
            DrawLine(tex, sp.x, spEdge, sp.x, laneY, c, thickness);
        }

        // Junction dots where verticals meet the bus lane
        DrawDot(tex, sw.x, laneY, 3, c);
        DrawDot(tex, sp.x, laneY, 3, c);
    }

    // ── Layered arc (below switch row, height proportional to node distance) ───

    private static void DrawLayeredArc(Texture2D tex, Vector2Int a, Vector2Int b,
                                       Color c, int thickness, int dist)
    {
        // Each distance level adds 28px of depth below SwitchY
        float depth = 26f + dist * 26f;          // 52, 78, 104, 130 for dist 1-4
        int   steps = Mathf.Max(Mathf.Abs(b.x - a.x) * 2, 48);

        int prevX = a.x, prevY = a.y;
        for (int s = 1; s <= steps; s++)
        {
            float t  = s / (float)steps;
            float px = Mathf.Lerp(a.x, b.x, t);
            float py = Mathf.Lerp(a.y, b.y, t) + depth * 4f * t * (1f - t);
            int   ix = Mathf.RoundToInt(px), iy = Mathf.RoundToInt(py);
            DrawLine(tex, prevX, prevY, ix, iy, c, thickness);
            prevX = ix; prevY = iy;
        }
    }

    // ── Position helpers ───────────────────────────────────────────────────────

    private static Vector2Int[] ComputePositions(int count, int padX, int centerY, int texWidth)
    {
        var pos = new Vector2Int[count];
        if (count == 0) return pos;
        float step = count > 1 ? (texWidth - padX * 2f) / (count - 1) : 0f;
        for (int i = 0; i < count; i++)
            pos[i] = new Vector2Int(
                Mathf.RoundToInt(count > 1 ? padX + i * step : texWidth / 2f),
                centerY);
        return pos;
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    private void ExportPng()
    {
        if (_diagramTex == null) return;
        string path = EditorUtility.SaveFilePanelInProject(
            "Сохранить схему", "CircuitDiagram", "png", "Выберите путь для сохранения.");
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllBytes(path, _diagramTex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            importer.isReadable         = true;
            importer.mipmapEnabled      = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode         = FilterMode.Point;
            importer.SaveAndReimport();
        }
        Debug.Log($"[CircuitDiagram] Схема сохранена: {path}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
    }

    // ── Pixel primitives ───────────────────────────────────────────────────────

    private static void Fill(Texture2D tex, Color c)
    {
        var pixels = new Color[tex.width * tex.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
    }

    private static void SetPixelSafe(Texture2D tex, int x, int y, Color c)
    {
        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
            tex.SetPixel(x, y, c);
    }

    private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color c, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy, half = thickness / 2;
        while (true)
        {
            for (int ty = -half; ty <= half; ty++)
                for (int tx = -half; tx <= half; tx++)
                    SetPixelSafe(tex, x0 + tx, y0 + ty, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawHLine(Texture2D tex, int x0, int x1, int y, Color c)
    {
        for (int x = x0; x <= x1; x++) SetPixelSafe(tex, x, y, c);
    }

    private static void DrawDashedLine(Texture2D tex, int x0, int y0, int x1, int y1,
                                       Color c, int thickness, int dashLen = 8, int gapLen = 5)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;
        float nx = dx / len, ny = dy / len, dist = 0f;
        bool  draw = true;
        while (dist < len)
        {
            float seg = Mathf.Min(dist + (draw ? dashLen : gapLen), len);
            if (draw)
            {
                int ax = Mathf.RoundToInt(x0 + dist * nx), ay = Mathf.RoundToInt(y0 + dist * ny);
                int bx = Mathf.RoundToInt(x0 + seg   * nx), by = Mathf.RoundToInt(y0 + seg   * ny);
                DrawLine(tex, ax, ay, bx, by, c, thickness);
            }
            dist = seg; draw = !draw;
        }
    }

    private static void DrawDot(Texture2D tex, int cx, int cy, int r, Color c)
    {
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    SetPixelSafe(tex, x, y, c);
    }

    private static void DrawFilledRect(Texture2D tex, int x, int y, int w, int h, Color c)
    {
        for (int ry = y; ry < y + h; ry++)
            for (int rx = x; rx < x + w; rx++)
                SetPixelSafe(tex, rx, ry, c);
    }

    private static void DrawRectBorder(Texture2D tex, int x, int y, int w, int h, Color c)
    {
        for (int rx = x; rx < x + w; rx++) { SetPixelSafe(tex, rx, y, c); SetPixelSafe(tex, rx, y + h - 1, c); }
        for (int ry = y; ry < y + h; ry++) { SetPixelSafe(tex, x, ry, c); SetPixelSafe(tex, x + w - 1, ry, c); }
    }

    private static void DrawCenteredText(Texture2D tex, string text, int cx, int cy, Color c, int scale)
    {
        int charW  = (3 + 1) * scale;
        int totalW = text.Length * charW - scale;
        DrawText(tex, text, cx - totalW / 2, cy + (5 * scale) / 2, c, scale);
    }

    private static void DrawText(Texture2D tex, string text, int startX, int startY, Color c, int scale)
    {
        int x = startX;
        foreach (char ch in text.ToUpperInvariant())
        {
            if (!Font.TryGetValue(ch, out var glyph)) { x += 4 * scale; continue; }
            for (int row = 0; row < 5; row++)
            {
                byte bits = glyph[row];
                for (int col = 0; col < 3; col++)
                {
                    if ((bits >> (2 - col) & 1) == 0) continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                            SetPixelSafe(tex, x + col * scale + sx, startY - row * scale - sy, c);
                }
            }
            x += (3 + 1) * scale;
        }
    }
}
