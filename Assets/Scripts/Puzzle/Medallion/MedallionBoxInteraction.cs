using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the chinesBox GameObject.
/// Works with PuzzleModeController to manage puzzle state, camera, and input.
/// Handles medallion UI, sounds, and the box open animation.
///
/// Animation contract:
///   Animator must have a bool parameter "IsOpen".
///   Setting it to true plays the open animation once; the box stays in that pose permanently.
///
/// <para><b>Execution order: -7.</b> Must run after SaveManager (-10) so <see cref="LoadSaveData"/>
/// is called before <c>Start</c>, and before MedallionCollectionTracker (-5) so
/// <see cref="ApplyPendingLoad"/> fills the holes before the tracker's startup sync.</para>
/// </summary>
[DefaultExecutionOrder(-7)]
public class MedallionBoxInteraction : MonoBehaviour, ISaveable, IPuzzleDropHandler
{
    private static readonly int AnimIsOpen  = Animator.StringToHash("IsOpen");
    private static readonly int StateOpened = Animator.StringToHash("Opened");

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private PuzzleModeController _controller;

    [Tooltip("Root panel GameObject in Canvas.")]
    [SerializeField] private GameObject _panel;

    [Tooltip("GameObject to activate when the puzzle is solved (e.g. the 'solved' light).")]
    [SerializeField] private GameObject _solvedObject;

    [Header("Puzzle — Medallion Order")]
    [Tooltip("Assign in order: slot 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood. " +
             "Must match the MedallionHole expected items on Hole_0..4.")]
    [SerializeField] private ItemData[] _medallionOrder;

    [Header("Holes")]
    [Tooltip("All MedallionHole objects — used for coin sound subscriptions.")]
    [SerializeField] private MedallionHole[] _holes;

    [Header("Sounds")]
    [Tooltip("Played once when the player opens / inspects the box.")]
    [SerializeField] private AudioClip _openBoxClip;

    [Tooltip("Played once when the puzzle is solved and the box opens.")]
    [SerializeField] private AudioClip _solvedClip;

    [Tooltip("Played when a medallion is dropped into a hole.")]
    [SerializeField] private AudioClip _coinDropClip;

    [Tooltip("Played when a medallion is retrieved from a hole.")]
    [SerializeField] private AudioClip _coinPickupClip;

    [Header("Sound Volumes")]
    [SerializeField, Range(0f, 1f)] private float _openBoxVolume    = 1f;
    [SerializeField, Range(0f, 1f)] private float _solvedVolume     = 1f;
    [SerializeField, Range(0f, 1f)] private float _coinDropVolume   = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _coinPickupVolume = 0.7f;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _puzzleSolved;
    private bool _boxOpenedOnce;
    private Animator _animator;

    private PuzzleSaveData? _pendingLoad;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "medallion_puzzle";

    public string GetSaveData()
    {
        var boxUI      = _panel?.GetComponent<MedallionBoxUI>();
        var holeStates = boxUI?.GetHoleStates() ?? Array.Empty<ItemData>();

        var ids = new string[holeStates.Length];
        for (int i = 0; i < holeStates.Length; i++)
            ids[i] = holeStates[i]?.ItemId ?? string.Empty;

        return JsonUtility.ToJson(new PuzzleSaveData { solved = _puzzleSolved, placedItemIds = ids });
    }

    public void LoadSaveData(string json)
    {
        _pendingLoad = JsonUtility.FromJson<PuzzleSaveData>(json);
    }

    [Serializable]
    private struct PuzzleSaveData
    {
        public bool     solved;
        public string[] placedItemIds;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _animator   = GetComponent<Animator>();
        _controller = _controller != null ? _controller : GetComponent<PuzzleModeController>();

        // Auto-populate _holes from children if the Inspector array was left empty.
        if (_holes == null || _holes.Length == 0 || System.Array.TrueForAll(_holes, h => h == null))
            _holes = GetComponentsInChildren<MedallionHole>(includeInactive: true);

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

        SubscribeToHoles();
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited  -= HandleExited;
            _controller.OnSolved  -= HandleSolved;
        }

