using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Opening cinematic: the player wakes up lying on the bed in Room 7.
/// Runs only on a NEW game (requested through <see cref="RequestStart"/> from
/// the main menu) — a loaded save skips it entirely.
///
/// Phases:
///   1. Setup — the capsule stands on the floor at the stand point (gravity
///      and room visibility triggers stay valid); the CAMERA is taken over in
///      world space: lying on the bed;
///   2. Wake  — eyelids open with blinks while the intro Volume (blur /
///      desaturation / darker exposure) mostly clears — the world stays soft
///      on the lamp and resolves into focus WHILE the gaze rests on it;
///   3. Lying gaze — the view turns from the lamp to the door; the remaining
///      blur fades out during the turn;
///   4. Rise  — sit up (still facing the door), a seated pause, then stand up
///      WHILE turning the view to the stand gaze target (the locker);
///   5. Focus — a slow drowsy turn to the bed (FTUE hints); control is
///      returned with the view EXACTLY where the cinematic ended (no snap).
///
/// ESC at any point skips the remaining sequence (fast-forward to phase 5).
/// All temporary post-processing lives in <see cref="_introVolume"/> — when it
/// is disabled the game post-processing (global + room volumes) returns as is.
/// </summary>
public class WakeUpIntroController : MonoBehaviour
{
    private const string PlayerTag = "Player";

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

    [Tooltip("Seated head position — the middle stage of getting up: lying → sit → stand.")]
    [SerializeField] private Transform _sitPoint;

    [Tooltip("Point above the FTUE hints on the bed — the final gaze target.")]
    [SerializeField] private Transform _bedGazeTarget;

    [Tooltip("What the player sees when the eyes part open while still lying — " +
             "e.g. the ceiling lamp. If empty, the camera looks straight up at the ceiling.")]
    [SerializeField] private Transform _lyingGazeTarget;

    [Tooltip("The second lying gaze: while still lying, the view turns from the " +
             "first target to this one (e.g. the room door) before the player rises.")]
    [SerializeField] private Transform _lyingSecondGazeTarget;

    [Tooltip("Seconds the lying view takes to turn to the second gaze target.")]
    [SerializeField, Min(0.1f)] private float _lyingGazeTurnDuration = 1.6f;

    [Tooltip("What the player faces once standing (e.g. the locker): the view turns " +
             "from the door to this target WHILE the body stands up. Empty = keep the gaze yaw.")]
    [SerializeField] private Transform _standGazeTarget;

    [Header("Wake-Up Timing")]
    [SerializeField, Min(0f)] private float _startDelay = 0.4f;
    [SerializeField, Min(0.1f)] private float _firstPartOpenDuration = 2.2f;
    [SerializeField, Min(0.1f)] private float _firstBlinkDuration = 0.9f;
    [SerializeField, Min(0.1f)] private float _finalOpenDuration = 3.5f;
    [SerializeField, Range(0f, 1f)] private float _firstPartOpenAmount = 0.45f;
    [SerializeField, Min(0.1f)] private float _postOpenBlinkDelay = 0.4f;
    [SerializeField, Min(0.1f)] private float _postOpenBlinkDuration = 0.7f;

    [Header("Rise From The Bed")]
    [Tooltip("Height of the rise arc control point above the midpoint (m) — lifts the camera over the bed.")]
    [SerializeField, Min(0f)] private float _riseArcHeight = 0.7f;

    [Tooltip("How far below the capsule top the eye height sits (m) — matches the FPS camera offset.")]
    [SerializeField, Min(0f)] private float _cameraBelowEyeOffset = 0.4f;

    [Header("Get Up Choreography")]
    [Tooltip("Seconds to sit up from the lying pose (camera rises from the bed to the sitting pose).")]
    [SerializeField, Min(0.1f)] private float _sitUpDuration = 1.8f;

    [Tooltip("Seconds of the pause sitting on the bed before standing.")]
    [SerializeField, Min(0f)] private float _sitPauseDuration = 0.7f;

