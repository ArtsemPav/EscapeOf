using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Logic for a combination lock using mechanical cylinders.
/// Supports any number of cylinders and any number of symbols per cylinder.
/// Integrated with PuzzleModeController for state management and events.
/// Plays an open animation and sound when the correct combination is entered.
/// </summary>
public class MechanicalLock : MonoBehaviour, ISaveable
{
    // ── Constants ───────────────────────────────────────────────────────────────

    private const string DefaultOpenTrigger = "OpenLock";
    private const string DefaultOpenState = "Opening";
    private const float AnimationTimeout = 10f;

    // ── Inspector ───────────────────────────────────────────────────────────────

    [Header("Combination Settings")]
    [SerializeField] private int[] _correctCombination = new int[5] { 1, 2, 3, 4, 5 };

    [Header("Save")]
    [Tooltip("Unique ID for saving the state of lock cylinders (their current rotation values).")]
    [SerializeField] private string _saveId;

    [Header("References")]
    [SerializeField] private PuzzleModeController _puzzleController;
    [SerializeField] private LockCylinder[] _cylinders;

    [Header("Animator")]
    [Tooltip("Animator of the lock mechanism. Auto-found in children if not assigned.")]
    [SerializeField] private Animator _lockAnimator;

    [Tooltip("Trigger parameter name that starts the lock-open animation.")]
    [SerializeField] private string _openTriggerParameter = DefaultOpenTrigger;

    [Tooltip("Animator state name to poll for open animation completion.")]
    [SerializeField] private string _openStateName = DefaultOpenState;

    [Header("Audio")]
    [SerializeField] private AudioClip _openClip;
    [SerializeField, Range(0f, 1f)] private float _openVolume = 1f;

    [Header("Rewards")]
    [Tooltip("Items added to inventory after the puzzle is solved. Given one by one.")]
    [SerializeField] private ItemData[] _rewardItems;

    // ── State ───────────────────────────────────────────────────────────────────

    private Camera _mainCamera;
    private bool _isProcessing;
    private bool _wasActiveLastFrame;

    public string SaveId => _saveId;

    // ── Unity Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _mainCamera = Camera.main;
        if (_puzzleController == null)
            _puzzleController = GetComponent<PuzzleModeController>();

