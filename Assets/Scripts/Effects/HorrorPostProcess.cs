using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-processing horror mood. Builds a runtime Volume profile with overrides that are
/// STRONGER than the game's base look (the base global volume already applies vignette,
/// chromatic aberration and cold white balance — see /Game_GlobalVolume), and blends them
/// in when Activate() is called.
///
/// While active the effects PULSE with a heartbeat rhythm ("lub-dub"): the vignette squeezes
/// like tunnel vision, chromatic aberration flinches on every contraction, film grain creeps
/// between beats. Fades back out on Deactivate().
///
/// Wire Activate() to the horror event's On Activated and Deactivate() to On Deactivated.
/// </summary>
[RequireComponent(typeof(Volume))]
public class HorrorPostProcess : MonoBehaviour
{
    private const float VolumePriority = 200f; // above /Game_GlobalVolume (100) so overrides apply

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
    [SerializeField, Range(0f, 1f)] private float _filmGrainResponse = 0.85f;

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
    private Coroutine _routine;
    private bool _active;

    private void Awake()
    {
        _volume = GetComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;

        BuildRuntimeProfile();
        _volume.profile = _runtimeProfile;

        _volume.weight = 0f;
        _volume.enabled = false;
    }

    private void OnDestroy()
    {
        if (_runtimeProfile != null)
            Destroy(_runtimeProfile);
    }

    /// <summary>Blends the horror effects in. Safe to call repeatedly — only the first call counts.</summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeRoutine(1f, _fadeInDuration));
    }

    /// <summary>Blends the horror effects back out. Safe to call when not active.</summary>
    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeRoutine(0f, _fadeOutDuration));
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

        LensDistortion lens = _runtimeProfile.Add<LensDistortion>(true);
        lens.intensity.overrideState = true;
        lens.intensity.value = _lensDistortion;
        lens.xMultiplier.overrideState = true;
        lens.xMultiplier.value = 1f;
        lens.yMultiplier.overrideState = true;
        lens.yMultiplier.value = 1f;
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
                _volume.weight = Mathf.Clamp01(target + beat * _pulseAmplitude);

                // Aberration flinches with every heart contraction
                _chromatic.intensity.value = _chromaticAberration + beat * _chromaticSpike;

                // Grain creeps up between beats, calms on the thump
                _grain.intensity.value = _filmGrainIntensity * (0.8f + 0.2f * (1f - beat));

                yield return null;
            }
        }

        if (target <= 0f)
        {
            _volume.weight = 0f;
            _volume.enabled = false;
            _routine = null;
        }
    }

    /// <summary>
    /// One heartbeat cycle in [0..1]: sharp "lub" thump, short gap, softer "dub" thump.
    /// Returns roughly 0..1, where 1 is peak contraction. Additive: can go above/below — clamped by caller.
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