    [Tooltip("Seconds to stand up from the sitting pose (camera lowers onto the legs and rises to full height).")]
    [SerializeField, Min(0.1f)] private float _standUpDuration = 2.0f;

    [Tooltip("Seconds of a small body sway when the stand-up finishes (settling the balance).")]
    [SerializeField, Min(0f)] private float _standSettleDuration = 0.6f;

    [Header("Gaze")]
    [SerializeField, Min(0.1f)] private float _gazeHoldTime = 1.1f;

    [Tooltip("Max |pitch| the gaze sweep may aim at (degrees, positive down). " +
             "Prevents the camera from diving into the bed/floor or craning up through the ceiling.")]
    [SerializeField, Range(5f, 60f)] private float _maxGazePitch = 45f;

    [Header("Lamp Focus (lying)")]
    [Tooltip("Volume weight while the gaze settles on the lamp (0 = fully focused). " +
             "The blur level the world still has when the player first looks at the lamp.")]
    [SerializeField, Range(0f, 1f)] private float _lampFocusWeight = 0.25f;

    [Tooltip("Seconds of the gradual focus-in while looking at the lamp.")]
    [SerializeField, Min(0.3f)] private float _lampFocusDuration = 2f;

    [Header("Final Slow Turn")]
    [Tooltip("Seconds of the slow drowsy turn to the bed note at the end of the intro.")]
    [SerializeField, Min(0.3f)] private float _slowTurnDuration = 2.2f;

    [Header("Audio")]
    [Tooltip("Source for the wake-up sound (gasp, breath). Plays once when the eyes part open.")]
    [SerializeField] private AudioSource _wakeSound;

    [Tooltip("Source for the room ambient loop, faded in over the intro.")]
    [SerializeField] private AudioSource _ambientSource;
    [SerializeField, Min(0.1f)] private float _ambientFadeInDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogging;

    // One-shot flag: consumed from SaveManager (survives domain reload, unlike
    // a static field on this class) — see SaveManager.RequestWakeUpIntro.

    private FPSController _fps;
    private CharacterController _characterController;
    private EyelidOverlay _eyelids;
    private VolumeProfile _runtimeProfile;
    private DepthOfField _dof;
    private ColorAdjustments _color;
    private bool _isPlaying;
    private bool _skipRequested;
    private float _ambientTargetVolume;
    private Vector3 _cameraWorldStart;
    private Quaternion _cameraStartRotation;
    private Vector3 _pinnedCameraPos;
    private Coroutine _pinRoutine;
    private float _baseLensFov = 60f;

    /// <summary>Standing eye height above the capsule root — matches the FPS rig camera offset.</summary>
    private float StandingEyeHeight => _fps.StandingHeight - _cameraBelowEyeOffset;

    /// <summary>
    /// Called by the main menu right before the scene transition. The intro
    /// controller in the newly loaded scene picks this up in Start().
    /// </summary>
    public static void RequestStart() => SaveManager.RequestWakeUpIntro();

