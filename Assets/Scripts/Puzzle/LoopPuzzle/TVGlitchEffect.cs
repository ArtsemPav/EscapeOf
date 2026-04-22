using System.Collections;
using UnityEngine;

/// <summary>
/// Randomly triggers glitch events on the TV screen by animating the
/// _GlitchAmount property of the TVGlitch shader.
/// Attach to the same GameObject as PeepholeTVCamera.
/// </summary>
public class TVGlitchEffect : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Minimum seconds between glitch events.")]
    [SerializeField] private float _intervalMin = 3f;
    [Tooltip("Maximum seconds between glitch events.")]
    [SerializeField] private float _intervalMax = 10f;

    [Header("Intensity")]
    [Tooltip("Minimum glitch strength (0–1).")]
    [SerializeField] private float _intensityMin = 0.3f;
    [Tooltip("Maximum glitch strength (0–1).")]
    [SerializeField] private float _intensityMax = 1f;

    [Header("Duration")]
    [Tooltip("Minimum glitch event duration in seconds.")]
    [SerializeField] private float _durationMin = 0.05f;
    [Tooltip("Maximum glitch event duration in seconds.")]
    [SerializeField] private float _durationMax = 0.35f;

    [Header("Burst")]
    [Tooltip("Chance (0–1) that a second quick glitch immediately follows the first.")]
    [SerializeField] [Range(0f, 1f)] private float _burstChance = 0.3f;
    [Tooltip("Pause in seconds between burst glitches.")]
    [SerializeField] private float _burstDelay = 0.1f;

    private static readonly int GlitchAmountID = Shader.PropertyToID("_GlitchAmount");

    private Material _material;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        var tvCamera = GetComponent<PeepholeTVCamera>();
        if (tvCamera == null)
        {
            Debug.LogError("[TVGlitchEffect] PeepholeTVCamera not found on this GameObject.", this);
            enabled = false;
            return;
        }

        _material = tvCamera.ScreenMaterial;
        if (_material == null)
        {
            Debug.LogError("[TVGlitchEffect] ScreenMaterial is null — PeepholeTVCamera may not have initialized yet.", this);
            enabled = false;
            return;
        }

        StartCoroutine(GlitchLoop());
    }

    // ── Glitch Loop ────────────────────────────────────────────────────────────

    private IEnumerator GlitchLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(_intervalMin, _intervalMax));
            yield return StartCoroutine(PlayGlitch());
        }
    }

    private IEnumerator PlayGlitch()
    {
        yield return StartCoroutine(SingleGlitch());

        if (Random.value < _burstChance)
        {
            yield return new WaitForSeconds(_burstDelay);
            yield return StartCoroutine(SingleGlitch());
        }
    }

    /// <summary>Snaps to a random intensity, holds for a random duration, then fades out.</summary>
    private IEnumerator SingleGlitch()
    {
        float intensity = Random.Range(_intensityMin, _intensityMax);
        float duration  = Random.Range(_durationMin, _durationMax);

        _material.SetFloat(GlitchAmountID, intensity);
        yield return new WaitForSeconds(duration);

        const float kFadeOut = 0.1f;
        for (float elapsed = 0f; elapsed < kFadeOut; elapsed += Time.deltaTime)
        {
            _material.SetFloat(GlitchAmountID, Mathf.Lerp(intensity, 0f, elapsed / kFadeOut));
            yield return null;
        }

        _material.SetFloat(GlitchAmountID, 0f);
    }
}
