using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

    /// <summary>
    /// Opening cinematic: the player wakes up lying on the bed in Room 7.
    /// Runs only on a NEW game — the launch request comes through
    /// <see cref="SaveManager.RequestWakeUpIntro"/> (a loaded save skips it entirely).
    ///
    /// Phases:
    ///   1. Setup — the capsule stands on the floor at the stand point (gravity
    ///      and room visibility triggers stay valid); the CAMERA is taken over in
    ///      world space: lying on the bed;
    ///   2. Wake  — eyelids open; the intro Volume (blur / desaturation / darker
    ///      exposure) steps down the Focus Ladder, one step hidden under each blink;
    ///   3. Lying gaze — gaze beats: the door, then the extra targets (the note,
    ///      the desk) — all through the same LyingGazeBeatRoutine;
    ///   4. Rise  — ONE continuous motion from the lying pose: the head leads up
    ///      over the legs, leans forward, dips toward the feet spot mid-rise
    ///      (watching where the feet plant), then lifts to the stand gaze target
    ///      (the desk);
    ///   5. Focus — a slow drowsy turn to the final point (the FTUE book);
    ///      control is returned with the view EXACTLY where the cinematic ended.
    ///
    /// ESC at any point skips the remaining sequence (fast-forward to phase 5).
    /// All temporary post-processing lives in <see cref="_introVolume"/> — when it
    /// is disabled the game post-processing (global + room volumes) returns as is.
    /// </summary>