    private void Start()
    {
        // Route intro sources to the SFX bus unless they already have a mixer group from the Inspector.
        if (_wakeSound != null && _wakeSound.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_wakeSound);
        if (_ambientSource != null && _ambientSource.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_ambientSource);

        if (!SaveManager.ConsumeWakeUpIntroRequest())
        {
            // Unconditional so the flag-loss is visible even without debug logging.
            Debug.Log("[WakeUpIntro] Skipped — no new-game request (save load or scene reload).", this);
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

        BuildRuntimeProfile();
        StartCoroutine(IntroRoutine());
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

        _dof = _runtimeProfile.Add<DepthOfField>(true);
        _dof.mode.overrideState = true;
        _dof.mode.value = DepthOfFieldMode.Gaussian;
        _dof.gaussianStart.overrideState = true;
        _dof.gaussianStart.value = _dofGaussianStart;
        _dof.gaussianEnd.overrideState = true;
        _dof.gaussianEnd.value = _dofGaussianEnd;
        _dof.gaussianMaxRadius.overrideState = true;
        _dof.gaussianMaxRadius.value = _dofMaxRadius;

        _color = _runtimeProfile.Add<ColorAdjustments>(true);
        _color.postExposure.overrideState = true;
        _color.postExposure.value = _sleepExposure;
        _color.saturation.overrideState = true;
        _color.saturation.value = _sleepSaturation;

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

        // ── Phase 2: wake — lids part, blur fades ─────────────────────────────
        // First: a sleepy half-open.
        if (_wakeSound != null) _wakeSound.Play();
        if (_ambientSource != null) StartCoroutine(AmbientFadeInRoutine());
        yield return OpenEyesRoutine(_firstPartOpenDuration, _firstPartOpenAmount, volumeTarget);

        // ...one heavy blink...
        if (_eyelids != null) yield return _eyelids.Blink(_firstBlinkDuration, _firstPartOpenAmount);

        // ...then fully open while STILL LYING — the sleep blur keeps most of
        // its strength (weight 1 → _lampFocusWeight): the world stays soft and
        // out of focus while the gaze settles on the lamp.
        yield return OpenEyesRoutine(_finalOpenDuration, 1f, _lampFocusWeight, alreadyOpen: _firstPartOpenAmount);
        if (_eyelids != null) _eyelids.OpenEyes();

        yield return new WaitForSeconds(_postOpenBlinkDelay);
        if (_eyelids != null && !_skipRequested) yield return _eyelids.Blink(_postOpenBlinkDuration, 1f);

        // ── Phase 2.5: the eyes focus on the lamp ─────────────────────────────
        // While looking at the lamp, the blur gradually resolves — the world
        // slowly comes into focus (a partial clear, the rest stays soft).
        yield return FadeVolumeRoutine(_lampFocusWeight, _lampFocusDuration);

        // ── Phase 2.6: lying gaze — lamp → door, then hold ────────────────────
        // Still lying: the view turns from the lamp to the door and rests on
        // it. WHILE TURNING to the door the remaining blur fades out smoothly
        // — by the time the gaze rests on the door the world is fully sharp.
        yield return LyingGazeTurnRoutine();
        if (!_skipRequested) yield return FadeVolumeRoutine(0f, _gazeHoldTime);

        // ── Phase 3: get up — sit, turn to the locker, rise to standing ───────
        yield return RiseRoutine();

        // ── Phase 4: a slow drowsy turn to the bed (the note), control ────────
        // The door and the locker were already looked at (lying and sitting) —
        // repeating them standing felt like the view bouncing back and forth.
        yield return SlowGazeRoutine(_bedGazeTarget, _slowTurnDuration);

        Finish();
    }

    /// <summary>
    /// Writes the FOV into the Cinemachine lens (Lens is a struct — copy, set, assign back).
    /// Kept for Finish()'s safety restore of the base lens after a skip.
    /// </summary>
    private static void SetLensFov(Unity.Cinemachine.CinemachineCamera cineCam, float fov)
    {
        var lens = cineCam.Lens;
        lens.FieldOfView = fov;
        cineCam.Lens = lens;
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

    /// <summary>
    /// Opens the eyes from 0 (or fromOpenAmount) to targetAmount while fading
    /// the intro volume weight from weightFrom to weightTo in parallel.
    /// </summary>
    private IEnumerator OpenEyesRoutine(float duration, float targetAmount, float weightTo,
        bool skipWhenRequested = true, float alreadyOpen = 0f)
    {
        float weightFrom = _introVolume != null ? _introVolume.weight : 0f;
        float t = 0f;
        while (t < duration)
        {
            if (skipWhenRequested && _skipRequested) yield break;
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
    /// Eases the intro volume weight to the target over the duration (smoothed).
    /// The intro runs on unscaled-feeling fixed phases; the weight fade uses the
    /// same deltaTime as the phases around it.
    /// </summary>
    private IEnumerator FadeVolumeRoutine(float targetWeight, float duration)
    {
        if (_introVolume == null) yield break;
        float from = _introVolume.weight;
        if (Mathf.Approximately(from, targetWeight)) yield break;

        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            _introVolume.weight = Mathf.Lerp(from, targetWeight, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        _introVolume.weight = targetWeight;
    }

    /// <summary>
    /// While still lying: smoothly turns the camera from the first gaze target
    /// (lamp) to the second one (door). The end rotation is stored as the new
    /// start rotation for the rise arc. Skipped when no second target is set.
    /// </summary>
    private IEnumerator LyingGazeTurnRoutine()
    {
        if (_lyingSecondGazeTarget == null || _fps.CameraTransform == null) yield break;

        Transform camera = _fps.CameraTransform;
        Quaternion fromRot = camera.rotation;
        Vector3 toTarget = _lyingSecondGazeTarget.position - camera.position;
        Quaternion toRot = toTarget.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
            : fromRot;

        float t = 0f;
        while (t < _lyingGazeTurnDuration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _lyingGazeTurnDuration));
            camera.rotation = Quaternion.Slerp(fromRot, toRot, p);
            yield return null;
        }
        camera.rotation = toRot;

        // The rise now starts from the door-facing pose.
        _cameraStartRotation = toRot;
    }
    /// <summary>
    /// Full get-up imitation, not a teleport: the camera plays the body motion —
    ///   3a. sit up from lying to a seated pose on the bed edge;
    ///   3b. a short pause sitting;
    ///   3c. stand up: rise from the seated height to full height at the stand
    ///       point while the sleep blur clears;
    ///   3d. a small balance settle sway.
    /// The capsule stays at the stand point the whole time (gravity-safe) —
    /// the CAMERA carries the illusion of the body moving.
    /// </summary>
    private IEnumerator RiseRoutine()
    {
        Transform player = _characterController.transform;
        Transform camera = _fps.CameraTransform;

        // The standing pose: eye height at the stand point, facing the stand
        // gaze target (the locker) if assigned, else the lying gaze direction.
        // The stand point, not the original lying spot, is where the player
        // ends up — sitting and rising both move the body toward the locker.
        Vector3 standPointPos = _standPoint != null
            ? _standPoint.position
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

        // ── 3a: sit up from the lying pose to the seated pose ─────────────────
        // The seated eye height: the SitPoint drives BOTH the position and the
        // height (1.35 — sitting upright, well above the mattress), so the
        // sit-up visibly lifts the head instead of sliding it sideways.
        // The lying-phase camera pin stops HERE — from the first sit-up frame
        // the scripted phases own the camera exclusively.
        StopCameraPin();
        Vector3 sitCamPos = _sitPoint != null
            ? _sitPoint.position
            : Vector3.Lerp(_cameraWorldStart, standCamPos, 0.6f);
        Quaternion sitRot = gazeYaw; // sitting keeps the current (door) direction

        Vector3 sitStartPos = _cameraWorldStart;
        Quaternion sitStartRot = _cameraStartRotation;

        // Arc control point: the head leads up, over the legs — the vertical
        // lift dominates, the horizontal travel stays minimal (sitting up from
        // a lying pose moves the head mostly UP, not across the bed).
        Vector3 sitControl = Vector3.Lerp(sitStartPos, sitCamPos, 0.5f)
            + Vector3.up * _riseArcHeight;

        float t = 0f;
        while (t < _sitUpDuration)
        {
            if (_skipRequested) break;
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _sitUpDuration));

            camera.position = Bezier(sitStartPos, sitControl, sitCamPos, p);
            camera.rotation = Quaternion.Slerp(sitStartRot, sitRot, p);
            yield return null;
        }
        // ── 3b: a beat sitting — the turn to the locker happens DURING the rise ─
        // Sitting keeps the door direction; the rise below turns the view from
        // the door to the locker while the body stands up, so getting up reads
        // as one continuous motion instead of turn-then-rise.
        if (!_skipRequested)
            yield return new WaitForSeconds(_sitPauseDuration);

        // ── 3c: stand up — rise to full height, the sleep blur clears ─────────
        // Standing eye height: standingHeight(2) − 0.4 = 1.6 m — the SAME pose
        // the rig uses. The rise keeps the camera LEVEL and turns the view from
        // the door to the locker WHILE the body stands up — one continuous
        // get-up motion (turn-then-rise felt like two separate steps).
        Vector3 standStartPos = camera.position;
        Quaternion standStartRot = camera.rotation;
        float riseStartWeight = _introVolume != null ? _introVolume.weight : 0f;

        Quaternion standEndRot = _standGazeTarget != null
            ? FlatLookRotation(_standGazeTarget.position, standCamPos)
            : standCamRot;

        t = 0f;
        while (t < _standUpDuration)
        {
            if (_skipRequested) break;
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _standUpDuration));

            camera.position = Vector3.Lerp(standStartPos, standCamPos, p);
            camera.rotation = Quaternion.Slerp(standStartRot, standEndRot, p);

            if (_introVolume != null)
                _introVolume.weight = Mathf.Lerp(riseStartWeight, 0f, p);
            yield return null;
        }

