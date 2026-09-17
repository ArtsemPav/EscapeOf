using System.Collections;
using UnityEngine;

/// <summary>
/// Smoothly fades a Light on/off for the Loop Puzzle solved cinematic.
/// Created specifically for the SolvedLight object in the PaintPuzzle prefab:
/// LoopPuzzleController activates the GameObject during the cinematic — the
/// light fades in, and fades back out just before the object is deactivated again.
/// Works only in Play Mode; in edit mode the light keeps its authored intensity.
/// </summary>
[RequireComponent(typeof(Light))]
public class SolvedLightFade : MonoBehaviour
{
    [Header("Fade")]
    [Tooltip("Duration of the fade to full intensity in seconds.")]
    [SerializeField, Min(0.01f)] private float _fadeInDuration = 1f;

    [Tooltip("Duration of the fade to zero intensity in seconds.")]
    [SerializeField, Min(0.01f)] private float _fadeOutDuration = 1f;

    [Tooltip("Delay before the fade-out starts, in seconds.")]
    [SerializeField, Min(0f)] private float _holdAtFullDuration = 0.5f;

    [Tooltip("If true, starts playing the fade-in as soon as this component is enabled.")]
    [SerializeField] private bool _fadeInOnEnable = true;

    /// <summary>Target intensity the light fades toward — its authored value in the Inspector.</summary>
    public float TargetIntensity { get; private set; }

    private Light _light;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        _light = GetComponent<Light>();
        TargetIntensity = _light.intensity;
    }

    private void OnEnable()
    {
        // Awake may not have run before the object was activated — guard anyway.
        if (_light == null)
        {
            _light = GetComponent<Light>();
            TargetIntensity = _light.intensity;
        }

        if (_fadeInOnEnable)
            FadeIn();
    }

    private void OnDisable()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

    /// <summary>
    /// Smoothly raises the light from zero to its authored intensity over _fadeInDuration.
    /// Cancels any running fade first.
    /// </summary>
    public void FadeIn()
    {
        if (!Application.isPlaying) return;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(0f, TargetIntensity, _fadeInDuration));
    }

    /// <summary>
    /// Smoothly lowers the light to zero over _fadeOutDuration, waiting
    /// _holdAtFullDuration first. Cancels any running fade first.
    /// </summary>
    public void FadeOut()
    {
        if (!Application.isPlaying) return;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        if (_holdAtFullDuration > 0f)
            yield return new WaitForSeconds(_holdAtFullDuration);
        yield return FadeRoutine(TargetIntensity, 0f, _fadeOutDuration);
    }

    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (_light == null) yield break;

        _light.intensity = from;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            _light.intensity = Mathf.Lerp(from, to, t);
            yield return null;
        }

        _light.intensity = to;
        _fadeCoroutine = null;
    }
}
