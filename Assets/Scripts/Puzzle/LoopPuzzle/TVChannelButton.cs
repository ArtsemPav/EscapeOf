using UnityEngine;

/// <summary>
/// Interactable button that cycles through cameras on the TV screen.
/// Attach to any GameObject on the Interactable Layer with a collider.
/// Assign <see cref="_controller"/> to the PeepholeTVCamera that manages the cameras.
/// </summary>
public class TVChannelButton : MonoBehaviour, IInteractable
{
    [SerializeField] private PeepholeTVCamera  _controller;

    [Header("Puzzle Mode")]
    [Tooltip("PuzzleModeController this button belongs to. The button only responds while that puzzle mode is active. If not assigned, taken from the LoopPuzzleController in parents.")]
    [SerializeField] private PuzzleModeController _puzzleMode;

    private void Start()
    {
        if (_puzzleMode == null)
        {
            var loopController = GetComponentInParent<LoopPuzzleController>(true);
            _puzzleMode = loopController != null ? loopController.PuzzleMode : null;
        }
    }

    /// <summary>Advances to the next camera on the TV.</summary>
    public void Interact()
    {
        // General power off (enabled set by LoopPuzzleController.OnPowerStateChanged).
        // The button press animation still plays so the player sees the button respond.
        if (!enabled) return;
        // Puzzle mode gate: the TV switch only responds inside the puzzle view
        // (the peephole/TV mode itself counts as puzzle mode).
        if (_puzzleMode != null && !_puzzleMode.IsActive) return;
        _controller?.NextCamera();
    }

    public bool   IsPickable()      => false;
    public bool   UseLMBClick       => true;

    /// <summary>Returns the localized camera-switch hint.</summary>
    public string GetInteractText() => enabled
        ? Localization.Get(LocalizationKeys.InteractSwitchCamera)
        : Localization.Get(LocalizationKeys.InteractNoPower);
}