        // ── 3d: balance settle — a small decaying sway ─────────────────────────
        t = 0f;
        Vector3 settledPos = camera.position;
        Quaternion settledRot = camera.rotation;
        Vector3 swayAxis = camera.right;
        while (t < _standSettleDuration)
        {
            if (_skipRequested) break;
            t += Time.deltaTime;
            float phase = t / Mathf.Max(_standSettleDuration, 0.01f);
            float sway = Mathf.Sin(phase * Mathf.PI * 2f) * (1f - phase) * 0.02f;
            camera.position = settledPos + swayAxis * sway;
            yield return null;
        }
        camera.position = settledPos;
        camera.rotation = settledRot;

        // Hand the camera back to the FPSController rig exactly where it is.
        // The body is teleported to the stand point first — yaw faces the
        // locker (the stand gaze target) so the rig snaps the camera onto
        // the body standing where the rise finished, facing the locker.
        if (_standPoint != null)
        {
            float bodyYaw = _standGazeTarget != null
                ? FlatYaw(_standGazeTarget.position - standPointPos)
                : player.eulerAngles.y;
            _fps.Teleport(new Vector3(
                _standPoint.position.x, player.position.y, _standPoint.position.z), bodyYaw);
        }
        _fps.ReturnCameraWorldToRig();
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
    /// The final drowsy turn: eases the body yaw + camera pitch toward the
    /// target over a fixed duration (fixed-time Slerp, not the aim routine —
    /// the slow drift must not be re-accelerated by the aim easing), then
    /// holds briefly. Used for the last look at the bed note.
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
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));

            player.rotation = Quaternion.Slerp(startBody, endBody, p);
            camera.localRotation = Quaternion.Euler(Mathf.Lerp(startPitch, targetPitch, p), 0f, 0f);
            yield return null;
        }

        player.rotation = endBody;
        camera.localRotation = Quaternion.Euler(targetPitch, 0f, 0f);

        // A short hold on the note before control is handed over.
        yield return new WaitForSeconds(_gazeHoldTime);
    }

    private void OnMenuPressed()
    {
        // ESC during the intro skips it instead of opening the pause menu.
        if (_isPlaying && !_skipRequested)
        {
            _skipRequested = true;
            if (_debugLogging) Debug.Log("[WakeUpIntro] Skip requested (ESC).", this);
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

        // Safety: a skip mid-zoom would leave the lens narrowed — restore the
        // base FOV so the returned control starts from the normal lens.
        if (restoreCameraState && _fps != null && _fps.CameraTransform != null)
        {
            var cineCam = _fps.CameraTransform.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (cineCam != null)
                SetLensFov(cineCam, _baseLensFov);
        }

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

        if (_debugLogging) Debug.Log("[WakeUpIntro] Finished — control returned to the player.", this);
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
