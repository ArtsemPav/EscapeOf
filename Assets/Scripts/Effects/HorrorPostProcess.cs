using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-processing horror mood with a CINEMATIC FOCUS LOCK on the approaching subject.
///
/// Two stages, wired to the two vent triggers:
///
/// 1. Activate()   — mood: stronger vignette (heartbeat pulse), film grain, cold grading,
///                   chromatic aberration, motion blur. No lens move yet.
/// 2. ActivateLensMove() — the lens take: the camera FOV snaps to telephoto (subject reads
///                   BIG from far away), Bokeh focus follows her so she stays tack-sharp,
///                   and as she crawls closer the telephoto eases back to normal FOV while
///                   the aperture deepens — she barely grows in frame, the background swims.
///                   FOV is applied additively (same pattern as PortalPlayerImpact), so
///                   other systems that own the camera are never overwritten.
///
/// Deactivate() fades everything out and restores the FOV.
/// </summary>
[RequireComponent(typeof(Volume))]
public class HorrorPostProcess : MonoBehaviour
{
    private const float VolumePriority = 200f; // above /Game_GlobalVolume (100) so overrides apply

    [Header("Cinematic Focus (optional)")]
    [Tooltip("Subject the camera keeps in focus (the crawling mannequin). " +
             "Enables Bokeh DOF that tracks her and the telephoto move. Null = plain Gaussian near blur.")]
    [SerializeField] private Transform _focusTarget;

    [Tooltip("Camera that renders the game. Assign explicitly — Camera.main fails when the " +
             "camera is not tagged MainCamera. Falls back to Camera.main, then any enabled camera.")]
    [SerializeField] private Camera _camera;

    [Tooltip("FOV multiplier while the subject is far. 0.7 = telephoto — she reads BIG.")]
    [SerializeField, Range(0.4f, 1f)] private float _telephotoZoom = 0.7f;

    [Tooltip("FOV multiplier for the opening punch-in — the very start of her crawl is even closer. " +
             "Eases into the distance curve in sync with her movement. 1 = no punch-in.")]
    [SerializeField, Range(0.3f, 1f)] private float _introZoom = 0.45f;

    [Tooltip("Metres of her crawl over which the opening punch-in eases back to the distance curve. " +
             "The zoom relaxes WITH her movement speed, not a timer — if she stops, the lens holds the punch-in.")]
    [SerializeField, Range(0.5f, 6f)] private float _introCrawlMeters = 2f;

    [Tooltip("Distance at or beyond which the telephoto zoom is at full strength. Eases to 1:1 as she approaches.")]
    [SerializeField, Min(0.5f)] private float _lensZoomMaxDistance = 8f;

    [Tooltip("Focal length while the subject is far (mm). High = background melts into bokeh.")]
    [SerializeField, Range(20f, 300f)] private float _focalLengthFarMm = 120f;

    [Tooltip("Focal length while the subject is close (mm). Low = she stays sharp at close range.")]
    [SerializeField, Range(20f, 300f)] private float _focalLengthNearMm = 70f;

    [Tooltip("Aperture while the subject is far (f-stop). Low = heavy background blur.")]
    [SerializeField, Range(0.5f, 32f)] private float _apertureFarF = 4f;

    [Tooltip("Aperture while the subject is close (f-stop). High = deep focus, she stays sharp up close.")]
    [SerializeField, Range(0.5f, 32f)] private float _apertureNearF = 10f;

    [Header("Vignette (target while active)")]
    [SerializeField, Range(0f, 1f)] private float _vignetteIntensity = 0.82f;
    [SerializeField, Range(0f, 1f)] private float _vignetteSmoothness = 0.4f;

    [Tooltip("Tint of the vignette. Dark blood red reads as dread without looking like a damage flash.")]
    [SerializeField] private Color _vignetteColor = new Color(0.14f, 0f, 0f, 1f);

    [Header("Color Grading (target while active)")]
    [Tooltip("Scene exposure in stops while the event is active. Negative = darker.")]
    [SerializeField, Range(-2f, 2f)] private float _exposureStops = -0.7f;

    [Tooltip("Color saturation while the event is active. Negative = drained, lifeless colors.")]
    [SerializeField, Range(-100f, 100f)] private float _saturation = -35f;

