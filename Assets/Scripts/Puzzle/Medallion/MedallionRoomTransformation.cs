using System;
using System.Collections;
using UnityEngine;
using Escape.Core;

/// <summary>
/// Post-solve transformation of Room 1, triggered by the Chinese box puzzle.
///
/// The sequence does NOT start when the puzzle is solved — that would soft-lock the
/// save: the door locks and the player could lose access to the room before picking
/// up the medallion revealed by the opened box. Instead the sequence waits until the
/// puzzle is solved AND the player has picked up the medallion (PickableItem destroys
/// itself on pickup and on loading a save where it was collected).
///
/// Sequence when both conditions are met:
///   1. The room door closes and locks (DoorInteraction.CloseAndLock) with a 2D close sound.
///   2. Room lights lerp to a cold blue tone while the crystals grow.
///   3. Crystals (disabled in the scene) are activated and grow from zero to their
///      authored scale, each at its own speed, while a looping ice-cracking sound plays.
///   4. When all crystals have finished growing, the vent grill detaches and falls
///      to the floor (authored GrillFall animation); a chain impact sound plays on landing.
///   5. After the grill lands, a one-shot 3D whisper sound plays from the vent.
///
/// When a save already has both the puzzle solved and the medallion collected, the
/// final state is applied instantly in Start() — no sounds, no animation.
/// </summary>
[DefaultExecutionOrder(-3)]
public class MedallionRoomTransformation : MonoBehaviour, ISaveable
{
    [Header("Save Settings")]
    [SerializeField] private string _saveId = "medallion_room_transformation";

    [Header("References")]
    [Tooltip("PuzzleModeController of the Chinese box puzzle (same GameObject by default).")]
    [SerializeField] private PuzzleModeController _puzzle;

    [Tooltip("Door to the room — closed and locked when the puzzle is solved.")]
    [SerializeField] private DoorInteraction _roomDoor;

    [Header("Crystals")]
    [Tooltip("Root of the crystal group, disabled until the puzzle is solved. " +
             "Activated on solve so the individual crystals can grow.")]
    [SerializeField] private GameObject _crystalsRoot;

    [Tooltip("Disabled crystal objects. Each grows from zero to its authored local scale.")]
    [SerializeField] private Transform[] _crystals;

    [Tooltip("Minimum growth duration, seconds (randomized between min and max per crystal).")]
    [SerializeField, Min(0.1f)] private float _crystalGrowthDurationMin = 3f;

    [Tooltip("Maximum growth duration, seconds (randomized between min and max per crystal).")]
    [SerializeField, Min(0.1f)] private float _crystalGrowthDurationMax = 8f;

    [Tooltip("Growth easing — how fast crystals expand near the end.")]
    [SerializeField] private AnimationCurve _growthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Crystals Sound")]
    [Tooltip("Looping sound played while the crystals grow (2D — audible everywhere in the room).")]
    [SerializeField] private AudioClip _iceCrackingClip;

    [SerializeField, Range(0f, 1f)] private float _iceCrackingVolume = 0.9f;

    [Header("Vent Grill")]
    [Tooltip("Vent grill that detaches and falls to the floor after the crystals are grown.")]
    [SerializeField] private Transform _ventGrill;

    [Tooltip("Animator on the grill. Must have a bool parameter 'IsFall'. " +
             "The Fall state must end with the grill resting on the floor — no transition out.")]
    [SerializeField] private Animator _ventGrillAnimator;

    [Tooltip("Sound played when the grill hits the floor (at the end of the fall animation).")]
    [SerializeField] private AudioClip _grillImpactClip;

    [SerializeField, Range(0f, 1f)] private float _grillImpactVolume = 1f;

    [Header("Medallion Pickup Gate")]
    [Tooltip("PickableItem of the medallion revealed by the opened box. The room transformation " +
             "waits until this item is picked up — otherwise the locked door would soft-lock the save.")]
    [SerializeField] private PickableItem _medallionPickable;

    [Header("Door Sound")]
    [Tooltip("2D one-shot played through AudioManager when the door starts closing — the door's " +
             "own 3D motion loop is too quiet from a distance.")]
    [SerializeField] private AudioClip _doorCloseClip;

    [SerializeField, Range(0f, 1f)] private float _doorCloseVolume = 1f;

