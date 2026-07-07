using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector for PressurePuzzle.
/// Shows a real-time HelpBox with:
///   - The magnitudes that will be generated for the current lever count and step settings.
///   - The number of valid solution combinations under all constraints.
///   - The solution total range and resulting max angle.
///   - An error if not enough levers are present to satisfy the constraints.
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

        if (v.MinLeversOn > v.MaxActions)
        {
            EditorGUILayout.HelpBox(
                $"INVALID — Min Levers On In Solution ({v.MinLeversOn}) is greater than " +
                $"Max Actions ({v.MaxActions}).\n" +
                $"The puzzle would be unsolvable — increase Max Actions or reduce Min Levers On.",
                MessageType.Error);
        }

        if (!v.CanPickSolution)
        {
            EditorGUILayout.HelpBox(
                $"INVALID — cannot pick a valid solution.\n\n" +
                $"Levers found       : {v.LeverCount}\n" +
                $"Min Levers ON      : {v.MinLeversOn}\n" +
                $"Max actions        : {v.MaxActions}\n" +
                $"Solution total range: {v.MinSolutionTotal:0.#} – {v.MaxSolutionTotal:0.#}\n\n" +
                $"Add more levers, reduce Min Levers On, or widen the Solution Fraction range.",
                MessageType.Error);
        }
        else
        {
            float maxAngle = v.MaxTotal * v.SolveAngle / v.MinSolutionTotal;

            EditorGUILayout.HelpBox(
                $"Ready — {v.ValidCombinationCount} valid solution combinations\n" +
                $"Levers: {v.LeverCount}  |  ON per solution: {v.MinLeversOn}–{v.LeverCount - 1}\n" +
                $"Action limit: {v.MaxActions} toggles per attempt\n\n" +
                $"Magnitudes (sorted, shuffled each session):\n  [{magnitudeList}]\n" +
                $"Max total: {v.MaxTotal:0.#}\n" +
                $"Solution total range: {v.MinSolutionTotal:0.#} – {v.MaxSolutionTotal:0.#}\n" +
                $"Solve at {v.SolveAngle:0.#}°  |  Danger at {v.DangerAngle:0.#}°  |  Max angle ~{maxAngle:0.#}°",
                MessageType.Info);
        }
    }
}
