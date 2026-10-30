using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// End-of-game cinematic for the final door puzzle.
/// Fired by FinalDoorPuzzleController.OnSolved:
///   A. First person: the door opens with the portal igniting, the gaze takes
///      in Death, then Angel, then the portal (the ending look fades in);
///   B. The player turns around - the patients stand behind. The crowd
///      camera: two angles, a slow fly-by past the figures, then the long
///      shot from behind the crowd's backs - where the doctor is revealed;
///   C. ALL ON THE MUSIC'S CLOCK: the doctor grieves from his reveal, turns
///      on the CHORD (the track's loudest peak, _finalChordTime), walks
///      through the second swell where the crowd bursts into their mist
///      particles on the dark screen, and the final dip to black closes on
///      the chord;
///   D. Credits.
/// ESC (menu action) skips straight to the final fade and the credits.
/// </summary>
public class EndGameSequence : MonoBehaviour
{
    [Header("Controller")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Door Animation")]
    [SerializeField] private Animator _doorAnimator;
    [SerializeField] private string _doorOpenStateName = "doorOpen";
    [SerializeField, Min(0.5f)] private float _doorOpenFallbackDuration = 4.5f;

    [Header("Start Position")]
    [SerializeField] private Transform _playerStartPoint;

    [Header("First-Person Gaze")]
    [SerializeField] private Transform _lookPoint;
    [SerializeField] private Transform _deathPoint;
    [SerializeField] private Transform _angelPoint;
    [SerializeField] private Transform _portalLookPoint;
    [SerializeField, Min(0f)] private float _gazeStartDelay = 1f;
    [SerializeField, Min(0.1f)] private float _gazeHoldTime = 2.5f;
    [SerializeField, Min(1f)] private float _gazeTurnSpeed = 30f;
    [SerializeField, Min(0.2f)] private float _gazeMinTurnDuration = 1.2f;
    [SerializeField, Min(0.5f)] private float _gazeMaxTurnDuration = 4f;

    [Header("Turn-Around")]
    [SerializeField, Min(0.3f)] private float _turnAroundDuration = 1.6f;

    [Header("Crowd Cinematic Camera")]
    [SerializeField] private CinemachineCamera _crowdCamera;
    [SerializeField, Min(1)] private int _cameraPriority = 2000;
    [SerializeField] private Transform _croudShot1;
    [SerializeField] private Transform _croudShot2;
    [SerializeField] private Transform _flyByStart;
    [SerializeField] private Transform _flyByEnd;
    [SerializeField] private Transform _crowdBackView;
    [SerializeField, Min(0.5f)] private float _shotHoldDuration = 3f;
    [SerializeField, Range(0.2f, 2f)] private float _shotFadeDuration = 0.7f;
    [SerializeField, Range(0f, 10f)] private float _panAmplitudeDegrees = 2.5f;
    [SerializeField, Range(0.05f, 2f)] private float _panSpeed = 0.25f;
    [SerializeField, Min(1f)] private float _flyByDuration = 6f;
    [SerializeField, Min(0.5f)] private float _backViewHoldDuration = 5f;
    [SerializeField, Range(0f, 1f)] private float _pullBackSpeed = 0.08f;

    [Header("Shadow Finale (the doctor)")]
    [SerializeField] private Transform _shadow;
    [SerializeField] private Animator _shadowAnimator;
    [SerializeField] private Transform _shadowFaceTarget;
    [SerializeField, Min(0f)] private float _shadowStartDelay = 1.2f;
    [SerializeField] private string _shadowIdleStateName = "Idle";
    [SerializeField] private string _shadowTurnStateName = "Turn";
    [SerializeField] private string _shadowWalkStateName = "Walk";
    [SerializeField, Min(0.1f)] private float _shadowWalkSpeed = 1.45f;
    [SerializeField] private Transform _portalWalkTarget;
    [SerializeField, Min(0.05f)] private float _shadowArrivalDistance = 0.5f;
    [SerializeField, Min(2f)] private float _shadowMaxWalkDuration = 30f;
    [SerializeField, Min(0f)] private float _walkFaceTurnDuration = 0.6f;

    [Header("Crowd Dissolve (the world's farewell)")]
    [SerializeField, Min(0.5f)] private float _crowdDissolveDuration = 4f;
    [SerializeField, Range(1f, 8f)] private float _particleBoost = 3.5f;
    [SerializeField, Range(0f, 1f)] private float _dimLevel = 0.55f;

    [Header("Music")]
    [SerializeField] private MusicTrack _endingTrack;

    [Tooltip("Time (seconds from the track's START) where the track's FINAL CHORD lands - " +
             "measured from the track's loudness envelope (the loudest peak). The whole " +
             "finale is paced backwards from this moment.")]
    [SerializeField, Min(1f)] private float _finalChordTime = 53f;

    [Tooltip("How many seconds BEFORE the final chord the doctor starts his turn. " +
             "The walk then fills the rest of the time (its pace adapts automatically).")]
    [SerializeField, Min(2f)] private float _turnLeadTime = 12f;

    [Header("Portal")]
    [SerializeField] private PortalFunnelController _portal;

    [Header("Crowd & Doctor (hidden until the ending)")]
    [SerializeField] private GameObject _crowd;
    [SerializeField, Min(0.1f)] private float _crowdFadeInDuration = 1.2f;
    [SerializeField] private GameObject _doctor;
    [SerializeField, Min(0.1f)] private float _doctorFadeInDuration = 2f;

    [Header("Fade & Credits")]
    [SerializeField, Min(0f)] private float _startDelay = 2f;
    [Tooltip("Duration of the final dip to black before the credits.")]
    [SerializeField, Min(0.1f)] private float _fadeDuration = 1.2f;
    [SerializeField] private EndGameCreditsView _creditsView;

    [Header("Post Processing (temporary, ending only)")]
    [SerializeField] private Volume _endingVolume;
    [SerializeField, Range(0f, 1f)] private float _gazeVolumeWeight = 0.55f;
    [SerializeField, Min(0.1f)] private float _dofGaussianStart = 3f;
    [SerializeField, Min(0.5f)] private float _dofGaussianEnd = 10f;
    [SerializeField, Range(0.1f, 1f)] private float _dofMaxRadius = 0.6f;
    [SerializeField, Range(-3f, 0f)] private float _endingExposure = -0.4f;
    [SerializeField, Range(-100f, 0f)] private float _endingSaturation = -25f;

    [Header("Sway")]
    [SerializeField, Range(0f, 3f)] private float _headSwayDegrees = 0.5f;

    [Header("Skip")]
    [SerializeField] private bool _allowSkip = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLogging;

    private CharacterController _characterController;
    private VolumeProfile _runtimeProfile;
    private bool _isPlaying;
    private bool _skipRequested;
    private CinemachineBrain _brain;
    private CinemachineBlendDefinition _savedBlend;
    private bool _brainCutForced;
    private Coroutine _pullBack;
    private float _musicStartTime;

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponent<FinalDoorPuzzleController>();

        if (_doorAnimator == null)
            _doorAnimator = GetComponent<Animator>();

        if (_endingVolume == null)
            _endingVolume = GetComponent<Volume>();

        if (_creditsView == null)
            _creditsView = FindFirstObjectByType<EndGameCreditsView>(FindObjectsInactive.Include);

        // Fallback: the credits canvas may be deleted/recreated - find by name.
        if (_creditsView == null)
        {
            var canvasRoot = GameObject.Find("Canvas");
            if (canvasRoot != null)
            {
                var child = canvasRoot.transform.Find("EndGameCredits");
                if (child != null) _creditsView = child.GetComponent<EndGameCreditsView>();
            }
        }

        if (_portal == null)
            _portal = FindFirstObjectByType<PortalFunnelController>(FindObjectsInactive.Include);

        _characterController = FindFirstObjectByType<CharacterController>();

        AutoFindSceneReferences();
        BuildRuntimeProfile();

        // The crowd and the doctor belong to the ending only - hide them the
        // moment this room wakes up, whatever the scene/prefab saved state is.
        DeactivateCrowdAndDoctor();

#if UNITY_EDITOR
        // A stale editor Solo (Cinemachine's static "show this camera no matter
        // what") survives into Play mode and hijacks every brain - clear it at
        // game start so a forgotten preview never breaks a playtest.
        Unity.Cinemachine.CinemachineCore.SoloCamera = null;
#endif
    }