    [Header("Chromatic Aberration (target while active)")]
    [SerializeField, Range(0f, 1f)] private float _chromaticAberration = 0.65f;

    [Tooltip("Extra aberration on each heartbeat — the image 'flinches' with the pulse.")]
    [SerializeField, Range(0f, 1f)] private float _chromaticSpike = 0.3f;

    [Header("Film Grain (target while active)")]
    [SerializeField, Range(0f, 1f)] private float _filmGrainIntensity = 0.5f;

    [Tooltip("How much the grain reacts to bright pixels (0 = uniform noise, 1 = only highlights).")]
    [SerializeField, Range(0f, 1f)] private float _filmGrainResponse = 0.9f;

    [Header("Near Blur (fallback without focus target)")]
    [Tooltip("Gaussian near blur for claustrophobia; the blur radius pumps with the heartbeat.")]
    [SerializeField] private bool _depthOfField = true;

    [Tooltip("Distance where blur starts (metres). Everything closer smears away.")]
    [SerializeField, Range(0.1f, 2f)] private float _dofStart = 0.3f;

    [Tooltip("Distance where the image is fully sharp (metres).")]
    [SerializeField, Range(0.5f, 4f)] private float _dofEnd = 1.8f;

    [Tooltip("Base blur radius while active.")]
    [SerializeField, Range(0f, 1.5f)] private float _dofMaxRadius = 0.85f;

    [Tooltip("Extra blur radius on each heartbeat.")]
    [SerializeField, Range(0f, 0.5f)] private float _dofHeartbeatPulse = 0.3f;

    [Header("Motion Blur (target while active)")]
    [Tooltip("Camera-only motion blur; intensity flinches on every heartbeat.")]
    [SerializeField] private bool _motionBlur = true;

    [SerializeField, Range(0f, 1f)] private float _motionBlurIntensity = 0.2f;

    [Tooltip("Extra motion blur on each heartbeat.")]
    [SerializeField, Range(0f, 0.5f)] private float _motionBlurHeartbeatPulse = 0.2f;

    [Header("Lens Distortion (target while active)")]
    [Tooltip("Negative = barrel (sickening) distortion at the edges.")]
    [SerializeField, Range(-1f, 1f)] private float _lensDistortion = -0.25f;

    [Header("Fading")]
    [Tooltip("Seconds to blend the effects in on Activate().")]
    [SerializeField, Min(0f)] private float _fadeInDuration = 2f;

    [Tooltip("Seconds to blend the effects out on Deactivate().")]
    [SerializeField, Min(0f)] private float _fadeOutDuration = 2f;

    [Header("Heartbeat Pulse")]
    [Tooltip("Beats per minute of the horror pulse (heartbeat vignette).")]
    [SerializeField, Range(40f, 180f)] private float _heartRateBpm = 72f;

    [Tooltip("Weight pulse amplitude per heartbeat while fully faded in. 0 = static.")]
    [SerializeField, Range(0f, 0.3f)] private float _pulseAmplitude = 0.1f;

    private Volume _volume;
    private VolumeProfile _runtimeProfile;
    private Vignette _vignette;
    private ChromaticAberration _chromatic;
    private FilmGrain _grain;
    private DepthOfField _dof;
    private MotionBlur _blur;
    private LensDistortion _lens;
    private Coroutine _moodRoutine;
    private bool _active;
    private bool _lensMoveStarted;

    // The FOV war winner: the game's camera is driven by Cinemachine (CinemachineBrain writes
    // FOV every frame, AspectRatioFov corrects it after, order 10000). Writing Camera.fieldOfView
    // directly is overwritten within a frame — the lens move must go through the Cinemachine lens.
    private CinemachineCamera _playerCinemachineCamera;
    private float _baseFov;

    // Subject's body position when the lens move started — the punch-in eases back
    // in sync with HER crawl progress (metres moved), not a timer
    private Vector3 _lensMoveStartPosition;

    // Heartbeat value of the current frame (0..1), written by the mood routine, read in LateUpdate
    private float _lastBeat;

    // Body bounds of the focus target — the mesh sits offset from the root transform,
    // especially when the root is the holder and the body crawls out of it
    private Renderer _focusRenderer;