    [Header("Cold Light")]
    [Tooltip("The room lights lerp to this cold blue color while the crystals grow (and stay).")]
    [SerializeField] private Color _coldLightColor = new Color(0.35f, 0.55f, 1f);

    [Tooltip("How long the light color transition takes, seconds.")]
    [SerializeField, Min(0.1f)] private float _lightColorLerpDuration = 3f;

    [Header("Whisper")]
    [Tooltip("AudioSource with the whisper clip (Play On Awake off). Played once after the grill lands.")]
    [SerializeField] private AudioSource _whisperSource;

    // Authored local scale of each crystal — captured in Awake before anything is touched.
    private Vector3[] _crystalTargetScales;

    // True once the whole sequence has run (or was restored from a save).
    private bool _hasFired;

    private AudioSource _iceCrackingLoopSource;
    private Coroutine _sequenceRoutine;

    private static readonly int AnimIsFall   = Animator.StringToHash("IsFall");
    private static readonly int StateFall    = Animator.StringToHash("Fall");

    // Original emission/color state of the room lights — restored if needed later.
    private Light[] _roomLights;
    private Color[] _originalLightColors;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() => JsonUtility.ToJson(new SaveData { hasFired = _hasFired });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data.hasFired)
            ApplySolvedStateInstantly();
    }

    [Serializable]
    private struct SaveData
    {
        public bool hasFired;
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (_puzzle == null)
            _puzzle = GetComponent<PuzzleModeController>();

        // Remember authored scales so crystals can grow back to exactly this size.
        if (_crystals != null)
        {
            _crystalTargetScales = new Vector3[_crystals.Length];
            for (int i = 0; i < _crystals.Length; i++)
            {
                if (_crystals[i] != null)
                    _crystalTargetScales[i] = _crystals[i].localScale;
            }
        }

        // Crystals must be invisible until the puzzle is solved.
        SetCrystalsActive(false);

        // Cache room lights so their color can be lerped to the cold tone on solve.
        CacheRoomLights();

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // If the puzzle was already solved and restored from a save, the final state
        // was applied by LoadSaveData (SaveManager runs at execution order -10).
        // Otherwise subscribe for the live solve event.
        if (_hasFired) return;

        if (_puzzle != null && _puzzle.IsSolved)
        {
            if (IsMedallionTaken())
            {
                // Fully completed in a previous session but this component has no save
                // (e.g. save was made before this feature existed) — catch up instantly.
                ApplySolvedStateInstantly();
                return;
            }

            // Puzzle solved but the medallion is still in the box (load or same session) —
            // the transformation must wait for the pickup, otherwise the player would
            // never be able to grab the medallion through the locked door (soft-lock).
            SubscribeToMedallionPickup();
            return;
        }

        if (_puzzle != null)
            _puzzle.OnSolved += HandlePuzzleSolved;
    }

    /// <summary>
    /// True when the medallion was picked up (or its PickableItem was never assigned —
    /// fail-open so a broken reference cannot brick the sequence).
    /// </summary>
    private bool IsMedallionTaken()
    {
        // A picked-up PickableItem destroys itself — a destroyed reference means taken.
        return _medallionPickable == null;
    }

    private void OnDestroy()
    {
        if (_puzzle != null)
            _puzzle.OnSolved -= HandlePuzzleSolved;

        if (_medallionPickable != null)
            _medallionPickable.OnPickedUp -= HandleMedallionPickedUp;

        SaveManager.Instance?.Unregister(this);
    }

    // ── Solve + Pickup Gate ───────────────────────────────────────────────────

    private bool _puzzleSolvedLive;
    private bool _subscribedToPickup;

    /// <summary>
    /// First gate of the sequence: the puzzle was solved. Does NOT start the transformation —
    /// the player must first pick up the medallion from the opened box, otherwise the
    /// locked door would soft-lock the save.
    /// </summary>
    private void HandlePuzzleSolved()
    {
        if (_hasFired || _puzzleSolvedLive) return;
        _puzzleSolvedLive = true;

        SubscribeToMedallionPickup();
    }

    /// <summary>Subscribes to the medallion pickup, resolving the reference if it was auto-destroyed on load.</summary>
    private void SubscribeToMedallionPickup()
    {
        if (_subscribedToPickup) return;
        _subscribedToPickup = true;

        if (_medallionPickable != null)
            _medallionPickable.OnPickedUp += HandleMedallionPickedUp;
    }

    /// <summary>Second gate: the medallion is in the inventory — the room can now be sealed.</summary>
    private void HandleMedallionPickedUp()
    {
        if (_hasFired) return;
        _hasFired = true;

        if (_sequenceRoutine != null) StopCoroutine(_sequenceRoutine);
        _sequenceRoutine = StartCoroutine(SolvedSequenceRoutine());
    }

    private IEnumerator SolvedSequenceRoutine()
    {
        // 1. Close and lock the room door immediately — with an audible 2D close sound.
        if (_roomDoor != null)
        {
            _roomDoor.CloseAndLock();
            if (_doorCloseClip != null)
                AudioManager.Instance?.PlaySFX(_doorCloseClip, _doorCloseVolume);
        }

        // 2. Room lights turn cold blue while the crystals grow.
        StartColdLightTransition();

        // 3. Grow the crystals with a looping ice-cracking sound.
        StartIceCrackingLoop();

        if (_crystals != null && _crystals.Length > 0)
            yield return GrowCrystalsRoutine();

        StopIceCrackingLoop();

        // 4. Detach the vent grill — plays the Fall animation, impact sound on landing.
        if (_ventGrillAnimator != null)
            yield return PlayGrillFallRoutine();

        // 5. One-shot 3D whisper from the vent.
        if (_whisperSource != null)
            _whisperSource.Play();
    }

    /// <summary>Grows every crystal from zero to its authored scale, each with its own random duration.</summary>
    private IEnumerator GrowCrystalsRoutine()
    {
        SetCrystalsActive(true);

        int count = _crystals.Length;
        var durations = new float[count];
        float maxDuration = 0f;

        for (int i = 0; i < count; i++)
        {
            if (_crystals[i] != null)
            {
                _crystals[i].localScale = Vector3.zero;
                durations[i] = UnityEngine.Random.Range(_crystalGrowthDurationMin, _crystalGrowthDurationMax);
                if (durations[i] > maxDuration) maxDuration = durations[i];
            }
        }

        float elapsed = 0f;
        while (elapsed < maxDuration)
        {
            elapsed += Time.deltaTime;
            for (int i = 0; i < count; i++)
            {
                var crystal = _crystals[i];
                if (crystal == null) continue;

                float t = Mathf.Clamp01(elapsed / durations[i]);
                crystal.localScale = Vector3.LerpUnclamped(
                    Vector3.zero, _crystalTargetScales[i], _growthCurve.Evaluate(t));
            }

            yield return null;
        }

        // Snap to final scales.
        for (int i = 0; i < count; i++)
        {
            if (_crystals[i] != null)
                _crystals[i].localScale = _crystalTargetScales[i];
        }
    }

    // ── Vent Grill Fall (Animator-driven) ─────────────────────────────────────

    /// <summary>
    /// Plays the authored GrillFall animation, then plays the impact sound once the
    /// grill comes to rest. The final pose of the Fall clip defines where the grill lies.
    /// </summary>
    private IEnumerator PlayGrillFallRoutine()
    {
        _ventGrillAnimator.SetBool(AnimIsFall, true);

        // Wait one frame so the transition to "Fall" has started.
        yield return null;

        // Wait until the Animator has entered the Fall state.
        while (_ventGrillAnimator != null &&
               !_ventGrillAnimator.GetCurrentAnimatorStateInfo(0).IsName("Fall"))
            yield return null;

        // Wait until the Fall animation has fully played.
        while (_ventGrillAnimator != null &&
               _ventGrillAnimator.GetCurrentAnimatorStateInfo(0).IsName("Fall") &&
               _ventGrillAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;

        // The grill has landed.
        if (_grillImpactClip != null)
            AudioManager.Instance?.PlaySFX(_grillImpactClip, _grillImpactVolume);
    }

    // ── Ice Cracking Loop ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts a dedicated looping AudioSource on this GameObject. 2D (non-spatial) so the
    /// cracking is audible anywhere in the room regardless of the crystal group position.
    /// </summary>
    private void StartIceCrackingLoop()
    {
        if (_iceCrackingClip == null) return;

        _iceCrackingLoopSource = gameObject.AddComponent<AudioSource>();
        _iceCrackingLoopSource.clip = _iceCrackingClip;
        _iceCrackingLoopSource.volume = _iceCrackingVolume;
        _iceCrackingLoopSource.spatialBlend = 0f;
        _iceCrackingLoopSource.loop = true;
        _iceCrackingLoopSource.playOnAwake = false;
        _iceCrackingLoopSource.Play();
    }

    private void StopIceCrackingLoop()
    {
        if (_iceCrackingLoopSource == null) return;

        _iceCrackingLoopSource.Stop();
        Destroy(_iceCrackingLoopSource);
        _iceCrackingLoopSource = null;
    }

    // ── Cold Light Transition ─────────────────────────────────────────────────

    /// <summary>
    /// Finds the room root — the first ancestor that has a "Walls" child
    /// (works both in the scene hierarchy and in Prefab Mode).
    /// </summary>
    private Transform FindRoomRoot()
    {
        Transform current = transform.parent;
        while (current != null)
        {
            if (current.Find("Walls") != null)
                return current;
            current = current.parent;
        }

        return transform.root;
    }

    /// <summary>Caches all lights of this room only (not the whole environment).</summary>
    private void CacheRoomLights()
    {
        Transform roomRoot = FindRoomRoot();
        _roomLights = roomRoot.GetComponentsInChildren<Light>(true);
        _originalLightColors = new Color[_roomLights.Length];
        for (int i = 0; i < _roomLights.Length; i++)
        {
            if (_roomLights[i] != null)
                _originalLightColors[i] = _roomLights[i].color;
        }
    }

    /// <summary>Starts a smooth lerp of every room light color to the cold blue tone.</summary>
    private void StartColdLightTransition()
    {
        if (_roomLights == null || _roomLights.Length == 0) return;
        StartCoroutine(LerpRoomLightsRoutine());
    }

    private IEnumerator LerpRoomLightsRoutine()
    {
        var startColors = new Color[_roomLights.Length];
        for (int i = 0; i < _roomLights.Length; i++)
            startColors[i] = _roomLights[i] != null ? _roomLights[i].color : Color.white;

        float elapsed = 0f;
        while (elapsed < _lightColorLerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = _growthCurve.Evaluate(Mathf.Clamp01(elapsed / _lightColorLerpDuration));

            for (int i = 0; i < _roomLights.Length; i++)
            {
                if (_roomLights[i] != null)
                    _roomLights[i].color = Color.Lerp(startColors[i], _coldLightColor, t);
            }

            yield return null;
        }

        for (int i = 0; i < _roomLights.Length; i++)
        {
            if (_roomLights[i] != null)
                _roomLights[i].color = _coldLightColor;
        }
    }

    // ── Instant Restore (save load) ───────────────────────────────────────────

    /// <summary>Applies the end state without animation or sound — used when loading a solved save.</summary>
    private void ApplySolvedStateInstantly()
    {
        if (_hasFired) return;
        _hasFired = true;

        // Door state is persisted by DoorInteraction's own save — nothing to do here.

        // Crystals: active at full authored scale.
        if (_crystalsRoot != null)
            _crystalsRoot.SetActive(true);

        if (_crystals != null)
        {
            for (int i = 0; i < _crystals.Length; i++)
            {
                if (_crystals[i] == null) continue;
                _crystals[i].gameObject.SetActive(true);
                if (_crystalTargetScales != null && i < _crystalTargetScales.Length)
                    _crystals[i].localScale = _crystalTargetScales[i];
            }
        }

        // Lights: cold blue immediately — no lerp on load.
        if (_roomLights != null)
        {
            for (int i = 0; i < _roomLights.Length; i++)
            {
                if (_roomLights[i] != null)
                    _roomLights[i].color = _coldLightColor;
            }
        }

        // Grill: jump straight to the final pose of the Fall animation — no play.
        if (_ventGrillAnimator != null)
        {
            _ventGrillAnimator.SetBool(AnimIsFall, true);
            _ventGrillAnimator.Play(StateFall, 0, 1f);
        }

        // Whisper is not replayed on load (one-shot for the live session only).
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Activates or deactivates all crystal GameObjects.</summary>
    private void SetCrystalsActive(bool active)
    {
        if (_crystalsRoot != null)
            _crystalsRoot.SetActive(active);

        if (_crystals == null) return;

        foreach (var crystal in _crystals)
        {
            if (crystal != null)
                crystal.gameObject.SetActive(active);
        }
    }
}