    /// <summary>Finds the ending helpers inside the puzzle prefab by their authored paths.</summary>
    private void AutoFindSceneReferences()
    {
        if (_portalLookPoint == null)
            _portalLookPoint = transform.Find("EndGameCinematic/PortalLookPoint");

        if (_portalWalkTarget == null)
            _portalWalkTarget = transform.Find("EndGameCinematic/PortalWalkTarget");

        if (_croudShot1 == null)
            _croudShot1 = transform.Find("EndGameCinematic/CroudShot1");

        if (_croudShot2 == null)
            _croudShot2 = transform.Find("EndGameCinematic/CroudShot2");

        if (_flyByStart == null)
            _flyByStart = transform.Find("EndGameCinematic/FlyByStart");

        if (_flyByEnd == null)
            _flyByEnd = transform.Find("EndGameCinematic/FlyByEnd");

        if (_crowdBackView == null)
            _crowdBackView = transform.Find("EndGameCinematic/CrowdBackView");

        if (_crowdCamera == null)
        {
            Transform cam = transform.Find("Cameras/EndGameCamera");
            if (cam != null) _crowdCamera = cam.GetComponent<CinemachineCamera>();
        }

        if (_shadow == null)
            _shadow = transform.Find("Player/morrow");

        if (_shadowAnimator == null && _shadow != null)
            _shadowAnimator = _shadow.GetComponentInChildren<Animator>(true);

        if (_crowd == null)
        {
            Transform crowd = transform.Find("Croud");
            if (crowd != null) _crowd = crowd.gameObject;
        }

        if (_doctor == null)
        {
            Transform doctor = transform.Find("Player");
            if (doctor != null) _doctor = doctor.gameObject;
        }
    }

    /// <summary>
    /// Builds the ending-look profile in code: DoF blur + desaturation + darker
    /// exposure. The Volume weight blends between the base game look (0) and
    /// this (1). Never edits a shared profile.
    /// </summary>
    private void BuildRuntimeProfile()
    {
        if (_endingVolume == null)
            return;

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
        color.postExposure.value = _endingExposure;
        color.saturation.overrideState = true;
        color.saturation.value = _endingSaturation;

        _endingVolume.isGlobal = true;
        _endingVolume.priority = 300;
        _endingVolume.sharedProfile = _runtimeProfile;
    }

    private void OnEnable()
    {
        if (_controller != null)
            _controller.OnSolved += HandleSolved;

        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnMenuPerformed -= OnSkipPressed;
            InputManager.Instance.OnMenuPerformed += OnSkipPressed;
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
            _controller.OnSolved -= HandleSolved;

        if (InputManager.Instance != null)
            InputManager.Instance.OnMenuPerformed -= OnSkipPressed;
    }

    private void HandleSolved()
    {
        if (_isPlaying) return;
        _isPlaying = true;
        StartCoroutine(SequenceRoutine());
    }

    private void OnSkipPressed()
    {
        if (_allowSkip && _isPlaying)
            _skipRequested = true;
    }

    // ── Main sequence ─────────────────────────────────────────────────────────

