using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector for PressurePuzzle.
/// Shows a real-time HelpBox with:
///   - The magnitudes that will be generated for the current lever count and step settings.
///   - The number of valid solution combinations under the MinLeversOnInSolution constraint.
///   - An error if not enough levers are present to satisfy the constraint.
///
/// Lever values are generated at runtime by PressurePuzzle.GenerateAndAssignLeverValues(),
/// so the puzzle is always solvable by construction — no manual per-lever values needed.
/// </summary>
[CustomEditor(typeof(PressurePuzzle))]
public class PressurePuzzleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var puzzle = (PressurePuzzle)target;
        var v      = puzzle.GetEditorValidation();

        EditorGUILayout.Space(6f);

        if (v.LeverCount == 0)
        {
            EditorGUILayout.HelpBox(
                "No PressureLever children found.\nAdd child GameObjects with PressureLever components.",
                MessageType.Warning);
            return;
        }

        string magnitudeList = string.Join(", ", System.Array.ConvertAll(v.Magnitudes, m => m.ToString("0.#")));

        if (!v.CanPickSolution)
        {
            EditorGUILayout.HelpBox(
                $"INVALID — cannot pick a valid solution.\n\n" +
                $"Levers found     : {v.LeverCount}\n" +
                $"Min Levers ON    : {v.MinLeversOn}\n" +
                $"Minimum required : {v.MinLeversOn * 2} levers total\n\n" +
                $"Add more levers or reduce Min Levers On In Solution.",
                MessageType.Error);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Ready — {v.ValidCombinationCount} valid solution combinations\n" +
                $"Levers: {v.LeverCount}  |  ON per solution: {v.MinLeversOn}–{v.LeverCount - v.MinLeversOn}\n\n" +
                $"Magnitudes (sorted, shuffled each session):\n  [{magnitudeList}]\n" +
                $"Total dial range: ±{v.TotalRange:0.#}",
                MessageType.Info);
        }
    }
}