public class WakeUpIntroController : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("Temporary intro Volume (DoF blur / desaturation / darker exposure while asleep). " +
             "Must have a higher priority than game volumes. Disabled after the intro.")]
    [SerializeField] private Volume _introVolume;

    [Tooltip("Volume weight at the peak of the sleep state (1 = full effect).")]
    [SerializeField, Range(0f, 1f)] private float _sleepVolumeWeight = 1f;

    [Header("Sleep Look (runtime profile)")]
    [Tooltip("Gaussian blur start distance while asleep — everything closer blurs (m).")]
    [SerializeField, Min(0f)] private float _dofGaussianStart = 0f;

    [Tooltip("Gaussian blur full-on distance while asleep (m).")]
    [SerializeField, Min(0.1f)] private float _dofGaussianEnd = 3f;

    [Tooltip("Gaussian blur max radius while asleep (0..1).")]
    [SerializeField, Range(0.05f, 1f)] private float _dofMaxRadius = 0.8f;

    [Tooltip("Color saturation while asleep (negative = washed out).")]
    [SerializeField, Range(-100f, 0f)] private float _sleepSaturation = -55f;

    [Tooltip("Post exposure (stops) while asleep — slightly darker.")]
    [SerializeField, Range(-3f, 0f)] private float _sleepExposure = -0.7f;

    [Header("Bed (Room 7)")]
    [Tooltip("Lying pose point — child of the bed. Position = where the player's head rests, " +
             "forward = the direction the player faces while lying (feet → head).")]
    [SerializeField] private Transform _lyingPoint;

    [Tooltip("Standing spot beside the bed the player ends up at after rising.")]
    [SerializeField] private Transform _standPoint;

    [Tooltip("Point above the FTUE hints on the bed — the final gaze target.")]
    [SerializeField] private Transform _bedGazeTarget;

    [Tooltip("What the player sees when the eyes part open while still lying — " +
             "e.g. the ceiling lamp. If empty, the camera looks straight up at the ceiling.")]
    [SerializeField] private Transform _lyingGazeTarget;

    [Tooltip("The second lying gaze: while still lying, the view turns from the " +
             "first target to this one (e.g. the room door) before the player rises.")]
    [SerializeField] private Transform _lyingSecondGazeTarget;

    [Tooltip("What the player faces once standing (e.g. the desk): the view turns to " +
             "this target WHILE the body stands up. Empty = keep the gaze yaw.")]
    [SerializeField] private Transform _standGazeTarget;

    [Tooltip("Extra lying head turns after the second lying target (e.g. the hint note): " +
             "the player, still on the bed, turns his head through them in order.")]
    [SerializeField] private Transform[] _lyingExtraGazeTargets;

    [Tooltip("Scare prop (the maid) enabled while the player lies on the bed. Disabled the " +
             "moment the gaze lands on the note — it is only ever seen from the lying pose.")]
    [SerializeField] private GameObject _scareMaid;

    [Header("Wake-Up Timing")]
    [SerializeField, Min(0f)] private float _startDelay = 0.4f;
    [SerializeField, Min(0.1f)] private float _firstPartOpenDuration = 2.2f;
    [SerializeField, Min(0.1f)] private float _firstBlinkDuration = 0.9f;
    [SerializeField, Min(0.1f)] private float _finalOpenDuration = 3.5f;
    [SerializeField, Range(0f, 1f)] private float _firstPartOpenAmount = 0.45f;
    [SerializeField, Min(0.1f)] private float _postOpenBlinkDuration = 0.7f;

    [Header("Rise From The Bed")]
    [Tooltip("Height of the rise arc control point above the midpoint (m) — lifts the camera over the bed.")]
    [SerializeField, Min(0f)] private float _riseArcHeight = 0.7f;

    [Tooltip("How far below the capsule top the eye height sits (m) — matches the FPS camera offset.")]
    [SerializeField, Min(0f)] private float _cameraBelowEyeOffset = 0.4f;

    [Header("Get Up Choreography")]
    [Tooltip("Seconds of the continuous rise from the lying pose to the standing pose " +
             "(head leads up over the legs, dips toward the feet spot, lifts to the gaze target).")]
    [SerializeField, Min(0.1f)] private float _standUpDuration = 2.0f;

    [Tooltip("How strongly the head dips toward the feet spot mid-rise (0 = no dip, " +
             "1 = fully aiming at the feet). A person watches where he plants his feet.")]
    [SerializeField, Range(0f, 1f)] private float _standLookDownWeight = 0.8f;

    [Tooltip("How far the head leans forward into the rise direction (m, peak mid-rise).")]
    [SerializeField, Min(0f)] private float _standLeanDistance = 0.25f;

    [Header("Gaze")]
    [SerializeField, Min(0.1f)] private float _gazeHoldTime = 1.1f;

    [Header("Human Gaze")]
    [Tooltip("Rest range on each lying gaze beat (s, randomized per beat — a person " +
             "looks at some things faster than others).")]
    [SerializeField] private Vector2 _lyingGazeHoldRange = new Vector2(0.5f, 1.2f);

    [Tooltip("Amplitude of the natural head sway while the gaze rests (degrees).")]
    [SerializeField, Range(0f, 3f)] private float _headSwayDegrees = 0.7f;

    [Tooltip("Blur weight while the head MOVES between lying gaze targets — the groggy " +
             "defocus that resolves when the gaze settles on each target.")]
    [SerializeField, Range(0f, 1f)] private float _wanderDefocusWeight = 0.45f;

    [Tooltip("Max |pitch| the gaze sweep may aim at (degrees, positive down). " +
             "Prevents the camera from diving into the bed/floor or craning up through the ceiling.")]
    [SerializeField, Range(5f, 60f)] private float _maxGazePitch = 45f;

    [Header("Lamp Focus (lying)")]
    [Tooltip("Focus ladder: the volume weight applied after each wake blink — the step hides " +
             "under the closed lids. Deep sleep starts at 1; each blink moves to the next " +
             "entry. The LAST entry must be 0 (fully sharp). Keep pre-final entries at ~0.5+ — " +
             "a low DoF weight is visually indistinguishable from sharp and wastes a blink.")]
    [SerializeField] private float[] _focusLadder = { 0.6f, 0f };

    [Tooltip("Seconds each SOFT (pre-final) focus level holds before the next blink.")]
    [SerializeField, Min(0f)] private float _lampFocusHoldTime = 2f;

    [Tooltip("Seconds the fully SHARP lamp view holds before the turn to the door.")]
    [SerializeField, Min(0f)] private float _lampSharpHoldTime = 2f;

    [Header("Final Slow Turn")]
    [Tooltip("Seconds of the slow drowsy turn from the stand gaze target to the bed note.")]
    [SerializeField, Min(0.3f)] private float _slowTurnDuration = 1.4f;

    [Header("Audio")]
    [Tooltip("Source for the wake-up sound (gasp, breath). Plays once when the eyes part open.")]
    [SerializeField] private AudioSource _wakeSound;

    [Tooltip("Source for the room ambient loop, faded in over the intro.")]
    [SerializeField] private AudioSource _ambientSource;
    [SerializeField, Min(0.1f)] private float _ambientFadeInDuration = 3f;

    // One-shot flag: consumed from SaveManager (survives domain reload, unlike
    // a static field on this class) — see SaveManager.RequestWakeUpIntro.

    private FPSController _fps;
    private CharacterController _characterController;
    private EyelidOverlay _eyelids;
    private VolumeProfile _runtimeProfile;
    private bool _isPlaying;
    private bool _skipRequested;
    private float _ambientTargetVolume;
    private Vector3 _cameraWorldStart;
    private Quaternion _cameraStartRotation;
    private Vector3 _pinnedCameraPos;
    private Coroutine _pinRoutine;

    /// <summary>Standing eye height above the capsule root — matches the FPS rig camera offset.</summary>
    private float StandingEyeHeight => _fps.StandingHeight - _cameraBelowEyeOffset;

    private void Start()
    {
        // Route intro sources to the SFX bus unless they already have a mixer group from the Inspector.
        if (_wakeSound != null && _wakeSound.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_wakeSound);
        if (_ambientSource != null && _ambientSource.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_ambientSource);

        if (!SaveManager.ConsumeWakeUpIntroRequest())
        {
            enabled = false;
            return;
        }

        BeginIntro();
    }

    /// <summary>
    /// DEBUG/TEST entry: forces the intro to play immediately, ignoring the
    /// request flag. Used by the editor debug menu (Wake-Up Intro / Restart
    /// Intro In Place). Safe to call repeatedly — restarts the sequence.
    /// </summary>
    public void DebugPlayNow()
    {
        if (_isPlaying)
        {
            StopAllCoroutines();
            _isPlaying = false;
        }
        BeginIntro();
    }

    private void BeginIntro()
    {
        _fps = FindFirstObjectByType<FPSController>();
        if (_fps == null)
        {
            Debug.LogError("[WakeUpIntro] FPSController not found — intro aborted.", this);
            enabled = false;
            return;
        }
        _characterController = _fps.GetComponent<CharacterController>();
        _eyelids = EyelidOverlay.Instance != null
            ? EyelidOverlay.Instance
            : FindFirstObjectByType<EyelidOverlay>(FindObjectsInactive.Include);

        if (_lyingPoint == null || _standPoint == null || _bedGazeTarget == null)
        {
            Debug.LogError("[WakeUpIntro] Lying/stand/bed-gaze points are not assigned — intro aborted.", this);
            enabled = false;
            return;
        }

        SetScareMaid(true); // the maid watches from the start of the intro

        BuildRuntimeProfile();
        StartCoroutine(IntroRoutine());
    }

    /// <summary>Enables/disables the scare prop (the maid), if assigned.</summary>
    private void SetScareMaid(bool active)
    {
        if (_scareMaid != null) _scareMaid.SetActive(active);
    }

    /// <summary>
    /// Builds the sleep-look profile in code (same pattern as HorrorPostProcess).
    /// DoF blur + desaturation + darker exposure. Deliberately NO lens
    /// distortion: its gradual release reads as a fake zoom.
    /// The Volume weight blends between the base game look (0) and this (1).
    /// </summary>
    private void BuildRuntimeProfile()
    {
        _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();

        DepthOfField dof = _runtimeProfile.Add<DepthOfField>(true);
        dof.mode.overrideState = true;
        dof.mode.value = DepthOfFieldMode.Gaussian;
        dof.gaussianStart.overrideState = true;
        dof.gaussianStart.value = _dofGaussianStart;
        dof.gaussianEnd.overrideState = true;
        dof.gaussianEnd.value = _dofGaussianEnd;
        dof.gaussianMaxRadius.overrideState = true;
        dof.gaussianMaxRadius.value = _dofMaxRadius;

        ColorAdjustments color = _runtimeProfile.Add<ColorAdjustments>(true);
        color.postExposure.overrideState = true;
        color.postExposure.value = _sleepExposure;
        color.saturation.overrideState = true;
        color.saturation.value = _sleepSaturation;

        if (_introVolume != null)
        {
            // Proven pattern (HorrorPostProcess / PortalPlayerImpact): assign the
            // runtime instance as sharedProfile so the Volume manager blends it.
            _introVolume.isGlobal = true;
            _introVolume.priority = 300; // above game (100) and horror (200) volumes
            _introVolume.sharedProfile = _runtimeProfile;
        }
    }

    private IEnumerator IntroRoutine()
    {
        _isPlaying = true;
        float volumeTarget = _sleepVolumeWeight;

        // ── Phase 1: setup (screen is black from the loading screen handoff) ──
        InputManager.Instance?.SetPlayerInputEnabled(false);
        InputManager.Instance?.SetUIInputEnabled(false);
        if (InputManager.Instance != null)
        {
            // Defensive: a debug restart mid-intro may leave a stale subscription.
            InputManager.Instance.OnMenuPerformed -= OnMenuPressed;
            InputManager.Instance.OnMenuPerformed += OnMenuPressed;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        InteractionUI.Instance?.SetVisible(false);
        _fps.SetCinematicControl(true);

        // Lying pose: teleport onto the bed, yaw = feet→head direction,
        // camera pitched up at the ceiling. Runs behind the eyelids.
        PoseLying();

        if (_eyelids != null) _eyelids.CloseEyes();
        if (_introVolume != null)
        {
            _introVolume.enabled = true;
            _introVolume.weight = volumeTarget;
        }

        if (_ambientSource != null)
        {
            _ambientTargetVolume = _ambientSource.volume;
            _ambientSource.volume = 0f;
            _ambientSource.Play();
        }

        yield return new WaitForSeconds(_startDelay);

        // ── Phase 2: wake — lids part, blur fades down the FOCUS LADDER ────────
        // Each blink hides one ladder step: the blur drops WHILE the eyelids
        // cover the screen, so the step itself is never seen — the player only
        // perceives that every blink leaves the world a bit sharper.
        // First: a sleepy half-open (still fully asleep — full blur).
        if (_wakeSound != null) _wakeSound.Play();
        if (_ambientSource != null) StartCoroutine(AmbientFadeInRoutine());
        yield return OpenEyesRoutine(_firstPartOpenDuration, _firstPartOpenAmount, volumeTarget);

        // ...one heavy blink — behind it the deep-sleep wash thins one ladder
        // step: still soft, but the lamp's silhouette appears...
        float[] ladder = _focusLadder != null && _focusLadder.Length > 0 ? _focusLadder : new[] { 0f };
        if (_eyelids != null)
        {
            yield return _eyelids.Blink(_firstBlinkDuration, _firstPartOpenAmount);
            SetVolumeWeight(ladder[0]);
        }

        // ...then fully open while STILL LYING — the world holds the stepped
        // blur while the gaze settles on the lamp.
        yield return OpenEyesRoutine(_finalOpenDuration, 1f, ladder[0], alreadyOpen: _firstPartOpenAmount);
        if (_eyelids != null) _eyelids.OpenEyes();

        // ...and the blink cycle continues: every further blink drops the blur
        // one more ladder step (the step hides under the closed lids), with a
        // hold on each level so the player actually SEES the stage. The final
        // (sharp) level gets its own hold before the door turn.
        for (int i = 1; ladder != null && i < ladder.Length; i++)
        {
            if (_skipRequested) break;
            if (_eyelids != null) yield return _eyelids.Blink(_postOpenBlinkDuration, 1f);
            SetVolumeWeight(ladder[i]);
            yield return new WaitForSeconds(i == ladder.Length - 1 ? _lampSharpHoldTime : _lampFocusHoldTime);
        }
        SetVolumeWeight(0f); // skip-safety: the lamp is sharp when moving on

        // ── Phase 2.5: the sharp lamp look rests before the door turn ──────────
        // (the hold already ran at the end of the ladder)

        // ── Phase 2.6: lying gaze — lamp → door, then the wandering beats ──────
        // Still lying: the door is just the FIRST gaze beat — the same drowsy
        // turn (angle-scaled duration, defocus pulse, rest) as the beats that
        // follow it to the note and the desk. The door beat does NOT resolve
        // its defocus: the blur stays at full strength for the whole hold —
        // the door (and anything at it) is never seen sharp; the NEXT beat
        // (the note) carries and resolves the blur instead. The defocus also
        // ramps EARLY (done by 30% of the turn) — the door is already fully
        // blurred the moment it swings into the frame, masking the maid.
        if (_lyingSecondGazeTarget != null)
        {
            float doorHold = Random.Range(_lyingGazeHoldRange.x, _lyingGazeHoldRange.y);
            yield return LyingGazeBeatRoutine(_lyingSecondGazeTarget, doorHold,
                resolveDefocusDuringRest: false, defocusRampEnd: 0.3f);
            _cameraStartRotation = _fps.CameraTransform != null
                ? _fps.CameraTransform.rotation
                : _cameraStartRotation;
        }

        // ── Phase 2.7: lying head turns — note, desk ──────────────────────────
        // Still on the bed, the player glances through the extra lying targets
        // with the same drowsy saccade, randomized rest lengths (some things
        // catch the eye longer). The note is the first post-door look: the
        // moment the gaze lands on it, the maid scare prop disappears — under
        // the blur carried over from the door hold (vanish-while-looking).
        if (_lyingExtraGazeTargets != null && _lyingExtraGazeTargets.Length > 0)
        {
            Transform noteTarget = _lyingExtraGazeTargets[0];
            foreach (Transform target in _lyingExtraGazeTargets)
            {
                if (target == null) continue;
                float hold = Random.Range(_lyingGazeHoldRange.x, _lyingGazeHoldRange.y);
                bool isNote = target == noteTarget;
                yield return LyingGazeBeatRoutine(target, hold,
                    onArrived: isNote ? () => SetScareMaid(false) : null);
                if (_skipRequested) break;
            }
        }
        SetScareMaid(false); // skip-safety: the maid never survives the lying phase

        // ── Phase 3: get up — ONE continuous rise to the stand gaze (the desk),
        // then a slow drowsy turn to the FTUE book, then control ─────────────
        yield return RiseRoutine();
        yield return SlowGazeRoutine(_bedGazeTarget, _slowTurnDuration);

        Finish();
    }

    /// <summary>Fades the ambient loop in over the wake-up (unscaled — intro is never paused).</summary>
    private IEnumerator AmbientFadeInRoutine()
    {
        yield return AudioFades.Volume(_ambientSource, _ambientTargetVolume, _ambientFadeInDuration);
    }

    private void PoseLying()
    {
        // The capsule stands on the floor at the stand point INSIDE Room 7 from
        // the first frame — the room visibility triggers see the player, gravity
        // is safe during the whole intro. The FIRST-PERSON CAMERA creates the
        // "lying on the bed" picture in world space, not the capsule.
        Transform player = _characterController.transform;

        Vector3 forward = _lyingPoint.forward;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        _fps.Teleport(new Vector3(
            _standPoint.position.x, player.position.y, _standPoint.position.z), yaw);

        // The capsule just teleported INTO Room 7's visibility trigger. Unity
        // fires no OnTriggerEnter for a teleport into a trigger volume, so
        // without a manual rebuild the whole room stays culled — the camera
        // looks straight through the ceiling at the skybox.
        RoomVisibilityManager.Instance?.ReconcileAfterSpawn(player.position);

        // Camera world override: HandleLook and ApplyCameraTransform stop
        // touching the camera until the intro hands it back.
        _fps.TakeCameraWorldOverride();

        // Camera at the lying head position. When a lying gaze target is
        // assigned (e.g. the room door), the view aims at it — otherwise
        // straight up at the ceiling.
        // NEGATIVE pitch looks up in the FPS rig (_targetPitch -= mouseY) —
        // +89° would stare through the bed into the floor tiles.
        Transform camera = _fps.CameraTransform;
        if (camera != null)
        {
            _cameraWorldStart = _lyingPoint.position;
            camera.position = _cameraWorldStart;

            if (_lyingGazeTarget != null)
            {
                Vector3 toTarget = _lyingGazeTarget.position - _cameraWorldStart;
                _cameraStartRotation = toTarget.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                    : Quaternion.Euler(-89f, yaw, 0f);
            }
            else
            {
                _cameraStartRotation = Quaternion.Euler(-89f, yaw, 0f);
            }

            camera.rotation = _cameraStartRotation;

            // PIN the camera in world space for the whole lying phase. The
            // camera is a child of the player rig: the capsule settles under
            // gravity after the teleport (its Y drops ~0.5 m) and head bob
            // writes the local offset every frame — together they drag the
            // camera INTO the mattress ("we're under the bed again"). The
            // pin coroutine re-asserts the world pose every frame until the
            // scripted phases (which also write world poses) take over.
            _pinnedCameraPos = _cameraWorldStart;
            _pinRoutine = StartCoroutine(PinCameraRoutine());
        }
    }

    /// <summary>
    /// Re-asserts the camera's WORLD POSITION every frame during the lying
    /// phases. Runs from PoseLying until the sit-up (StopCameraPin). The
    /// camera is a child of the player rig: the capsule settles under gravity
    /// after the teleport (its Y drops ~0.5 m) and head bob writes the local
    /// offset every Update — together they drag the camera INTO the mattress
    /// ("we're under the bed again"). Only the POSITION is pinned: the
    /// rotation is written exclusively by the scripted phases (the rig keeps
    /// its hands off it while the cinematic override is active). `yield null`
    /// resumes after the rig's Update, so the pin wins before the render.
    /// </summary>
    private IEnumerator PinCameraRoutine()
    {
        while (true)
        {
            Transform camera = _fps.CameraTransform;
            if (camera != null)
                camera.position = _pinnedCameraPos;
            yield return null;
        }
    }

    /// <summary>Stops the lying-phase camera pin (called when the rise takes over).</summary>
    private void StopCameraPin()
    {
        if (_pinRoutine != null)
        {
            StopCoroutine(_pinRoutine);
            _pinRoutine = null;
        }
    }

    /// <summary>Sets the intro Volume weight in one call (no-op without the volume).</summary>
    private void SetVolumeWeight(float weight)
    {
        if (_introVolume != null) _introVolume.weight = weight;
    }

    /// <summary>
    /// Opens the eyes from 0 (or fromOpenAmount) to targetAmount while fading
    /// the intro volume weight from weightFrom to weightTo in parallel.
    /// </summary>
    private IEnumerator OpenEyesRoutine(float duration, float targetAmount, float weightTo,
        float alreadyOpen = 0f)
    {
        float weightFrom = _introVolume != null ? _introVolume.weight : 0f;
        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / duration);
            if (_eyelids != null)
                _eyelids.SetEyeOpen(Mathf.Lerp(alreadyOpen, targetAmount, p));
            if (_introVolume != null)
                _introVolume.weight = Mathf.Lerp(weightFrom, weightTo, p);
            yield return null;
        }
        if (_eyelids != null) _eyelids.SetEyeOpen(targetAmount);
        if (_introVolume != null) _introVolume.weight = weightTo;
    }

    /// <summary>
    /// Drowsy head-turn easing: quintic smootherstep evaluated on a warped
    /// progress (p^0.75). The velocity rises gently, peaks early (~a third
    /// of the way) at a modest multiple of the average, then decays along a
    /// LONG monotonic tail to zero — one impulse, one smooth settle. Zero
    /// velocity and zero acceleration at both ends.
    /// </summary>
    private static float DrowsyEase(float p)
    {
        float w = Mathf.Pow(Mathf.Clamp01(p), 0.75f);
        return w * w * w * (w * (w * 6f - 15f) + 10f);
    }

    /// <summary>
    /// One HUMAN gaze beat while lying: one drowsy saccade toward the target
    /// (single impulse, long decay — see DrowsyEase), then a natural head sway
    /// while the gaze rests. The rest length is randomized by the caller — a
    /// person looks at some things faster than others. With
    /// <paramref name="resolveDefocusDuringRest"/> = false (the door beat)
    /// the defocus stays at full strength for the whole hold — the door is
    /// never seen sharp; the next beat resolves the blur instead.
    /// <paramref name="onArrived"/> fires the instant the gaze LANDS on the
    /// target (turn over, sway about to start) — e.g. the desk beat hides the
    /// maid exactly then, under the full-blur landing.
    /// </summary>
    private IEnumerator LyingGazeBeatRoutine(Transform target, float restTime,
        bool resolveDefocusDuringRest = true, float defocusRampEnd = 1f,
        System.Action onArrived = null)
    {
        if (target == null || _fps.CameraTransform == null) yield break;

        Transform camera = _fps.CameraTransform;

        Vector3 toTarget = target.position - camera.position;
        if (toTarget.sqrMagnitude < 0.0001f) yield break;
        Quaternion toRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        // Saccade duration scales with the angle: the player is still groggy,
        // every lying head turn is SLOW and unhurried.
        float angle = Quaternion.Angle(camera.rotation, toRot);
        float turnDuration = Mathf.Clamp(angle / 14f, 2.4f, 4.2f);

        // ONE continuous motion with a LONG TAIL: the velocity rises gently,
        // peaks around a third of the turn, then decays MONOTONICALLY to
        // zero at the target. One single impulse, one single settle — no
        // punch at the start, no overshoot, no re-turn, no hard stop.
        Quaternion fromRot = camera.rotation;

        // Groggy defocus: the residual sleep blur CARRIES through the door
        // rest and pulses UP while the head moves, resolving LATE in the hold
        // — the eyes catch up with the head long after it stops.
        float defocusFrom = _introVolume != null ? _introVolume.weight : 0f;

        float t = 0f;
        while (t < turnDuration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / turnDuration);
            camera.rotation = Quaternion.Slerp(fromRot, toRot, DrowsyEase(p));

            // The defocus finishes ramping at defocusRampEnd of the turn:
            // a SHORTER ramp front-loads the blur — the target is fully
            // blurred long before it reaches the center of the frame.
            if (_introVolume != null)
                _introVolume.weight = Mathf.Lerp(defocusFrom, _wanderDefocusWeight,
                    Mathf.Clamp01(p / Mathf.Max(defocusRampEnd, 0.01f)));
            yield return null;
        }
        camera.rotation = toRot;

        // The gaze has LANDED on the target — fire the arrival hook (if any)
        // BEFORE the rest begins.
        onArrived?.Invoke();

        // Natural sway while resting — and (unless the caller suppressed it)
        // the defocus RESOLVES GRADUALLY across the hold (not a fast pull at
        // the end): a fast blur-to-sharp transition reads as a camera jerk.
        // resolveDefocusDuringRest = false (the DOOR beat): the blur STAYS at
        // full strength for the whole hold — the door is never seen sharp;
        // the defocusFrom of the NEXT beat carries on from this weight.
        float swayT = 0f;
        while (swayT < restTime)
        {
            if (_skipRequested) yield break;
            swayT += Time.deltaTime;
            float fadeIn = Mathf.Clamp01(swayT / 0.8f);
            float fadeOut = Mathf.Clamp01((restTime - swayT) / Mathf.Max(restTime * 0.3f, 0.01f));
            float amp = _headSwayDegrees * 0.5f * fadeIn * fadeOut;
            float s2 = swayT;
            float swayYaw = (Mathf.Sin(s2 * 1.7f) + 0.5f * Mathf.Sin(s2 * 2.9f + 1.3f)) * amp;
            float swayPitch = (Mathf.Sin(s2 * 1.3f + 0.7f) + 0.5f * Mathf.Sin(s2 * 2.3f)) * amp;
            camera.rotation = toRot * Quaternion.Euler(swayPitch, swayYaw, 0f);

            if (resolveDefocusDuringRest && _introVolume != null)
                _introVolume.weight = Mathf.Lerp(_wanderDefocusWeight, 0f,
                    Mathf.Clamp01((swayT - restTime * 0.15f) / Mathf.Max(restTime * 0.85f, 0.01f)));
            yield return null;
        }
        if (resolveDefocusDuringRest) SetVolumeWeight(0f);
        camera.rotation = toRot;
    }

    /// <summary>
    /// Full get-up imitation, not a teleport: ONE continuous motion from the
    /// lying pose to the standing pose at the stand point — the camera plays
    /// the body: Bezier arc up over the legs, a forward lean, a mid-rise dip
    /// of the gaze toward the feet spot (watching where the feet plant), then
    /// the head lifts to the stand gaze target. NO rest there — the stand
    /// target exists only to steer the body yaw; control/turns move straight
    /// on. The capsule stays at the stand point the whole time (gravity-safe) —
    /// the CAMERA carries the illusion of the body moving.
    /// </summary>
    private IEnumerator RiseRoutine()
    {
        Transform player = _characterController.transform;
        Transform camera = _fps.CameraTransform;

        // The standing pose: eye height at the stand point, facing the FINAL
        // gaze target (the FTUE book) — the rise aims STRAIGHT at it, no
        // intermediate stops. The end pose is built on the CAPSULE's actual
        // position, not the marker's transform: the capsule has stood at the
        // stand point (XZ) since the intro start and is gravity-settled — its
        // root Y IS the real floor height. The marker hangs on the bed prop
        // and can sit at any height; ending the rise at marker Y + eye offset
        // made ReturnCameraWorldToRig snap the camera vertically (the rig
        // puts the camera at capsule root + eye offset) — the post-battery jolt.
        Vector3 standPointPos = _standPoint != null
            ? new Vector3(_standPoint.position.x, player.position.y, _standPoint.position.z)
            : player.position;
        Vector3 standCamPos = standPointPos + Vector3.up * StandingEyeHeight;
        Vector3 gazeFlatFwd = _cameraStartRotation * Vector3.forward;
        gazeFlatFwd.y = 0f;
        Quaternion gazeYaw = gazeFlatFwd.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(gazeFlatFwd.normalized, Vector3.up)
            : Quaternion.Euler(0f, player.eulerAngles.y, 0f);
        Quaternion standCamRot = _standGazeTarget != null
            ? FlatLookRotation(_standGazeTarget.position, standPointPos)
            : gazeYaw;

        // ── 3a: stand up — ONE continuous get-up motion ────────────────────────
        // From the lying pose straight to the stand point: the head leads UP
        // over the legs (Bezier arc), leans forward into the travel, DIPS
        // toward the feet spot mid-rise (watching where the feet plant), then
        // lifts to the stand gaze target (the desk). The lying-phase camera
        // pin stops HERE — from the first frame the scripted phase owns the
        // camera.
        StopCameraPin();
        Vector3 standStartPos = camera.position;
        Quaternion standStartRot = camera.rotation;
        float riseStartWeight = _introVolume != null ? _introVolume.weight : 0f;

        Quaternion standEndRot = standCamRot;
        if (_standGazeTarget != null)
        {
            // FULL look (level yaw + clamped pitch): the desk is BELOW eye
            // level — the rise must arrive already pitched down at it.
            Vector3 toDesk = _standGazeTarget.position - standCamPos;
            float flatDist = new Vector2(toDesk.x, toDesk.z).magnitude;
            float deskPitch = flatDist > 0.0001f
                ? Mathf.Clamp(-Mathf.Atan2(toDesk.y, flatDist) * Mathf.Rad2Deg, -_maxGazePitch, _maxGazePitch)
                : 0f;
            standEndRot = FlatLookRotation(_standGazeTarget.position, standCamPos)
                * Quaternion.Euler(deskPitch, 0f, 0f);
        }

        // Arc control point: the head leads up, over the legs — the vertical
        // lift dominates, the horizontal travel stays minimal (getting up from
        // a lying pose moves the head mostly UP, not across the bed).
        Vector3 standControl = Vector3.Lerp(standStartPos, standCamPos, 0.5f)
            + Vector3.up * _riseArcHeight;

        // The feet spot: the floor point the body stands up over (the stand
        // point). The mid-rise dip lowers the PITCH toward the feet while the
        // YAW keeps aiming at the stand gaze target (the desk) — the head
        // tilts down in place, it never veers off toward the travel direction.
        // Forward lean — the head leads into the direction of travel.
        Vector3 travel = standCamPos - standStartPos;
        travel.y = 0f;
        Vector3 leanDir = travel.sqrMagnitude > 0.0001f ? travel.normalized : Vector3.zero;
        Vector3 deskFlatFwd = standEndRot * Vector3.forward;
        deskFlatFwd.y = 0f;
        float deskYaw = deskFlatFwd.sqrMagnitude > 0.0001f
            ? FlatYaw(deskFlatFwd)
            : camera.eulerAngles.y;

        float t = 0f;
        while (t < _standUpDuration)
        {
            if (_skipRequested) break;
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _standUpDuration));
            float dip = Mathf.Sin(p * Mathf.PI) * _standLookDownWeight; // 0 → peak → 0

            camera.position = Bezier(standStartPos, standControl, standCamPos, p)
                + leanDir * (Mathf.Sin(p * Mathf.PI) * _standLeanDistance);

            Quaternion baseRot = Quaternion.Slerp(standStartRot, standEndRot, p);
            if (dip > 0.001f)
            {
                // Pitch down toward the feet (clamped) at the CURRENT eye
                // height — the yaw STAYS on the desk.
                Vector3 toFoot = standPointPos - camera.position;
                float flatDist = new Vector2(toFoot.x, toFoot.z).magnitude;
                float downPitch = flatDist > 0.0001f
                    ? Mathf.Clamp(-Mathf.Atan2(toFoot.y, flatDist) * Mathf.Rad2Deg, -_maxGazePitch, _maxGazePitch)
                    : 0f;
                baseRot = Quaternion.Slerp(baseRot, Quaternion.Euler(downPitch, deskYaw, 0f), dip);
            }
            camera.rotation = baseRot;

            if (_introVolume != null)
                _introVolume.weight = Mathf.Lerp(riseStartWeight, 0f, p);
            yield return null;
        }

        // Hand the camera back to the FPSController rig immediately — NO rest
        // on the stand gaze target: the rise already aims at the final point.
        // CRITICAL ORDER: the scripted END pose is captured BEFORE the
        // teleport — the teleport rotates the body, which DRAGS the child
        // camera; reading camera.rotation after it would give the dragged
        // (wrong) rotation. The captured pitch is then written into the
        // camera's LOCAL rotation AFTER ReturnCameraWorldToRig resets it —
        // the view stays EXACTLY on the book, no snap.
        if (_standPoint != null)
        {
            Vector3 camFwd = camera.forward;
            camFwd.y = 0f;
            float bodyYaw = camFwd.sqrMagnitude > 0.0001f
                ? FlatYaw(camFwd)
                : player.eulerAngles.y;

            Quaternion endRotWorld = camera.rotation; // BEFORE the teleport!

            _fps.Teleport(new Vector3(
                standPointPos.x, player.position.y, standPointPos.z), bodyYaw);
            _fps.ReturnCameraWorldToRig();

            // Pitch AFTER ReturnCameraWorldToRig (it resets localRotation to
            // identity): express the scripted end pose in the new body frame
            // and read the pitch straight from it — convention-free, no sign
            // assumptions. local yaw is 0 by construction (the body yaw IS
            // the view's flat forward).
            Quaternion localRot = Quaternion.Inverse(Quaternion.Euler(0f, bodyYaw, 0f)) * endRotWorld;
            Vector3 euler = localRot.eulerAngles;
            float endPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _fps.CameraTransform.localRotation = Quaternion.Euler(endPitch, 0f, 0f);
        }
        else
        {
            _fps.ReturnCameraWorldToRig();
        }
        SyncPitchAfterRise();
    }

    /// <summary>
    /// After the rise hands the camera back to the rig, the internal pitch
    /// state is re-synced with the current camera rotation so returning input
    /// control later does not snap the view.
    /// </summary>
    private void SyncPitchAfterRise()
    {
        // ReturnCameraWorldToRig leaves the camera at the neutral local pose;
        // SetCinematicControl(false → true) re-syncs smoothed pitch state to it.
        _fps.SetCinematicControl(false);
        _fps.SetCinematicControl(true);
    }

    /// <summary>Level (yaw-only) rotation looking toward a world point from a position.</summary>
    private static Quaternion FlatLookRotation(Vector3 target, Vector3 from)
    {
        Vector3 flat = target - from;
        flat.y = 0f;
        return flat.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flat.normalized, Vector3.up)
            : Quaternion.identity;
    }

    /// <summary>Yaw angle in degrees of a flat direction vector.</summary>
    private static float FlatYaw(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
            : 0f;
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    /// <summary>
    /// The slow drowsy turn after standing: eases the body yaw + camera pitch
    /// toward the target (the FTUE book) over a fixed duration, then holds
    /// briefly before control is handed over.
    /// </summary>
    private IEnumerator SlowGazeRoutine(Transform target, float duration)
    {
        if (target == null) yield break;

        Transform player = _characterController.transform;
        Transform camera = _fps.CameraTransform;
        if (player == null || camera == null) yield break;

        Quaternion startBody = player.rotation;
        Quaternion startCamLocal = camera.localRotation;
        float startPitch = startCamLocal.eulerAngles.x > 180f
            ? startCamLocal.eulerAngles.x - 360f
            : startCamLocal.eulerAngles.x;

        // Target: body yaw = the flat direction to the point; camera pitch =
        // the clamped pitch to the point (the same head range as the sweep).
        Vector3 flatToLook = target.position - player.position;
        flatToLook.y = 0f;
        Quaternion endBody = flatToLook.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatToLook.normalized, Vector3.up)
            : startBody;

        Vector3 toLook = target.position - camera.position;
        float flatDist = new Vector2(toLook.x, toLook.z).magnitude;
        float targetPitch = flatDist > 0.0001f
            ? Mathf.Clamp(-Mathf.Atan2(toLook.y, flatDist) * Mathf.Rad2Deg, -_maxGazePitch, _maxGazePitch)
            : 0f;

        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = DrowsyEase(Mathf.Clamp01(t / duration));

            player.rotation = Quaternion.Slerp(startBody, endBody, p);
            camera.localRotation = Quaternion.Euler(Mathf.Lerp(startPitch, targetPitch, p), 0f, 0f);
            yield return null;
        }

        player.rotation = endBody;
        camera.localRotation = Quaternion.Euler(targetPitch, 0f, 0f);

        // A short hold on the book before control is handed over.
        yield return new WaitForSeconds(_gazeHoldTime);
    }

    private void OnMenuPressed()
    {
        // ESC during the intro skips it instead of opening the pause menu.
        if (_isPlaying && !_skipRequested)
        {
            _skipRequested = true;
        }
    }

    /// <summary>Restores input, HUD and post-processing; the game takes over.</summary>
    private void Finish() => Finish(restoreCameraState: true);

    /// <summary>
    /// Full finish, or the teardown-safe variant (<paramref name="restoreCameraState"/> =
    /// false from OnDestroy): during scene unload the rig/camera may already be destroyed —
    /// touching them throws MissingReferenceException, and restoring them is pointless anyway.
    /// </summary>
    private void Finish(bool restoreCameraState)
    {
        _isPlaying = false;
        StopCameraPin();
        SetScareMaid(false); // ESC-skip safety: the maid never survives the intro

        if (_introVolume != null)
        {
            _introVolume.weight = 0f;
            _introVolume.enabled = false; // game post-processing returns as is
        }
        if (_eyelids != null) _eyelids.OpenEyes();

        if (_ambientSource != null)
            _ambientSource.volume = _ambientTargetVolume;

        // Hand control over WITHOUT moving the view: the player keeps looking
        // exactly where the intro left the camera.
        //
        // Normal finish (after the rise): the camera rig already owns the view
        // (ReturnCameraWorldToRig happened at the end of the rise, the gaze
        // routines kept writing camera.localRotation). SetCinematicControl(true)
        // re-syncs the controller's internal pitch with the CURRENT camera
        // rotation — without this the first mouse move snaps back to the
        // rise-end pose.
        //
        // ESC skip (world override still on): capture the camera's world pose,
        // snap it back into the rig, then rebuild the body yaw and the camera
        // local pitch so the rendered view is identical.
        Transform camera = _fps.CameraTransform;
        if (camera != null && _fps.CameraWorldOverrideActive)
        {
            Quaternion camWorldRot = camera.rotation;

            _fps.ReturnCameraWorldToRig();

            // Body yaw follows the camera's flat forward…
            Vector3 flatFwd = camWorldRot * Vector3.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude > 0.0001f)
            {
                Vector3 bodyEuler = _characterController.transform.eulerAngles;
                bodyEuler.y = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
                _characterController.transform.eulerAngles = bodyEuler;
            }

            // …camera keeps the captured pitch, expressed in the new body frame.
            Quaternion localTarget = Quaternion.Inverse(_characterController.transform.rotation) * camWorldRot;
            Vector3 euler = localTarget.eulerAngles;
            camera.localRotation = Quaternion.Euler(euler.x, 0f, 0f);
        }

        // Sync the internal look state with the current camera rotation, then
        // return control — the view continues exactly from this pose.
        // Guarded: during scene teardown (OnDestroy → Finish) the camera
        // transform may already be destroyed.
        if (restoreCameraState && _fps != null && _fps.CameraTransform != null)
        {
            _fps.SetCinematicControl(true);
            _fps.SetCinematicControl(false);
        }
        InputManager.Instance?.SetUIInputEnabled(true);
        InputManager.Instance?.SetPlayerInputEnabled(true);
        if (InputManager.Instance != null) InputManager.Instance.OnMenuPerformed -= OnMenuPressed;

        InteractionUI.Instance?.SetVisible(true);
    }

    private void OnDestroy()
    {
        // Safety: if the scene unloads mid-intro (debug stop, scene reload),
        // restore the input and post-processing state unconditionally.
        // restoreCameraState: false — the transforms may already be destroyed
        // (teardown order), touching them throws MissingReferenceException.
        if (_isPlaying) Finish(restoreCameraState: false);

        if (_runtimeProfile != null)
            Destroy(_runtimeProfile);
    }
}