    private void Awake()
    {
        _volume = GetComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;

        BuildRuntimeProfile();
        // Proven pattern (PortalPlayerImpact): assign as sharedProfile so the
        // Volume manager blends our runtime instance directly.
        _volume.sharedProfile = _runtimeProfile;

        _volume.weight = 0f;
        _volume.enabled = false;
    }

    private void OnDestroy()
    {
        if (_runtimeProfile != null)
            Destroy(_runtimeProfile);
    }

    /// <summary>Blends the mood in. Safe to call repeatedly — only the first call counts.</summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;
        if (_moodRoutine != null) StopCoroutine(_moodRoutine);
        _moodRoutine = StartCoroutine(FadeRoutine(1f, _fadeInDuration));
    }

    /// <summary>
    /// The lens take: camera snaps to telephoto (she reads BIG from far away), focus and
    /// aperture track her approach every frame from LateUpdate. Wire to the main scare trigger.
    /// </summary>
    public void ActivateLensMove()
    {
        if (_lensMoveStarted) return;
        _lensMoveStarted = true;

        // Capture the base FOV from the same place the FOV war winners write to
        CacheCinemachineCamera();
        _baseFov = _playerCinemachineCamera != null
            ? _playerCinemachineCamera.Lens.FieldOfView
            : (GetRenderCamera() != null ? GetRenderCamera().fieldOfView : 0f);

        // Use the ROOT transform, not renderer bounds: ActivateLensMove fires while the
        // subject is still hidden (renderer bounds of a disabled renderer are garbage at
        // the world origin), and CharacterRunPast moves this very root during the crawl.
        _lensMoveStartPosition = _focusTarget != null ? _focusTarget.position : Vector3.zero;
    }

    /// <summary>Blends the horror effects back out. Safe to call when not active.</summary>
    public void Deactivate()
    {
        if (!_active && !_lensMoveStarted) return;
        _active = false;
        _lensMoveStarted = false;

        // Restore the FOV captured at the lens move start
        if (_baseFov > 0f)
            ApplyFov(_baseFov);

        if (_moodRoutine != null) StopCoroutine(_moodRoutine);
        _moodRoutine = StartCoroutine(FadeRoutine(0f, _fadeOutDuration));
    }

    /// <summary>
    /// Creates the profile in code: every override is a target value applied at full weight.
    /// The volume weight blends between the base game look (weight 0) and these values (weight 1).
    /// </summary>
    private void BuildRuntimeProfile()
    {
        _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();

        _vignette = _runtimeProfile.Add<Vignette>(true);
        _vignette.color.overrideState = true;
        _vignette.color.value = _vignetteColor;
        _vignette.intensity.overrideState = true;
        _vignette.intensity.value = _vignetteIntensity;
        _vignette.smoothness.overrideState = true;
        _vignette.smoothness.value = _vignetteSmoothness;

        _chromatic = _runtimeProfile.Add<ChromaticAberration>(true);
        _chromatic.intensity.overrideState = true;
        _chromatic.intensity.value = _chromaticAberration;

        _grain = _runtimeProfile.Add<FilmGrain>(true);
        _grain.type.overrideState = true;
        _grain.type.value = FilmGrainLookup.Thin2; // fine analog noise, reads as "camera dying"
        _grain.intensity.overrideState = true;
        _grain.intensity.value = _filmGrainIntensity;
        _grain.response.overrideState = true;
        _grain.response.value = _filmGrainResponse;

        ColorAdjustments color = _runtimeProfile.Add<ColorAdjustments>(true);
        color.postExposure.overrideState = true;
        color.postExposure.value = _exposureStops;
        color.saturation.overrideState = true;
        color.saturation.value = _saturation;

        _lens = _runtimeProfile.Add<LensDistortion>(true);
        _lens.intensity.overrideState = true;
        _lens.intensity.value = _lensDistortion;
        _lens.xMultiplier.overrideState = true;
        _lens.xMultiplier.value = 1f;
        _lens.yMultiplier.overrideState = true;
        _lens.yMultiplier.value = 1f;

        if (_focusTarget != null)
        {
            // Bokeh with a moving focusDistance — the subject stays tack-sharp while
            // the focal length melts everything behind it into bokeh.
            _dof = _runtimeProfile.Add<DepthOfField>(true);
            _dof.mode.overrideState = true;
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focusDistance.overrideState = true;
            _dof.focusDistance.value = 2f; // overwritten every frame in LateUpdate
            _dof.focalLength.overrideState = true;
            _dof.focalLength.value = _focalLengthFarMm;
            _dof.aperture.overrideState = true;
            _dof.aperture.value = _apertureFarF;
        }
        else if (_depthOfField)
        {
            // Fallback: cheap Gaussian near-blur, claustrophobia without a subject.
            _dof = _runtimeProfile.Add<DepthOfField>(true);
            _dof.mode.overrideState = true;
            _dof.mode.value = DepthOfFieldMode.Gaussian;
            _dof.gaussianStart.overrideState = true;
            _dof.gaussianStart.value = _dofStart;
            _dof.gaussianEnd.overrideState = true;
            _dof.gaussianEnd.value = _dofEnd;
            _dof.gaussianMaxRadius.overrideState = true;
            _dof.gaussianMaxRadius.value = _dofMaxRadius;
        }

        if (_motionBlur)
        {
            _blur = _runtimeProfile.Add<MotionBlur>(true);
            _blur.mode.overrideState = true;
            _blur.mode.value = MotionBlurMode.CameraOnly;
            _blur.intensity.overrideState = true;
            _blur.intensity.value = _motionBlurIntensity;
        }
    }

    // ── Telephoto zoom + focus tracking ────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_active || _focusTarget == null || _dof == null)
            return;

        Camera cam = GetRenderCamera();
        if (cam == null)
            return;

        // Axial (along view ray) distance to the subject's BODY (renderer bounds center —
        // the mesh crawls away from the root pivot, the root alone misses by half a metre)
        Vector3 subjectPoint = FocusPoint();
        Vector3 toSubject = subjectPoint - cam.transform.position;
        float distance = Mathf.Max(0.1f, Vector3.Dot(toSubject, cam.transform.forward));

        // Subject always inside the focal plane — sharp at any distance
        _dof.focusDistance.value = distance;

        if (_lensMoveStarted)
        {
            // 1 when far → 0 when close: telephoto melts away as she approaches
            float farT = Mathf.Clamp01(distance / _lensZoomMaxDistance);

            // Opening punch-in: eases back in sync with HER crawl progress — the zoom
            // relaxes over the first _introCrawlMeters of movement. If she stops or
            // crawls slowly, the lens HOLDS the punch-in; fast crawl relaxes it fast.
            // Measured on the ROOT transform: renderer bounds lag when she is hidden
            // (disabled renderer reports garbage) and crawl animation offsets the mesh.
            float introT = 1f;
            float crawlProgress = Vector3.Distance(_focusTarget.position, _lensMoveStartPosition);
            if (crawlProgress < _introCrawlMeters)
                introT = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(crawlProgress / _introCrawlMeters));

            float zoom = Mathf.Lerp(1f, _telephotoZoom, farT) * Mathf.Lerp(1f, _introZoom / _telephotoZoom, introT);

            // Real telephoto zoom THROUGH CINEMACHINE (Camera.fieldOfView is overwritten by
            // CinemachineBrain + AspectRatioFov every frame — writing it directly does nothing)
            ApplyFov(_baseFov * zoom);

            // Lens eases with the zoom: telephoto far, calmer near
            _dof.focalLength.value = Mathf.Lerp(_focalLengthNearMm, _focalLengthFarMm, farT);

            // Aperture deepens as she approaches — DOF stops squeezing her at close range
            _dof.aperture.value = Mathf.Lerp(_apertureNearF, _apertureFarF, farT);
        }

        if (_blur != null)
        {
            // heartbeat flinch: 0..1 written by the mood routine
            _blur.intensity.value = _motionBlurIntensity + _lastBeat * _motionBlurHeartbeatPulse;
        }
    }

    /// <summary>World point to focus on: renderer bounds center if available, else the transform.</summary>
    private Vector3 FocusPoint()
    {
        if (_focusRenderer == null)
            _focusRenderer = _focusTarget.GetComponentInChildren<Renderer>(true);
        return _focusRenderer != null ? _focusRenderer.bounds.center : _focusTarget.position;
    }

    private IEnumerator FadeRoutine(float target, float duration)
    {
        _volume.enabled = true;

        float start = _volume.weight;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _volume.weight = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _volume.weight = target;

        if (target > 0f)
        {
            float beatsPerSecond = _heartRateBpm / 60f;
            float t = 0f;
            while (_active)
            {
                t += Time.unscaledDeltaTime * beatsPerSecond;

                // Lub-dub heartbeat: two sharp thumps per beat — tunnel-vision panic pulse.
                // Vignette, exposure and lens ride on the volume weight.
                float beat = HeartbeatWave(Mathf.Repeat(t, 1f));
                _lastBeat = beat;
                _volume.weight = Mathf.Clamp01(target + beat * _pulseAmplitude);

                // Aberration flinches with every heart contraction
                _chromatic.intensity.value = _chromaticAberration + beat * _chromaticSpike;

                // Grain creeps up between beats, calms on the thump
                _grain.intensity.value = _filmGrainIntensity * (0.8f + 0.2f * (1f - beat));

                // Fallback Gaussian near-blur closes in on the thump
                if (_dof != null && _focusTarget == null)
                    _dof.gaussianMaxRadius.value = _dofMaxRadius + beat * _dofHeartbeatPulse;

                yield return null;
            }
        }

        if (target <= 0f)
        {
            _volume.weight = 0f;
            _volume.enabled = false;
            _moodRoutine = null;
        }
    }

    /// <summary>Finds the render camera: explicit reference → Camera.main → any enabled camera.</summary>
    private Camera GetRenderCamera()
    {
        if (_camera != null) return _camera;

        _camera = Camera.main;
        if (_camera != null && _camera.enabled) return _camera;

        // Player cameras are often not tagged MainCamera — take any live camera
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
            if (cam.isActiveAndEnabled)
                return _camera = cam;

        return null;
    }

    /// <summary>
    /// Finds the player's CinemachineCamera (pattern from EarthquakeFOVPulse): on Camera.main's
    /// parents, else by the object name "PlayerCamera". FOV must be written through its Lens —
    /// Camera.fieldOfView is overwritten by CinemachineBrain + AspectRatioFov every frame.
    /// </summary>
    private void CacheCinemachineCamera()
    {
        if (_playerCinemachineCamera != null) return;

        Camera mainCam = GetRenderCamera();
        if (mainCam != null)
            _playerCinemachineCamera = mainCam.GetComponentInParent<CinemachineCamera>();

        if (_playerCinemachineCamera == null)
        {
            GameObject playerCameraObject = GameObject.Find("PlayerCamera");
            if (playerCameraObject != null)
                _playerCinemachineCamera = playerCameraObject.GetComponent<CinemachineCamera>();
        }
    }

    /// <summary>Writes FOV through Cinemachine when available, else to the camera directly.</summary>
    private void ApplyFov(float fov)
    {
        if (_playerCinemachineCamera != null)
        {
            var lens = _playerCinemachineCamera.Lens;
            lens.FieldOfView = fov;
            _playerCinemachineCamera.Lens = lens;
        }
        else if (_camera != null)
        {
            _camera.fieldOfView = fov;
        }
    }

    /// <summary>
    /// One heartbeat cycle in [0..1]: sharp "lub" thump, short gap, softer "dub" thump.
    /// Returns roughly 0..1, where 1 is peak contraction.
    /// </summary>
    private static float HeartbeatWave(float phase)
    {
        const float FirstBeatEnd = 0.14f;  // "lub" occupies the first 14% of the cycle
        const float SecondBeatStart = 0.28f;
        const float SecondBeatEnd = 0.4f;

        if (phase < FirstBeatEnd)
            return SharpThump(phase / FirstBeatEnd);

        if (phase >= SecondBeatStart && phase < SecondBeatEnd)
            return 0.6f * SharpThump((phase - SecondBeatStart) / (SecondBeatEnd - SecondBeatStart));

        return 0f;
    }

    /// <summary>Sine hump with a sharp attack and quick release — reads as a muscle contraction.</summary>
    private static float SharpThump(float t)
    {
        t = Mathf.Clamp01(t);
        return Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2f);
    }
}
