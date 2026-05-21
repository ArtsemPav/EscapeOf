using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Root controller for the Metamorf puzzle.
/// Win Condition: Each specified decor cylinder must intersect its assigned reward trigger.
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class MetamorfPuzzleController : MonoBehaviour
{
    [Serializable]
    public struct CylinderTargetPair
    {
        [Tooltip("The decor cylinder (e.g., Cylinder.001).")]
        public MetamorfDecorCylinder cylinder;
        [Tooltip("The specific trigger area for this cylinder.")]
        public Collider rewardTrigger;
    }

    [Header("Win Condition")]
    [Tooltip("List of pairs specifying which cylinder must reach which reward trigger.")]
    [SerializeField] private List<CylinderTargetPair> _targetPairs;

    [Header("Events")]
    [Tooltip("Fired once when the puzzle is solved.")]
    public UnityEvent OnPuzzleSolved;

    private MetamorfCylinderButton[] _buttons;
    private PuzzleModeController _puzzleModeController;
    private bool _isSolved;

    private void Start()
    {
        _puzzleModeController = GetComponent<PuzzleModeController>();
        _buttons = GetComponentsInChildren<MetamorfCylinderButton>(includeInactive: true);

        foreach (MetamorfCylinderButton button in _buttons)
        {
            button.OnStateChanged += OnCylinderStateChanged;
        }

        if (_targetPairs == null || _targetPairs.Count == 0)
        {
            Debug.LogWarning($"[{nameof(MetamorfPuzzleController)}] Target Pairs are not assigned on {gameObject.name}.", this);
        }
    }

    private void OnDestroy()
    {
        if (_buttons == null) return;

        foreach (MetamorfCylinderButton button in _buttons)
        {
            if (button != null)
                button.OnStateChanged -= OnCylinderStateChanged;
        }
    }

    /// <summary>Called by each MetamorfCylinderButton after a rotation step completes.</summary>
    public void OnCylinderStateChanged()
    {
        if (_isSolved || _targetPairs == null || _targetPairs.Count == 0) return;

        bool allCorrect = true;
        foreach (var pair in _targetPairs)
        {
            if (pair.cylinder == null || pair.rewardTrigger == null)
            {
                allCorrect = false;
                break;
            }

            // Get the collider of the specific decor cylinder
            if (pair.cylinder.TryGetComponent<Collider>(out var cylinderCollider))
            {
                // Check if the specific cylinder intersects its specific reward trigger
                if (!pair.rewardTrigger.bounds.Intersects(cylinderCollider.bounds))
                {
                    allCorrect = false;
                    break;
                }
            }
            else
            {
                allCorrect = false;
                break;
            }
        }

        if (allCorrect)
        {
            _isSolved = true;
            Debug.Log($"[{nameof(MetamorfPuzzleController)}] Puzzle Solved! All target cylinders have reached their specific reward triggers.");
            OnPuzzleSolved?.Invoke();
            _puzzleModeController.SetSolved();
        }
    }
}