    private IEnumerator SequenceRoutine()
    {
        // A0. Take everything over while the screen is still black from the
        // pour cinematic's fade. One frame is yielded BEFORE the player snap:
        // SetSolved saves the game right after this coroutine starts, and the
        // save must capture the player's pre-ending position.
        yield return null;

        InputManager.Instance?.SetPlayerInputEnabled(false);
        InputManager.Instance?.SetUIInputEnabled(false);
        InteractionUI.Instance?.SetVisible(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        var fps = _characterController != null
            ? _characterController.GetComponent<FPSController>()
            : null;
        fps?.SetCinematicControl(true);

        SnapPlayerToStart();
        OrientCameraToLookPoint();

        // The crowd and the doctor stay hidden for now - they are revealed
        // later on their own cues.
        DeactivateCrowdAndDoctor();

        // A1. Let the puzzle-exit fade finish so the player sees the scene.
        yield return new WaitForSeconds(_startDelay);

        // A2. The door opens; the portal ignites; the ending music starts.
        if (_portal != null)
            _portal.Activate();

        if (_endingTrack != null)
        {
            MusicDirector.RequestTrack(_endingTrack);
            _musicStartTime = Time.time;
        }

        Coroutine gazeVolume = StartCoroutine(
            FadeVolumeRoutine(0f, _gazeVolumeWeight, _doorOpenFallbackDuration));

        bool doorOpened = false;
        if (_doorAnimator != null && !string.IsNullOrEmpty(_doorOpenStateName))
        {
            _doorAnimator.Play(_doorOpenStateName, 0, 0f);
            yield return WaitForStateFinish(_doorAnimator, _doorOpenStateName);
            doorOpened = true;
        }
        if (!doorOpened)
            yield return new WaitForSeconds(_doorOpenFallbackDuration);

        StopCoroutine(gazeVolume);
        if (_endingVolume != null) _endingVolume.weight = _gazeVolumeWeight;

        // A3. Gaze choreography: Death, then Angel, then the portal.
        yield return new WaitForSeconds(_gazeStartDelay);
        yield return GazeAtRoutine(_deathPoint);
        yield return GazeAtRoutine(_angelPoint);
        yield return GazeAtRoutine(_portalLookPoint != null ? _portalLookPoint : _lookPoint);

        // B1. The player turns around; the crowd fades in during the swing.
        if (!_skipRequested)
        {
            StartCoroutine(FadeCrowdInRoutine());
            yield return TurnAroundRoutine();
            yield return FadeVolumeRoutine(
                _endingVolume != null ? _endingVolume.weight : 0f, 1f, _turnAroundDuration);
        }

        // B2. Crowd cinematic: the shots, the fly-by, the back view.
        if (!_skipRequested)
            yield return CrowdCinematicRoutine();

        // C. The doctor's farewell + the crowd's last splash, on the music's clock.
        if (!_skipRequested)
            yield return ShadowFinaleRoutine();

        // D. Final fade + credits.
        yield return FinalFadeAndCreditsRoutine();
    }

    // ── Phase routines ────────────────────────────────────────────────────────

    /// <summary>Eased 180 turn toward the crowd, then the gaze settles.</summary>
    private IEnumerator TurnAroundRoutine()
    {
        if (_characterController == null) yield break;

        Transform player = _characterController.transform;
        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;
        if (camera == null) yield break;

        Quaternion targetBody;
        if (_crowdBackView != null)
        {
            Vector3 flat = _crowdBackView.position - player.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                targetBody = Quaternion.LookRotation(flat.normalized, Vector3.up);
            else
                targetBody = Quaternion.Euler(0f, player.eulerAngles.y + 180f, 0f);
        }
        else
        {
            targetBody = Quaternion.Euler(0f, player.eulerAngles.y + 180f, 0f);
        }

        Quaternion startBody = player.rotation;
        Quaternion startCam = camera.localRotation;
        Quaternion endCam = Quaternion.Euler(0f, 0f, 0f);

        float t = 0f;
        while (t < _turnAroundDuration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = DrowsyEase(Mathf.Clamp01(t / _turnAroundDuration));
            player.rotation = Quaternion.Slerp(startBody, targetBody, p);
            camera.localRotation = Quaternion.Slerp(startCam, endCam, p);
            yield return null;
        }
        player.rotation = targetBody;
        camera.localRotation = endCam;
    }

    /// <summary>
    /// The crowd shots: dip to black, the camera cuts under it, fade up - the
    /// doctor is revealed behind the black of the fly-by's cut; the final shot
    /// from behind the crowd's backs is where his finale happens.
    /// </summary>
    private IEnumerator CrowdCinematicRoutine()
    {
        if (_crowdCamera == null)
        {
            if (_debugLogging) Debug.LogWarning("[EndGameSequence] Crowd camera not found - skipping crowd shots.");
            yield break;
        }

        // Dip the player's own view to black FIRST - the switch to the crowd
        // camera happens under it, as a hard cut (no blend, no fly-in).
        yield return FadeToBlackRoutine();
        if (_skipRequested) yield break;

        ForceBrainCutMode();
        _crowdCamera.gameObject.SetActive(true);
        _crowdCamera.Priority = _cameraPriority;

        if (_croudShot1 != null)
            CutToShot(_croudShot1);
        yield return FadeToClearRoutine();

        if (_croudShot1 != null)
            yield return PanShotRoutine(_croudShot1, _shotHoldDuration);

        if (!_skipRequested && _croudShot2 != null)
        {
            yield return FadeCutRoutine(_croudShot2);
            yield return PanShotRoutine(_croudShot2, _shotHoldDuration);
        }

        if (!_skipRequested)
        {
            // The doctor is revealed behind the black of the transition into
            // the fly-by (he grieves from his first frame on); the camera
            // drifts while the screen is still clearing.
            yield return FadeToBlackRoutine();
            ActivateDoctorAndShadow();
            if (_flyByStart != null)
                CutToShot(_flyByStart);
            var fly = _flyByStart != null && _flyByEnd != null
                ? StartCoroutine(FlyByRoutine())
                : null;
            yield return FadeToClearRoutine();
            if (fly != null) yield return fly;
        }

        if (!_skipRequested && _crowdBackView != null)
        {
            yield return FadeCutRoutine(_crowdBackView);
            // The pull-away starts immediately (in parallel with the hold pan).
            if (_pullBackSpeed > 0f)
                _pullBack = StartCoroutine(PullBackRoutine());
            yield return PanShotRoutine(_crowdBackView, _backViewHoldDuration);
        }
    }

    /// <summary>Very slow constant-speed dolly backwards along the camera's own axis.</summary>
    private IEnumerator PullBackRoutine()
    {
        while (true)
        {
            if (_skipRequested) yield break;
            _crowdCamera.transform.position -=
                _crowdCamera.transform.forward * (_pullBackSpeed * Time.deltaTime);
            yield return null;
        }
    }

    /// <summary>
    /// Forces the brain's default blend to a hard Cut while the ending owns
    /// the cameras - every switch is covered by the black dip anyway. The old
    /// blend is restored when the crowd camera is released.
    /// </summary>
    private void ForceBrainCutMode()
    {
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();
        if (_brain == null || _brainCutForced) return;

        _savedBlend = _brain.DefaultBlend;
        _brain.DefaultBlend = new CinemachineBlendDefinition
        {
            Style = CinemachineBlendDefinition.Styles.Cut,
            Time = 0f
        };
        _brainCutForced = true;
    }

    /// <summary>Restores the brain's stock blend after the ending.</summary>
    private void RestoreBrainBlend()
    {
        if (_brain != null && _brainCutForced)
            _brain.DefaultBlend = _savedBlend;
        _brainCutForced = false;
    }

    /// <summary>Dip to black, cut to the shot pose, fade back up.</summary>
    private IEnumerator FadeCutRoutine(Transform target)
    {
        if (ScreenFader.Instance == null)
        {
            CutToShot(target);
            yield break;
        }

        yield return ScreenFader.Instance.FadeIn(_shotFadeDuration);
        if (_skipRequested) yield break;
        CutToShot(target);
        yield return ScreenFader.Instance.FadeOut(_shotFadeDuration);
    }

    /// <summary>Fades the screen to black (half of a dip transition).</summary>
    private IEnumerator FadeToBlackRoutine()
    {
        if (ScreenFader.Instance == null) yield break;
        yield return ScreenFader.Instance.FadeIn(_shotFadeDuration);
    }

    /// <summary>Fades the screen back to clear (half of a dip transition).</summary>
    private IEnumerator FadeToClearRoutine()
    {
        if (ScreenFader.Instance == null) yield break;
        yield return ScreenFader.Instance.FadeOut(_shotFadeDuration);
    }

    /// <summary>
    /// Holds a static shot while the camera drifts in a slow, subtle pan
    /// around the shot pose (cinema's "breathing" tripod): yaw swings gently
    /// between plus/minus the amplitude, the motion eases in and out.
    /// </summary>
    private IEnumerator PanShotRoutine(Transform shot, float duration)
    {
        Quaternion baseRot = shot.rotation;
        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, p));
            float yaw = Mathf.Sin(t * _panSpeed) * _panAmplitudeDegrees * envelope;
            _crowdCamera.transform.SetPositionAndRotation(shot.position, baseRot * Quaternion.Euler(0f, yaw, 0f));
            yield return null;
        }
    }

    /// <summary>Cuts the crowd camera to the given shot pose (instant, no blend).</summary>
    private void CutToShot(Transform shot)
    {
        _crowdCamera.transform.SetPositionAndRotation(shot.position, shot.rotation);
    }

    /// <summary>
    /// The fly-by: the crowd camera drifts from the start pose to the end pose
    /// along the line of figures. Ease-OUT curve - the motion starts
    /// immediately (no ramp-up) and glides to a smooth stop.
    /// </summary>
    private IEnumerator FlyByRoutine()
    {
        if (_flyByStart == null || _flyByEnd == null) yield break;

        Vector3 fromPos = _flyByStart.position;
        Vector3 toPos = _flyByEnd.position;
        Quaternion fromRot = _flyByStart.rotation;
        Quaternion toRot = _flyByEnd.rotation;

        float t = 0f;
        while (t < _flyByDuration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / _flyByDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f);
            _crowdCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, eased),
                Quaternion.Slerp(fromRot, toRot, eased));
            yield return null;
        }
        _crowdCamera.transform.SetPositionAndRotation(toPos, toRot);
    }

    /// <summary>
    /// The doctor's farewell and the crowd's last splash, ALL on the track's
    /// loudness map: the doctor grieves from his reveal, turns on the CHORD
    /// (the loudest peak, _finalChordTime), walks through the second swell
    /// where the crowd bursts on the dark screen, and the final dip to black
    /// closes after the swell.
    /// </summary>
    private IEnumerator ShadowFinaleRoutine()
    {
        if (_shadow == null || _shadowAnimator == null)
        {
            if (_debugLogging) Debug.LogWarning("[EndGameSequence] Shadow not found - skipping its finale.");
            yield break;
        }

        // The crowd's farewell (the dim, the burst, the final black) runs on
        // the MUSIC's own clock - beats land on the final chord.
        StartCoroutine(MusicSyncFinaleRoutine());

        // The doctor's beats, all on the chord's clock:
        //   he STANDS (idle) watching the patients until _turnLeadTime
        //   seconds before the chord - then turns and walks, the arrival
        //   lands right on the chord.
        float chordRemainingNow = GetChordTimeRemainingSafe();
        float waitToTurn = Mathf.Max(chordRemainingNow - _turnLeadTime, 0.3f);
        yield return HoldWithSkip(waitToTurn);
        if (_skipRequested) yield break;

        // THE CHORD APPROACH: fire the turn - the CLIP itself rotates the
        // skeleton through the half-turn. The ROOT's job is to keep the
        // RENDERED orientation seamless: while the Turn->Walk crossfade
        // un-blends the clip's hip rotation, the root tweens by the same
        // offset, so the figure's world yaw never jumps and the walk starts
        // already facing the walk direction.
        _shadowAnimator.SetTrigger("Turn");
        yield return WaitForStateFinish(_shadowAnimator, _shadowTurnStateName);
        if (_skipRequested) yield break;

        // The crossfade Turn->Walk un-winds the hips' clip rotation; move the
        // same amount into the root IN SYNC so the visual direction is fixed.
        _shadowAnimator.SetTrigger(_shadowWalkStateName);
        yield return FlipRootDuringWalkRoutine(0.4f);
        if (_skipRequested) yield break;

        yield return ShadowWalkRoutine();
    }

    /// <summary>
    /// Tweens the root's yaw by the hips' measured offset over the Walk
    /// crossfade's duration, in sync with the Turn->Walk transition: the
    /// rendered hips' world yaw stays constant every frame.
    /// </summary>
    private IEnumerator FlipRootDuringWalkRoutine(float duration)
    {
        Transform hips = _shadow.Find("mixamorig:Hips");
        if (hips == null || duration <= 0f)
        {
            FlipShadowRootOntoHips();
            yield break;
        }

        float rigYaw = _shadow.eulerAngles.y;
        // CRITICAL: the offset must be in [0..360) form, NOT the shortest
        // DeltaAngle! The crossfade unwinds the hips from +186 to 0 (a
        // -186 sweep), so the root must rise by +186 in the SAME direction -
        // DeltaAngle would return -174 and the figure would visibly spin
        // back around before walking.
        // The crossfade blends the hips' rotation by the quaternion's SHORTEST
        // path: from -174 to 0 (RISING +174), NOT from +186 down. So the root
        // must FALL by DeltaAngle (-174) in sync - the sum stays constant.
        // Repeat (+186) would add to the rise and spin the figure a full 360.
        float rel = Mathf.DeltaAngle(rigYaw, hips.eulerAngles.y);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float w = Mathf.Clamp01(t / duration);
            _shadow.rotation = Quaternion.Euler(0f, rigYaw + rel * w, 0f);
            yield return null;
        }
        _shadow.rotation = Quaternion.Euler(0f, rigYaw + rel, 0f);
    }

    /// <summary>
    /// The ending's last beats, driven by the MUSIC's own clock (the track's
    /// remaining time to the final chord):
    ///   remaining <= 3.5s - the screen dims (not to full black);
    ///   remaining <= 2.5s - the crowd bursts into sparks and dissolves;
    ///   remaining <= 1s   - the final dip to full black;
    ///   0                 - the final chord lands on the black screen.
    /// Without music the beats run on a nominal 10 s countdown instead of
    /// collapsing into one frame.
    /// </summary>
    private IEnumerator MusicSyncFinaleRoutine()
    {
        bool dimStarted = false;
        bool burstStarted = false;
        bool blackStarted = false;
        float guard = 0f;

        // The beats run on the music's clock (seconds to the final chord).
        // When the music is unavailable (-1) or the sequence already overran
        // the chord, a NOMINAL countdown keeps the beats in ORDER instead of
        // firing dim/burst/black in the same frame - the doctor's departure
        // stays visible either way.
        float chordRemaining = GetChordTimeRemaining();
        bool musicClock = chordRemaining > 0f; // 0 means the chord already passed
        if (chordRemaining < MinFinaleWindowSeconds)
            chordRemaining = MinFinaleWindowSeconds;
        if (_debugLogging)
            Debug.Log($"[EndGameSequence] Finale beats: musicClock={musicClock}, " +
                      $"chordWindow={chordRemaining:F1}s");

        float lastKnownRemaining = -1f; // the music clock's last live reading
        float noMusicElapsed = 0f;      // seconds since the music clock died
        while (guard < 180f)
        {
            if (_skipRequested) yield break;
            guard += Time.deltaTime;

            float remaining = musicClock ? GetChordTimeRemaining() : -1f;
            if (remaining < 0f)
            {
                // The music is over (or never played) - count down the
                // nominal window from where the music clock left off.
                if (lastKnownRemaining < 0f) lastKnownRemaining = chordRemaining;
                noMusicElapsed += Time.deltaTime;
                remaining = Mathf.Max(0f, lastKnownRemaining - noMusicElapsed);
            }
            else
            {
                lastKnownRemaining = remaining;
            }

            if (!dimStarted && remaining <= 3.5f)
            {
                dimStarted = true;
                StartCoroutine(FadeToDimRoutine(_fadeDuration));
            }
            if (!burstStarted && remaining <= 2.5f)
            {
                burstStarted = true;
                StartCoroutine(CrowdDissolveRoutine());
            }
            if (!blackStarted && remaining <= 1f)
            {
                blackStarted = true;
                StartCoroutine(FadeOutRoutine());
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>The shortest stretch the farewell beats spread over when the
    /// music's clock is missing or exhausted - keeps the doctor's departure
    /// on screen instead of collapsing the whole finale into one frame.</summary>
    private const float MinFinaleWindowSeconds = 10f;

    /// <summary>Fades the screen to the partial dim level (not full black).</summary>
    private IEnumerator FadeToDimRoutine(float duration)
    {
        if (ScreenFader.Instance == null) yield break;
        yield return ScreenFader.Instance.FadeIn(duration, _dimLevel);
    }

    /// <summary>
    /// Seconds left until the track's FINAL CHORD (the loudness peak in the
    /// track's envelope, _finalChordTime). Returns -1 when the music is
    /// unavailable or already past the chord.
    /// </summary>
    private float GetChordTimeRemaining()
    {        if (MusicDirector.Instance == null || MusicDirector.Instance.ActiveTrack == null) return -1f;
        var directorType = MusicDirector.Instance.GetType();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var source = directorType.GetField("_activeSource", flags)?.GetValue(MusicDirector.Instance) as AudioSource;
        if (source == null || source.clip == null || !source.isPlaying) return -1f;
        return Mathf.Max(0f, _finalChordTime - source.time);
    }

    /// <summary>Safe variant: falls back to the nominal lead time when the
    /// music is not playing (so the idle/grief phases never stall).</summary>
    private float GetChordTimeRemainingSafe()
    {
        float v = GetChordTimeRemaining();
        if (v < 0f) v = _shadowStartDelay + 3f; // nominal budget without music
        return v;
    }

    /// <summary>
    /// The doctor's walk to the portal: the WalkPace animator parameter is set
    /// so the clip's loop speed carries him the whole distance in exactly the
    /// time left before the final chord. The body is eased onto the walking
    /// direction. No dissolve - the screen covers the end.
    /// </summary>
    private IEnumerator ShadowWalkRoutine()
    {
        if (_portalWalkTarget == null)
        {
            if (_debugLogging) Debug.LogWarning("[EndGameSequence] Portal walk target not found - skipping shadow walk.");
            yield break;
        }

        Vector3 flat = _portalWalkTarget.position - _shadow.position;
        flat.y = 0f;
        float distance = flat.magnitude;

        // The time budget: everything left until the final chord. Without the
        // music this is the walk's own budget - the departure still plays out.
        float timeToChord = GetChordTimeRemaining();
        if (timeToChord < 0f) timeToChord = MinFinaleWindowSeconds;

        // The walk clip's loop must carry him the whole distance in that time.
        // WalkPace = clip playback multiplier (the authored stride speed is
        // the serialized _shadowWalkSpeed).
        float strideSpeed = _shadowWalkSpeed;
        float pace = Mathf.Clamp(distance / timeToChord / strideSpeed, 0.15f, 1.2f);
        _shadowAnimator.SetFloat("WalkPace", pace);

        _shadowAnimator.SetTrigger(_shadowWalkStateName);

        // Ease the body toward the walking direction - then start the steps.
        if (flat.sqrMagnitude > 0.0001f)
        {
            Quaternion from = _shadow.rotation;
            Quaternion to = Quaternion.Euler(0f, Quaternion.LookRotation(flat.normalized).eulerAngles.y, 0f);
            if (_walkFaceTurnDuration > 0.01f && Quaternion.Angle(from, to) > 1f)
            {
                float faceT = 0f;
                while (faceT < _walkFaceTurnDuration)
                {
                    if (_skipRequested) yield break;
                    faceT += Time.deltaTime;
                    _shadow.rotation = Quaternion.Slerp(from, to, DrowsyEase(Mathf.Clamp01(faceT / _walkFaceTurnDuration)));
                    yield return null;
                }
            }
            _shadow.rotation = to;
        }

        float elapsed = 0f;
        while (elapsed < _shadowMaxWalkDuration)
        {
            if (_skipRequested) yield break;
            elapsed += Time.deltaTime;

            // The music's clock rules: when the chord lands, the walk is over
            // no matter the distance - the dark screen and the credits are up.
            // GetChordTimeRemaining() < 0 means NO music - never a reason to
            // cut the walk; the budget (timeToChord) caps it instead.
            float remaining = GetChordTimeRemaining();
            if (remaining >= 0f && (remaining <= 0f || elapsed >= timeToChord))
                break;

            Vector3 toTarget = _portalWalkTarget.position - _shadow.position;
            toTarget.y = 0f;
            float distNow = toTarget.magnitude;

            if (distNow <= _shadowArrivalDistance)
                break;

            Vector3 step = toTarget.normalized * Mathf.Min(_shadowWalkSpeed * pace * Time.deltaTime, distNow);
            _shadow.position += step;
            yield return null;
        }
    }

    /// <summary>
    /// Rotates the shadow's ROOT onto the hips' current world yaw. Right at
    /// the turn clip's end the skeleton holds the ~186 degree visual offset;
    /// moving that offset into the root keeps the rendered direction identical
    /// while the rig's rest orientation faces the portal.
    /// </summary>
    private void FlipShadowRootOntoHips()
    {
        Transform hips = _shadow.Find("mixamorig:Hips");
        if (hips == null) return;

        float rigYaw = _shadow.eulerAngles.y;
        float hipsYaw = hips.eulerAngles.y;
        float rel = Mathf.DeltaAngle(rigYaw, hipsYaw);

        _shadow.rotation = Quaternion.Euler(0f, rigYaw + rel, 0f);
    }

    /// <summary>
    /// The crowd's farewell: the mist particles inside every ghost burst out
    /// (emission, size and speed swell hard) while the figures themselves
    /// erode with the shader's noise dissolve - the crowd unravels into a
    /// cloud of sparks, which then drift on alone.
    /// </summary>
    private IEnumerator CrowdDissolveRoutine()
    {
        if (_crowd == null) yield break;

        var materials = new System.Collections.Generic.List<Material>();
        foreach (var r in _crowd.GetComponentsInChildren<Renderer>(true))
        {
            // Skip particle renderers - their sparks must linger AFTER the
            // figures are gone, not fade with them.
            if (r is UnityEngine.ParticleSystemRenderer) continue;
            if (r == null) continue;
            var m = r.material;
            m.SetFloat("_DISSOLVE_ON", 1f);
            m.SetFloat("_DissolveOn", 1f);
            m.EnableKeyword("_DISSOLVE_ON");
            materials.Add(m);
        }

        // Cache the particles' base config. The systems may be dormant
        // (Play On Awake off / stopped by culling) - wake them up first,
        // then give each one an immediate burst so the flash is instant.
        var particles = _crowd.GetComponentsInChildren<ParticleSystem>(true);
        var baseRates = new System.Collections.Generic.List<float>();
        var baseMax = new System.Collections.Generic.List<int>();
        var baseSizes = new System.Collections.Generic.List<float>();
        foreach (var ps in particles)
        {
            var em = ps.emission;
            baseRates.Add(em.rateOverTime.constant);
            baseMax.Add(ps.main.maxParticles);
            baseSizes.Add(ps.main.startSize.constant);

            if (!ps.isPlaying) ps.Play(true);

            var burst = new UnityEngine.ParticleSystem.Burst(0f, Mathf.RoundToInt(baseMax[baseMax.Count - 1] * _particleBoost));
            em.SetBursts(new[] { burst });
            var main = ps.main;
            main.maxParticles = Mathf.RoundToInt(baseMax[baseMax.Count - 1] * _particleBoost);
        }

        float t = 0f;
        while (t < _crowdDissolveDuration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float d = Mathf.Clamp01(t / _crowdDissolveDuration);

            for (int i = 0; i < particles.Length; i++)
            {
                var mainMod = particles[i].main;
                mainMod.maxParticles = Mathf.RoundToInt(baseMax[i] * _particleBoost);
                mainMod.startSize = baseSizes[i] * Mathf.Lerp(1f, 2.5f, d);
                mainMod.startSpeed = particles[i].main.startSpeed.constant * Mathf.Lerp(1f, 2.2f, d);
                var em = particles[i].emission;
                float boost = Mathf.Lerp(1f, _particleBoost, Mathf.Clamp01(d / 0.3f));
                em.rateOverTime = baseRates[i] * boost;
            }

            foreach (var m in materials)
            {
                if (m == null) continue;
                if (m.HasProperty("_Dissolve")) m.SetFloat("_Dissolve", d * 1.6f);
                var c = m.GetColor("_BaseColor");
                c.a = 1f - d;
                m.SetColor("_BaseColor", c);
            }
            yield return null;
        }

        // Fully gone: zero everything one last time.
        foreach (var m in materials)
        {
            if (m == null) continue;
            if (m.HasProperty("_Dissolve")) m.SetFloat("_Dissolve", 1.6f);
            var c = m.GetColor("_BaseColor");
            c.a = 0f;
            m.SetColor("_BaseColor", c);
        }
    }

    /// <summary>Waits out the final fade (it may already run) and shows the credits.</summary>
    private IEnumerator FinalFadeAndCreditsRoutine()
    {
        // 1. Full black FIRST - never launch the credits while the scene is
        //    still visible and the cameras are blending.
        if (ScreenFader.Instance != null)
        {
            if (ScreenFader.Instance.CurrentAlpha < 0.95f)
                yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        }
        else
            yield return new WaitForSeconds(_fadeDuration);

        // 2. Under the black ScreenFader, fade in the credits' OWN black
        //    background. It must be fully opaque BEFORE the cameras are
        //    released - otherwise the 3D scene shows through mid-fade.
        Coroutine overlayFade = _creditsView != null ? _creditsView.FadeOverlayIn() : null;
        if (overlayFade != null)
            yield return overlayFade;
        if (_creditsView != null)
            _creditsView.Show();
        else
            Debug.LogWarning("[EndGameSequence] Credits view not found - credits skipped.");

        // 3. Only now release the crowd camera (Cinemachine blends back to the
        //    player camera - hidden under the opaque credits background).
        RestoreBrainBlend();
        if (_pullBack != null)
        {
            StopCoroutine(_pullBack);
            _pullBack = null;
        }
        ReleaseCrowdCamera();

        // 4. Clear the game's black fader - the credits' own black is up.
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(0.5f);
    }

    /// <summary>
    /// Keeps the crowd and the doctor hidden until their cues in the ending.
    /// </summary>
    private void DeactivateCrowdAndDoctor()
    {
        if (_crowd == null)
        {
            Transform crowd = transform.Find("Croud");
            if (crowd != null) _crowd = crowd.gameObject;
        }

        if (_doctor == null)
            _doctor = transform.Find("Player") != null ? transform.Find("Player").gameObject : null;

        if (_crowd != null) _crowd.SetActive(false);
        if (_doctor != null) _doctor.SetActive(false);
    }

    /// <summary>Fades the crowd in while the player swings around to face the patients.</summary>
    private IEnumerator FadeCrowdInRoutine()
    {
        if (_crowd == null) yield break;

        _crowd.SetActive(true);
        yield return FadeRenderersRoutine(
            _crowd.GetComponentsInChildren<Renderer>(true), _crowdFadeInDuration);
    }

    /// <summary>
    /// Activates the doctor (and his shadow child) - he fades in right after.
    /// </summary>
    private void ActivateDoctorAndShadow()
    {
        if (_doctor == null) return;

        _doctor.gameObject.SetActive(true);
        ActivateShadowOnDoctor();
    }

    /// <summary>
    /// Activates the shadow (the morrow on the doctor): a pale spirit, facing
    /// the crowd, grieving from his first frame.
    /// </summary>
    private void ActivateShadowOnDoctor()
    {
        if (_shadow == null) return;

        _shadow.gameObject.SetActive(true);

        // The doctor is a pale spirit, not a black blob: white reads against
        // the dark room AND against the portal light he walks into.
        foreach (var r in _shadow.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            var m = r.material;
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", new Color(0.92f, 0.93f, 0.97f, 1f));
        }

        if (_shadowFaceTarget != null)
        {
            Vector3 flat = _shadowFaceTarget.position - _shadow.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                _shadow.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(flat.normalized).eulerAngles.y, 0f);
        }

        // CRITICAL: the freshly activated animator does not start its state
        // machine by itself (the avatar is null - a Generic rig quirk). The
        // Idle state must be forced, otherwise the doctor freezes forever.
        if (_shadowAnimator != null && !string.IsNullOrEmpty(_shadowIdleStateName))
        {
            _shadowAnimator.Rebind();
            _shadowAnimator.Play(_shadowIdleStateName, 0, 0f);
        }
    }

    /// <summary>
    /// Fades all given renderers in from invisible to full over the duration
    /// (instantiates each material once, enables transparency, lerps alpha).
    /// </summary>
    private static IEnumerator FadeRenderersRoutine(Renderer[] renderers, float duration)
    {
        if (renderers == null || renderers.Length == 0) yield break;

        var materials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            materials[i] = renderers[i].material; // instantiates the material
            materials[i].SetFloat("_Surface", 1f); // Transparent in URP Lit
            var c = materials[i].GetColor("_BaseColor");
            c.a = 0f;
            materials[i].SetColor("_BaseColor", c);
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / duration);
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                var c = materials[i].GetColor("_BaseColor");
                c.a = a;
                materials[i].SetColor("_BaseColor", c);
            }
            yield return null;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null) continue;
            var c = materials[i].GetColor("_BaseColor");
            c.a = 1f;
            materials[i].SetColor("_BaseColor", c);
        }
    }

    /// <summary>
    /// One gaze stage: a single eased impulse of body yaw + camera pitch
    /// toward the target, then a natural breathing sway while the gaze rests.
    /// </summary>
    private IEnumerator GazeAtRoutine(Transform target)
    {
        if (target == null || _characterController == null) yield break;

        Transform player = _characterController.transform;
        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;
        if (camera == null) yield break;

        Vector3 toLook = target.position - camera.position;
        if (toLook.sqrMagnitude < 0.0001f) yield break;
        Quaternion worldLook = Quaternion.LookRotation(toLook.normalized, Vector3.up);

        Vector3 flatToLook = target.position - player.position;
        flatToLook.y = 0f;
        if (flatToLook.sqrMagnitude < 0.0001f) yield break;
        Quaternion targetBody = Quaternion.LookRotation(flatToLook.normalized, Vector3.up);

        Quaternion localTarget = Quaternion.Inverse(targetBody) * worldLook;
        Quaternion targetCam = Quaternion.Euler(localTarget.eulerAngles.x, 0f, 0f);

        Quaternion startBody = player.rotation;
        Quaternion startCam = camera.localRotation;

        float angle = Quaternion.Angle(camera.rotation, worldLook);
        float duration = Mathf.Clamp(angle / _gazeTurnSpeed, _gazeMinTurnDuration, _gazeMaxTurnDuration);

        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            float p = DrowsyEase(Mathf.Clamp01(t / duration));
            player.rotation = Quaternion.Slerp(startBody, targetBody, p);
            camera.localRotation = Quaternion.Slerp(startCam, targetCam, p);
            yield return null;
        }
        player.rotation = targetBody;
        camera.localRotation = targetCam;

        // Breathing sway while the gaze rests.
        float swayT = 0f;
        while (swayT < _gazeHoldTime)
        {
            if (_skipRequested) yield break;
            swayT += Time.deltaTime;
            float fadeIn = Mathf.Clamp01(swayT / 0.8f);
            float fadeOut = Mathf.Clamp01((_gazeHoldTime - swayT) / Mathf.Max(_gazeHoldTime * 0.3f, 0.01f));
            float amp = _headSwayDegrees * 0.5f * fadeIn * fadeOut;
            float s2 = swayT;
            float swayYaw = (Mathf.Sin(s2 * 1.7f) + 0.5f * Mathf.Sin(s2 * 2.9f + 1.3f)) * amp;
            float swayPitch = (Mathf.Sin(s2 * 1.3f + 0.7f) + 0.5f * Mathf.Sin(s2 * 2.3f)) * amp;
            camera.localRotation = targetCam * Quaternion.Euler(swayPitch, swayYaw, 0f);
            yield return null;
        }
        camera.localRotation = targetCam;
    }

    /// <summary>Drowsy head-turn easing: quintic smootherstep on a warped
    /// progress - one gentle velocity pulse, long monotonic decay.</summary>
    private static float DrowsyEase(float p)
    {
        float w = Mathf.Pow(Mathf.Clamp01(p), 0.75f);
        return w * w * w * (w * (w * 6f - 15f) + 10f);
    }

    /// <summary>Waits the given time in skippable slices.</summary>
    private IEnumerator HoldWithSkip(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>Fades to black over FadeDuration.</summary>
    private IEnumerator FadeOutRoutine()
    {
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);
    }

    /// <summary>Fades the ending Volume weight between two values.</summary>
    private IEnumerator FadeVolumeRoutine(float from, float to, float duration)
    {
        if (_endingVolume == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            if (_skipRequested) yield break;
            t += Time.deltaTime;
            _endingVolume.weight = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        _endingVolume.weight = to;
    }

    /// <summary>Returns the crowd camera to the game (priority down, off).</summary>
    private void ReleaseCrowdCamera()
    {
        if (_crowdCamera == null) return;
        _crowdCamera.Priority = 0;
        _crowdCamera.gameObject.SetActive(false);
    }

    /// <summary>
    /// Snaps the player to the start point horizontally, preserving their
    /// current height. Runs while the screen is still black.
    /// </summary>
    private void SnapPlayerToStart()
    {
        if (_playerStartPoint == null)
        {
            Transform found = transform.Find("playerFinal");
            if (found == null)
                found = transform.Find("player");
            if (found != null)
                _playerStartPoint = found;
        }

        if (_playerStartPoint == null)
            return;

        if (_characterController == null)
            _characterController = FindFirstObjectByType<FPSController>()
                ?.GetComponent<CharacterController>();

        if (_characterController == null)
            return;

        Transform player = _characterController.transform;
        Vector3 position = player.position;
        position.x = _playerStartPoint.position.x;
        position.z = _playerStartPoint.position.z;

        player.GetComponent<FPSController>()
            ?.Teleport(position, player.eulerAngles.y);
    }

    /// <summary>
    /// Instantly turns the body and camera onto the look point (behind black).
    /// </summary>
    private void OrientCameraToLookPoint()
    {
        if (_lookPoint == null || _characterController == null)
            return;

        Transform player = _characterController.transform;
        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;

        Vector3 flatToLook = _lookPoint.position - player.position;
        flatToLook.y = 0f;
        if (flatToLook.sqrMagnitude > 0.0001f)
            player.rotation = Quaternion.LookRotation(flatToLook.normalized, Vector3.up);

        if (camera != null)
        {
            Vector3 toLook = _lookPoint.position - camera.position;
            if (toLook.sqrMagnitude > 0.0001f)
            {
                Quaternion worldLook = Quaternion.LookRotation(toLook.normalized, Vector3.up);
                Quaternion localTarget = Quaternion.Inverse(player.rotation) * worldLook;
                Vector3 euler = localTarget.eulerAngles;
                camera.localRotation = Quaternion.Euler(euler.x, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Waits until the animator has finished playing the given state.
    /// </summary>
    private IEnumerator WaitForStateFinish(Animator animator, string stateName, float timeout = 20f)
    {
        // Let the state machine land on the new state: while the trigger is
        // still crossing a transition the current state is still the OLD one.
        float landElapsed = 0f;
        while (landElapsed < 1.5f &&
               !animator.IsInTransition(0) &&
               !animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
        {
            landElapsed += Time.deltaTime;
            yield return null;
        }

        float elapsed = 0f;
        while (elapsed < timeout &&
               !animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               !animator.GetNextAnimatorStateInfo(0).IsName(stateName))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        while (animator.IsInTransition(0) &&
               animator.GetNextAnimatorStateInfo(0).IsName(stateName))
            yield return null;

        while (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;
    }
}
