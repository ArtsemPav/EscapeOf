using UnityEngine;

/// <summary>
/// Bridge between the shared PuzzleSolvedCinematic Animation Events and the
/// paint puzzle components that live on other GameObjects. Put on the same
/// GameObject as the cinematic Animator; wire Animation Events in the
/// cinematic clip to these methods.
/// </summary>
public class PaintCinematicSignals : MonoBehaviour
{
    [Tooltip("Drawers opened by the cinematic (top and bottom reward drawers).")]
    [SerializeField] private DrawerAutoOpen[] _drawers;

    [Tooltip("Solved room light faded in while the solved camera is shown.")]
    [SerializeField] private SolvedLightFade _solvedLight;

    [Tooltip("Puzzle mode controller on the ControlPanel. Exited under full black " +
             "when the cinematic switches back to the player camera.")]
    [SerializeField] private PuzzleModeController _puzzleMode;

    /// <summary>Animation Event: open the reward drawers.</summary>
    public void OnOpenDrawer()
    {
        if (_drawers == null) return;
        foreach (var drawer in _drawers)
            drawer?.OpenDrawer();
    }

    /// <summary>Animation Event: fade in the solved room light.</summary>
    public void OnShowSolvedLight()
    {
        if (_solvedLight == null) return;
        _solvedLight.gameObject.SetActive(true);
        _solvedLight.FadeIn();
    }

    /// <summary>Animation Event: fade out and deactivate the solved room light.</summary>
    public void OnHideSolvedLight()
    {
        if (_solvedLight == null) return;
        _solvedLight.FadeOut();
    }

    /// <summary>
    /// Animation Event: exit puzzle mode while the screen is fully black and the
    /// camera has already been cut back to the player. SetSolved marks the puzzle
    /// solved, exits puzzle mode and persists the state.
    /// </summary>
    public void OnExitPuzzleMode() => _puzzleMode?.SetSolved();
}
