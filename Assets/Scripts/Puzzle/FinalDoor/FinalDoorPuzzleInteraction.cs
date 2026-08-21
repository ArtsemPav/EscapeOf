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

    [Tooltip("How long to stay on the Overview camera during activation before calling SetSolved (seconds).")]
    [SerializeField, Min(0f)] private float _activationDuration = 3f;

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
    /// Plays the activation sequence, then calls SetSolved.
    /// </summary>
    private void HandlePuzzleSolved()
    {
        _solved = true;
        _isActivating = true;

        PlaySFX(_activationClip, _activationVolume);

        // Lock UI — no more drops or retrieval.
        var ui = GetUI();
        ui?.MarkSolved();

        // Switch to overview camera to show the full door activation.
        _controller.SwitchToOverview();

        // Play activation animation if an animator is assigned.
        if (_activationAnimator != null)
            _activationAnimator.SetTrigger(_animActivateHash);

        StartCoroutine(ActivationSequenceRoutine());
    }

    /// <summary>
    /// Waits for the activation duration, then calls SetSolved.
    /// When the skull phase is added later, this is where the transition
    /// to Phase 2 will happen instead of SetSolved.
    /// </summary>
    private IEnumerator ActivationSequenceRoutine()
    {
        yield return new WaitForSeconds(_activationDuration);

        _isActivating = false;

        if (_solvedObject != null)
            _solvedObject.SetActive(true);

        PlaySFX(_solvedClip, _solvedVolume);

        _controller?.SetSolved();
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
            var ui = GetUI();
            ui?.RestoreState(load.placedItemIds, _allMedallions);
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

            _controller?.SetSolved();
        }
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