        AutoResolveReferences();

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_puzzleController != null && _puzzleController.IsSolved)
        {
            RestoreSolvedState();
        }
        else if (_puzzleController != null && !_puzzleController.IsSolved)
        {
            RandomizeCylinders();
        }
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_isProcessing)
            return;

        if (_puzzleController != null && _puzzleController.IsActive && !_puzzleController.IsSolved)
        {
            // Skip input on the first frame the puzzle becomes active
            // so the interaction click doesn't also rotate a cylinder
            if (!_wasActiveLastFrame)
            {
                _wasActiveLastFrame = true;
                return;
            }

            HandleInput();
        }
        else
        {
            _wasActiveLastFrame = false;
        }
    }

    // ── Auto-Resolve ────────────────────────────────────────────────────────────

    /// <summary>Finds Animator reference from child objects if not assigned.</summary>
    private void AutoResolveReferences()
    {
        if (_lockAnimator == null)
            _lockAnimator = GetComponentInChildren<Animator>();
    }

    // ── Input ───────────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        bool lmb = Mouse.current.leftButton.wasPressedThisFrame;
        bool rmb = Mouse.current.rightButton.wasPressedThisFrame;

        if (lmb || rmb)
        {
            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                LockCylinder cylinder = hit.collider.GetComponentInParent<LockCylinder>();
                if (cylinder != null && Array.IndexOf(_cylinders, cylinder) != -1)
                {
                    // LMB -> rotate one step forward, RMB -> rotate one step backward
                    cylinder.Rotate(!lmb);
                    CheckCombination();
                }
            }
        }
    }

    // ── Combination ─────────────────────────────────────────────────────────────

    private void RandomizeCylinders()
    {
        bool isCorrect;
        do
        {
            isCorrect = true;
            for (int i = 0; i < _cylinders.Length; i++)
            {
                if (_cylinders[i] != null)
                {
                    int randomValue = UnityEngine.Random.Range(0, _cylinders[i].SymbolCount);
                    _cylinders[i].SetValue(randomValue);

                    if (i < _correctCombination.Length && randomValue != _correctCombination[i])
                    {
                        isCorrect = false;
                    }
                }
            }
            // If we accidentally rolled the correct combination, try again
        } while (isCorrect && _cylinders.Length == _correctCombination.Length);
    }

    private void CheckCombination()
    {
        if (_cylinders.Length != _correctCombination.Length) return;

        for (int i = 0; i < _cylinders.Length; i++)
        {
            if (_cylinders[i].CurrentValue != _correctCombination[i])
            {
                return;
            }
        }

        Solve();
    }

    // ── Solve Flow ──────────────────────────────────────────────────────────────

    private void Solve()
    {
        _isProcessing = true;

        AudioManager.Instance?.PlaySFX(_openClip, _openVolume);

        if (_lockAnimator != null && gameObject.activeInHierarchy)
        {
            StartCoroutine(PlayOpenAnimationThenFinish());
        }
        else
        {
            FinishSolve();
        }
    }

    /// <summary>
    /// Plays the lock-open animation, waits for it to complete,
    /// then notifies PuzzleModeController.
    /// </summary>
    private IEnumerator PlayOpenAnimationThenFinish()
    {
        _lockAnimator.SetTrigger(_openTriggerParameter);

        // Wait one frame so the transition has started
        yield return null;

        // Wait until the Animator has entered the open state
        float elapsed = 0f;
        while (_lockAnimator != null &&
               !_lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(_openStateName) &&
               elapsed < AnimationTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Wait until the open animation has fully played
        while (_lockAnimator != null &&
               _lockAnimator.GetCurrentAnimatorStateInfo(0).IsName(_openStateName) &&
               _lockAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            yield return null;
        }

        FinishSolve();
    }

    /// <summary>
    /// Notifies PuzzleModeController that the puzzle is solved and grants reward items.
    /// </summary>
    private void FinishSolve()
    {
        _isProcessing = false;

        StartCoroutine(GrantRewardItemsRoutine());

        SaveManager.Instance?.Save();
        _puzzleController?.SetSolved();
    }

    /// <summary>
    /// Shows each reward item via ItemInspector one by one.
    /// When the player confirms pickup, the item is added to inventory
    /// and the next item is shown.
    /// </summary>
    private IEnumerator GrantRewardItemsRoutine()
    {
        if (_rewardItems == null || _rewardItems.Length == 0)
            yield break;

        foreach (var item in _rewardItems)
        {
            if (item == null) continue;
            if (InventorySystem.Instance != null && InventorySystem.Instance.HasItem(item))
                continue;

            bool confirmed = false;

            if (ItemInspector.Instance != null)
            {
                ItemInspector.Instance.BeginInspection(item, null, _ =>
                {
                    if (InventorySystem.Instance != null && !InventorySystem.Instance.AddItem(item))
                    {
                        Debug.LogWarning($"[MechanicalLock] Could not add '{item.itemName}' — inventory is full.");
                    }
                    confirmed = true;
                });

                yield return new WaitUntil(() => confirmed);
            }
            else
            {
                if (InventorySystem.Instance != null && !InventorySystem.Instance.AddItem(item))
                {
                    Debug.LogWarning($"[MechanicalLock] Could not add '{item.itemName}' — inventory is full.");
                }
            }
        }
    }

    // ── Solved Restore ──────────────────────────────────────────────────────────

    /// <summary>
    /// Silently restores the visual state when the puzzle was already solved on load.
    /// Fast-forwards the animator to the end of the open animation.
    /// </summary>
    private void RestoreSolvedState()
    {
        if (_lockAnimator != null)
        {
            _lockAnimator.Play(_openStateName, 0, 1f);
        }
    }

    // ── ISaveable Implementation ────────────────────────────────────────────────

    public string GetSaveData()
    {
        int[] values = new int[_cylinders.Length];
        for (int i = 0; i < _cylinders.Length; i++)
            values[i] = _cylinders[i].CurrentValue;

        return JsonUtility.ToJson(new LockSaveData
        {
            cylinderValues = values
        });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<LockSaveData>(json);

        if (data.cylinderValues != null && data.cylinderValues.Length == _cylinders.Length)
        {
            for (int i = 0; i < _cylinders.Length; i++)
            {
                _cylinders[i].SetValue(data.cylinderValues[i]);
            }
        }
    }

    [Serializable]
    private struct LockSaveData
    {
        public int[] cylinderValues;
    }

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
