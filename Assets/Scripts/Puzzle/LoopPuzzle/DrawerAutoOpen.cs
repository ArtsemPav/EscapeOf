using UnityEngine;

/// <summary>
/// Opens the reward drawer when the shared PuzzleSolvedCinematic plays.
/// Other puzzles move their reward via Animation Events on a cinematic
/// Animator; the paint puzzle drawer is a physics drawer (DrawerDrag),
/// so the cinematic's drawer event calls OpenDrawer() here instead.
/// </summary>
public class DrawerAutoOpen : MonoBehaviour
{
    [Tooltip("Drawer that plays the opening animation.")]
    [SerializeField] private DrawerDrag _drawer;

    [Tooltip("Duration of the drawer opening animation (seconds).")]
    [SerializeField, Min(0.1f)] private float _openDuration = 2f;

    [ContextMenu("Open Drawer")]
    public void OpenDrawer()
    {
        if (_drawer == null)
            _drawer = GetComponent<DrawerDrag>();

        _drawer?.AutoOpen(_openDuration);
    }
}
