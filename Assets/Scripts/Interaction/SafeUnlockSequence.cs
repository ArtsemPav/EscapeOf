using UnityEngine;
using System.Collections;

/// <summary>
/// Sequence controller to handle the safe opening process.
/// 1. Exits puzzle mode.
/// 2. Rotates the handle.
/// 3. Unlocks and opens the door.
/// </summary>
public class SafeUnlockSequence : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RotateOnTrigger _handleRotate;
    [SerializeField] private Escape.Core.DoorInteraction _door;
    [SerializeField] private PuzzleModeController _puzzleMode;

    [Header("Settings")]
    [SerializeField] private float _delayBeforeHandle = 0.5f;
    [SerializeField] private float _delayBeforeDoor = 1.0f;

    private void Awake()
    {
        if (_handleRotate == null) _handleRotate = GetComponentInChildren<RotateOnTrigger>();
        if (_door == null) _door = GetComponentInChildren<Escape.Core.DoorInteraction>();
        if (_puzzleMode == null) _puzzleMode = GetComponentInParent<PuzzleModeController>();
    }

    /// <summary>
    /// Starts the sequence of unlocking the safe.
    /// </summary>
    public void StartSequence()
    {
        StartCoroutine(UnlockSequenceRoutine());
    }

    private IEnumerator UnlockSequenceRoutine()
    {
        // 1. Exit Puzzle Mode immediately
        if (_puzzleMode != null)
        {
            _puzzleMode.ExitPuzzleMode();
        }

        yield return new WaitForSeconds(_delayBeforeHandle);

        // 2. Rotate Handle
        if (_handleRotate != null)
        {
            _handleRotate.TriggerRotation();
        }

        yield return new WaitForSeconds(_delayBeforeDoor);

        // 3. Unlock and Open Door
        if (_door != null)
        {
            _door.UnlockAndOpen();
        }
    }
}