        UnsubscribeFromHoles();
    }

    private void Start()
    {
        ApplyPendingLoad();
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ── PuzzleModeController Event Handlers ───────────────────────────────────

    /// <summary>Opens the panel, plays the inspect sound once, and binds the drop handler.</summary>
    private void HandleEntered()
    {
        // Play open sound only the first time the box is inspected.
        if (!_boxOpenedOnce)
        {
            _boxOpenedOnce = true;
            PlaySFX(_openBoxClip, _openBoxVolume);
        }

        if (_panel != null)
            UIManager.Instance?.OpenPanel(_panel);

        var boxUI = _panel?.GetComponent<MedallionBoxUI>();
        if (boxUI != null)
        {
            boxUI.OnPuzzleSolved -= HandlePuzzleSolved;
            boxUI.OnPuzzleSolved += HandlePuzzleSolved;
            boxUI.Populate(_medallionOrder);
        }
    }

    /// <summary>Closes the panel when the player exits puzzle mode.</summary>
    private void HandleExited()
    {
        if (_panel != null)
            UIManager.Instance?.ClosePanel(_panel);
    }

    /// <summary>
    /// Called via OnSolved after SetSolved() completes (i.e. after the open animation).
    /// Sound and animation are already handled in HandlePuzzleSolved — this is intentionally a no-op.
    /// </summary>
    private void HandleSolved() { }

    // ── Hole Sound Subscriptions ──────────────────────────────────────────────

    private void SubscribeToHoles()
    {
        if (_holes == null) return;
        foreach (var hole in _holes)
        {
            if (hole == null) continue;
            hole.OnFilled    += OnCoinDropped;
            hole.OnRetrieved += OnCoinPickedUp;
        }
    }

    private void UnsubscribeFromHoles()
    {
        if (_holes == null) return;
        foreach (var hole in _holes)
        {
            if (hole == null) continue;
            hole.OnFilled    -= OnCoinDropped;
            hole.OnRetrieved -= OnCoinPickedUp;
        }
    }

    private void OnCoinDropped()  => PlaySFX(_coinDropClip,    _coinDropVolume);
    private void OnCoinPickedUp() => PlaySFX(_coinPickupClip,  _coinPickupVolume);

    // ── MedallionBoxUI Handler ────────────────────────────────────────────────

    private void HandlePuzzleSolved()
    {
        _puzzleSolved = true;

        if (_solvedObject != null)
            _solvedObject.SetActive(true);

        // Play the solved sound immediately when the puzzle is completed.
        PlaySFX(_solvedClip, _solvedVolume);

        // Trigger the open animation.
        if (_animator != null)
            _animator.SetBool(AnimIsOpen, true);

        // Wait for the animation to finish before returning camera to the player.
        StartCoroutine(WaitForOpenAnimationRoutine());
    }

    /// <summary>
    /// Waits until the Open animation finishes, then calls SetSolved to exit puzzle mode.
    /// This prevents the camera from returning to the player mid-animation.
    /// </summary>
    private IEnumerator WaitForOpenAnimationRoutine()
    {
        // Wait one frame so the transition to "Open" has started.
        yield return null;

        // Wait until the Animator has entered the "Open" state.
        while (_animator != null &&
               !_animator.GetCurrentAnimatorStateInfo(0).IsName("Open"))
            yield return null;

        // Wait until the "Open" animation has fully played.
        while (_animator != null &&
               _animator.GetCurrentAnimatorStateInfo(0).IsName("Open") &&
               _animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;

        // Animation is done — now mark solved, exit puzzle mode, and save.
        _controller?.SetSolved();
    }

    // ── IPuzzleDropHandler ────────────────────────────────────────────────────

    /// <summary>
    /// Delegates drop handling to MedallionBoxUI so that PuzzleModeController can find
    /// this handler via GetComponentInChildren without requiring MedallionBoxUI to be
    /// a child of this GameObject.
    /// </summary>
    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        var boxUI = _panel?.GetComponent<MedallionBoxUI>();
        return boxUI != null && boxUI.HandleDrop(item, screenPosition, out _);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Routes SFX through AudioManager singleton — consistent with all other puzzle sounds.</summary>
    private static void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip, volume);
    }

    private void ApplyPendingLoad()
    {
        if (_pendingLoad == null) return;

        var load = _pendingLoad.Value;
        _pendingLoad = null;

        if (load.placedItemIds != null && load.placedItemIds.Length > 0)
        {
            var boxUI = _panel?.GetComponent<MedallionBoxUI>();
            boxUI?.RestoreState(load.placedItemIds, _medallionOrder);
        }

        if (load.solved)
        {
            _puzzleSolved = true;

            if (_solvedObject != null)
                _solvedObject.SetActive(true);

            // Restore animator to open state immediately — no animation plays on load.
            if (_animator != null)
            {
                _animator.SetBool(AnimIsOpen, true);
                _animator.Play(StateOpened, 0, 1f);
            }

            _controller?.SetSolved();
        }
    }

}
