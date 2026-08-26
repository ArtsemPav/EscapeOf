using System;
using System.Collections;
using ChemicalPuzzle;
using UnityEngine;

/// <summary>
/// Coordinates the final door puzzle logic: medallion placement, victory
/// detection, activation animation, and save/restore.
///
/// Phase 1 (current): place 6 medallions into door holes. Any medallion can
/// go into any hole. Free retrieval until all 6 are in the correct positions.
/// When all 6 are correct → switch to Overview camera → play activation
/// animation → wait → SetSolved.
///
/// Phase 2 (skull — to be added later): after activation, descend skull,
/// switch to Skull camera, insert 2 remaining medallions into skull eyes,
/// then play door-open animation (possibly via Cinemachine Timeline).
///
/// Save ID: "final_door_puzzle"
/// Saves: solved flag + placedItemIds for all 6 door holes.
/// </summary>
[DefaultExecutionOrder(-7)]
public class FinalDoorPuzzleInteraction : MonoBehaviour, ISaveable, IPuzzleDropHandler, IPuzzleExitGuard
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Controller")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Door Holes (6)")]
    [Tooltip("Assign in order matching _doorMedallionOrder: 0..5.")]
    [SerializeField] private MedallionHole[] _doorHoles;

    [Tooltip("Correct ItemData for each door hole, in order 0..5.")]
    [SerializeField] private ItemData[] _doorMedallionOrder;

    [Header("All Medallions")]
    [Tooltip("All ItemData used by this puzzle — used for save restoration. Order does not matter.")]
    [SerializeField] private ItemData[] _allMedallions;

    [Header("Activation")]
    [Tooltip("Animator that plays the activation sequence when all 6 medallions are correct. Optional.")]
    [SerializeField] private Animator _activationAnimator;

    [Tooltip("Trigger parameter name in the activation animator.")]
    [SerializeField] private string _activationTrigger = "Activate";

    [Tooltip("Animator on the Scull object. Plays 'scullMove' after the medallion puzzle is solved.")]
    [SerializeField] private Animator _scullAnimator;

    [Tooltip("Name of the scull animation state to play after switching to the overview camera.")]
    [SerializeField] private string _scullMoveStateName = "scullMove";

    [Tooltip("How long to stay on the Overview camera during activation before calling SetSolved (seconds).")]
    [SerializeField, Min(0f)] private float _activationDuration = 3f;

    [Header("Cinematic Fade")]
    [Tooltip("Duration of the screen fade to/from black during the cinematic sequence.")]
    [SerializeField, Min(0.1f)] private float _fadeDuration = 1f;

    [Header("Solved Visuals")]
    [Tooltip("Activated when the puzzle is solved.")]
    [SerializeField] private GameObject _solvedObject;

    [Header("Sounds")]
    [SerializeField] private AudioClip _activationClip;
    [SerializeField] private AudioClip _solvedClip;
    [SerializeField] private AudioClip _coinDropClip;
    [SerializeField] private AudioClip _coinPickupClip;

    [Header("Sound Volumes")]
    [SerializeField, Range(0f, 1f)] private float _activationVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _solvedVolume   = 1f;
    [SerializeField, Range(0f, 1f)] private float _coinDropVolume  = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _coinPickupVolume = 0.7f;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _solved;
    private bool _isActivating;
    private PuzzleSaveData? _pendingLoad;

    private int _animActivateHash;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Door holes in order 0..5.</summary>
    public MedallionHole[] DoorHoles => _doorHoles;

    /// <summary>Correct ItemData for each door hole, in order 0..5.</summary>
    public ItemData[] DoorMedallionOrder => _doorMedallionOrder;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "final_door_puzzle";

    public string GetSaveData()
    {
        var ui = GetUI();
        var holeStates = ui != null ? ui.GetHoleStates() : Array.Empty<ItemData>();
        var ids = new string[holeStates.Length];
        for (int i = 0; i < holeStates.Length; i++)
            ids[i] = holeStates[i]?.ItemId ?? string.Empty;

        return JsonUtility.ToJson(new PuzzleSaveData { solved = _solved, placedItemIds = ids });
    }

    public void LoadSaveData(string json)
    {
        _pendingLoad = JsonUtility.FromJson<PuzzleSaveData>(json);
    }

    [Serializable]
    private struct PuzzleSaveData
    {
        public bool solved;
        public string[] placedItemIds;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = _controller != null ? _controller : GetComponent<FinalDoorPuzzleController>();

        _animActivateHash = Animator.StringToHash(_activationTrigger);

        // Auto-populate door holes from children if the Inspector array was left empty.
        if (_doorHoles == null || _doorHoles.Length == 0 || System.Array.TrueForAll(_doorHoles, h => h == null))
            _doorHoles = GetComponentsInChildren<MedallionHole>(includeInactive: true);

        SubscribeToHoles();
        SaveManager.Instance?.Register(this);
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnEntered += HandleEntered;
            _controller.OnExited  += HandleExited;
            _controller.OnSolved  += HandleSolved;
        }

        var ui = GetUI();
        if (ui != null)
            ui.OnPuzzleSolved += HandlePuzzleSolved;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited  -= HandleExited;
            _controller.OnSolved  -= HandleSolved;
        }

        var ui = GetUI();
        if (ui != null)
            ui.OnPuzzleSolved -= HandlePuzzleSolved;
    }

    private void Start()
    {
        ApplyPendingLoad();
    }

    private void OnDestroy()
    {
        UnsubscribeFromHoles();
        SaveManager.Instance?.Unregister(this);
    }

    // ── Controller Event Handlers ─────────────────────────────────────────────

    private void HandleEntered()
    {
        var ui = GetUI();
        if (ui == null) return;

        ui.Populate(_doorMedallionOrder);

        // Camera is already set by FinalDoorSideInteractable — no switch needed here.
    }

    private void HandleExited()
    {
        // Nothing to clean up — the controller handles camera and input.
    }

    private void HandleSolved() { }

    // ── Puzzle Solved (from UI) ───────────────────────────────────────────────

    /// <summary>
    /// Called by FinalDoorPuzzleUI when all 6 door holes are filled correctly.
    /// Starts the cinematic sequence: fade → overview camera → scull animation →
    /// fade → return to player.
    /// </summary>
    private void HandlePuzzleSolved()
    {
        _solved = true;
        _isActivating = true;

        PlaySFX(_activationClip, _activationVolume);

        // Lock UI — no more drops or retrieval.
        var ui = GetUI();
        ui?.MarkSolved();

        StartCoroutine(CinematicSequenceRoutine());
    }

    /// <summary>
    /// Cinematic sequence:
    /// 1. Fade to black.
    /// 2. Instant cut to FinalDoorCameraAll (overview).
    /// 3. Fade in — player sees the door.
    /// 4. Play scullMove animation.
    /// 5. Wait for the animation to finish.
    /// 6. Fade to black.
    /// 7. Exit puzzle mode (returns camera to the player).
    /// 8. Fade in — player regains control.
    /// </summary>
    private IEnumerator CinematicSequenceRoutine()
    {
        // ── Phase 1: Fade to black ──────────────────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 2: Instant cut to overview camera (screen is black) ───────────
        _controller.SwitchToOverviewInstant();

        // Wait one frame so the brain processes the camera switch.
        yield return null;

        // ── Phase 3: Fade in — player sees the overview ─────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 4: Play the scullMove animation ───────────────────────────────
        if (_activationAnimator != null)
            _activationAnimator.SetTrigger(_animActivateHash);

        if (_scullAnimator != null)
            _scullAnimator.Play(_scullMoveStateName);

        // ── Phase 5: Wait for the scullMove animation to finish ─────────────────
        yield return WaitForAnimationFinish(_scullAnimator, _scullMoveStateName);

        // ── Phase 6: Fade to black again ────────────────────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        // ── Phase 7: Exit puzzle mode — camera returns to the player instantly ──
        _isActivating = false;

        if (_solvedObject != null)
            _solvedObject.SetActive(true);

        PlaySFX(_solvedClip, _solvedVolume);

        _controller?.ExitPuzzleModeInstant();
        _controller?.SetSolved();

        // Wait one frame so the brain processes the camera return.
        yield return null;

        // ── Phase 8: Fade in — player regains control ───────────────────────────
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Waits until the specified Animator has finished playing the given state.
    /// Falls back to _activationDuration if the animator or state is invalid.
    /// </summary>
    private IEnumerator WaitForAnimationFinish(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            yield return new WaitForSeconds(_activationDuration);
            yield break;
        }

        // Wait one frame for the animation to start.
        yield return null;

        // Wait until the animator enters the target state.
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
            yield return null;

        // Wait until the state has fully played.
        while (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;
    }

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// Delegates drop handling to FinalDoorPuzzleUI.
    /// PuzzleModeController finds this handler via GetComponentInChildren.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        var ui = GetUI();
        return ui != null && ui.HandleDrop(item, screenPosition, out _);
    }

    // ── IPuzzleExitGuard ──────────────────────────────────────────────────────

    /// <summary>
    /// Blocks Esc exit while the activation sequence is playing.
    /// </summary>
    public bool CanExitPuzzle() => !_isActivating;

    // ── Save / Restore ────────────────────────────────────────────────────────

    private void ApplyPendingLoad()
    {
        if (_pendingLoad == null) return;

        var load = _pendingLoad.Value;
        _pendingLoad = null;

        if (load.placedItemIds != null && load.placedItemIds.Length > 0)
        {
            var lookupItems = ResolveLookupItems();
            if (lookupItems == null || lookupItems.Length == 0)
            {
                Debug.LogWarning("[FinalDoorPuzzle] Cannot restore medallions: " +
                                 "_allMedallions, _doorMedallionOrder and InventorySystem.AllItems are all empty.");
            }
            else
            {
                var ui = GetUI();
                ui?.RestoreState(load.placedItemIds, lookupItems);
            }
        }

        if (load.solved)
        {
            _solved = true;

            var ui = GetUI();
            ui?.MarkSolved();

            if (_solvedObject != null)
                _solvedObject.SetActive(true);

            // Restore activation animator to end state.
            if (_activationAnimator != null)
            {
                _activationAnimator.SetTrigger(_animActivateHash);
                _activationAnimator.Play("Activated", 0, 1f);
            }

            // Restore scull animator to the end of scullMove.
            if (_scullAnimator != null)
                _scullAnimator.Play(_scullMoveStateName, 0, 1f);

            _controller?.SetSolved();
        }
    }

    /// <summary>
    /// Resolves the item lookup array for save restoration.
    /// Falls back from _allMedallions → _doorMedallionOrder → InventorySystem.AllItems.
    /// </summary>
    private ItemData[] ResolveLookupItems()
    {
        if (_allMedallions != null && _allMedallions.Length > 0)
            return _allMedallions;

        if (_doorMedallionOrder != null && _doorMedallionOrder.Length > 0)
            return _doorMedallionOrder;

        if (InventorySystem.Instance != null && InventorySystem.Instance.AllItems != null)
            return InventorySystem.Instance.AllItems;

        return Array.Empty<ItemData>();
    }

    // ── Hole Sound Subscriptions ──────────────────────────────────────────────

    private void SubscribeToHoles()
    {
        if (_doorHoles == null) return;
        foreach (var hole in _doorHoles)
        {
            if (hole == null) continue;
            hole.OnFilled    += OnCoinDropped;
            hole.OnRetrieved += OnCoinPickedUp;
        }
    }

    private void UnsubscribeFromHoles()
    {
        if (_doorHoles == null) return;
        foreach (var hole in _doorHoles)
        {
            if (hole == null) continue;
            hole.OnFilled    -= OnCoinDropped;
            hole.OnRetrieved -= OnCoinPickedUp;
        }
    }

    private void OnCoinDropped()  => PlaySFX(_coinDropClip,   _coinDropVolume);
    private void OnCoinPickedUp() => PlaySFX(_coinPickupClip, _coinPickupVolume);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FinalDoorPuzzleUI GetUI()
    {
        return GetComponent<FinalDoorPuzzleUI>();
    }

    private static void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip, volume);
    }
}
