using UnityEngine;

/// <summary>
/// ScriptableObject that defines the electric panel puzzle configuration:
/// correct solution mapping and per-wire visual colors.
/// Create via right-click → Create → Puzzle → Electric Puzzle Data.
/// </summary>
[CreateAssetMenu(fileName = "ElectricPuzzleData", menuName = "Puzzle/Electric Puzzle Data")]
public class ElectricPuzzleData : ScriptableObject
{
    [Tooltip("solution[i] = index of the neutral terminal that colored terminal i must connect to. " +
             "Array length must be exactly 6.")]
    [SerializeField] private int[] _solution = { 3, 5, 1, 4, 0, 2 };

    [Tooltip("Color of each wire. Index matches the colored terminal index (0..5).")]
    [SerializeField] private Color[] _wireColors =
    {
        Color.red,
        new Color(1f, 0.5f, 0f),  // orange
        Color.yellow,
        Color.green,
        Color.blue,
        Color.magenta,
    };

    /// <summary>Correct neutral-terminal index for each colored terminal (length = 6).</summary>
    public int[] Solution => _solution;

    /// <summary>Wire color for each colored terminal (length = 6).</summary>
    public Color[] WireColors => _wireColors;
}
