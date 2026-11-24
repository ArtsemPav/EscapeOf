using UnityEngine;

/// <summary>
/// Attaches to a skull eye socket (coinLeft / CoinRight). Implements
/// <see cref="IPuzzleHoverTarget"/> so PuzzleCursor shows the hand cursor and
/// ScullPuzzlePhase highlights the coin while the player can retrieve it.
/// The actual highlight logic lives in ScullPuzzlePhase — this component
/// just forwards hover callbacks to it.
/// </summary>
public class ScullEyeHoverTarget : MonoBehaviour, IPuzzleHoverTarget
{
    [Tooltip("ScullPuzzlePhase that owns the eye state. Auto-found in parent if empty.")]
    [SerializeField] private ScullPuzzlePhase _phase;

    [Tooltip("0 for the left eye (coinLeft), 1 for the right eye (CoinRight).")]
    [SerializeField] private int _eyeIndex;

    /// <summary>Eye index this component reports to ScullPuzzlePhase.</summary>
    public int EyeIndex => _eyeIndex;

    private void Awake()
    {
        if (_phase == null)
            _phase = GetComponentInParent<ScullPuzzlePhase>();
    }

    public bool CanRetrieve => _phase != null && _phase.IsCoinInEye(_eyeIndex);

    public CrosshairMode HoverCrosshair => CrosshairMode.Hand;

    public void SetHover(bool on)
    {
        if (_phase != null)
            _phase.SetEyeHover(_eyeIndex, on);
    }
}
