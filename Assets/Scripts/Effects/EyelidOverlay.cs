using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen "waking up" overlay: two eyelids rendered as the area OUTSIDE
/// an elliptical eye opening (Custom/EyelidOverlay shader). The lids hang
/// from the top and bottom screen edges with CONCAVE facing edges — at
/// eyeOpen = 1 the opening covers the whole screen, at 0 the lids close over
/// it like natural eyelids (no hourglass pinch, no bottom gap).
///
/// The WakeUpIntroController drives it through <see cref="SetEyeOpen"/>
/// (0 = closed, 1 = fully open) and performs the scripted blinks with
/// <see cref="Blink"/>.
///
/// The shader material is created at runtime from the shader asset; attach
/// the component to any GameObject in the scene and assign the shader.
/// </summary>
public class EyelidOverlay : MonoBehaviour
{
    public static EyelidOverlay Instance { get; private set; }

    [Header("Shader")]
    [Tooltip("The eyelid shader asset (Custom/EyelidOverlay).")]
    [SerializeField] private Shader _shader;

    [Header("Eyelid Shape")]
    [Tooltip("How deep the lid curves are (0 = straight edges, 1 = deep arcs).")]
    [SerializeField, Range(0f, 1f)] private float lidCurve = 0.6f;

    [Tooltip("Softness of the lid edge as a fraction of the screen.")]
    [SerializeField, Range(0.01f, 0.3f)] private float lidEdgeSoftness = 0.08f;

    [Tooltip("Tint shown while the eyes are closed (darkens the view through the lashes).")]
    [SerializeField] private Color sleepTint = new Color(0.02f, 0.02f, 0.04f, 1f);

    /// <summary>Raised when the eyes become fully open.</summary>
    public event Action OnFullyOpen;

    /// <summary>Current eye-open amount (0 = closed, 1 = open).</summary>
    public float EyeOpen { get; private set; }

    private static readonly int EyeOpenId = Shader.PropertyToID("_EyeOpen");
    private static readonly int CurveId = Shader.PropertyToID("_Curve");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int TintId = Shader.PropertyToID("_Tint");

    private Canvas _canvas;
    private Material _material;
    private Image _overlay;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildOverlay();
        SetEyeOpen(0f);
        _canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_material != null) Destroy(_material);
    }

    /// <summary>
    /// Sets the eye-open amount. 0 = fully closed (the lids cover the screen),
    /// 1 = fully open (the overlay is invisible). Safe to call every frame.
    /// </summary>
    public void SetEyeOpen(float value)
    {
        EyeOpen = Mathf.Clamp01(value);
        if (_overlay == null || _material == null) return; // Awake not finished yet (restart race)
        ApplyEyeOpen();
    }

    /// <summary>Instantly closes the eyes and shows the overlay.</summary>
    public void CloseEyes()
    {
        if (_canvas != null) _canvas.enabled = true;
        SetEyeOpen(0f);
    }

    /// <summary>Instantly opens the eyes fully and hides the overlay.</summary>
    public void OpenEyes()
    {
        SetEyeOpen(1f);
        if (_canvas != null) _canvas.enabled = false;
    }

    /// <summary>
    /// Coroutine: one scripted blink. The eyes close from the given open
    /// amount and return to it, over <paramref name="duration"/> seconds total.
    /// </summary>
    public Coroutine Blink(float duration, float fromOpenAmount)
        => StartCoroutine(BlinkRoutine(duration, fromOpenAmount));

    private IEnumerator BlinkRoutine(float duration, float fromOpenAmount)
    {
        float half = Mathf.Max(0.01f, duration * 0.5f);
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            SetEyeOpen(Mathf.Lerp(fromOpenAmount, 0f, t / half));
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            SetEyeOpen(Mathf.Lerp(0f, fromOpenAmount, t / half));
            yield return null;
        }
        SetEyeOpen(fromOpenAmount);
    }

    private void ApplyEyeOpen()
    {
        if (_overlay == null) return;

        if (EyeOpen >= 1f)
        {
            _canvas.enabled = false;
            OnFullyOpen?.Invoke();
            return;
        }

        _canvas.enabled = true;
        _material.SetFloat(EyeOpenId, EyeOpen);
        _material.SetFloat(CurveId, lidCurve);
        _material.SetFloat(SoftnessId, lidEdgeSoftness);
        _material.SetColor(TintId, sleepTint);
    }

    // ── Procedural build ─────────────────────────────────────────────────────

    private void BuildOverlay()
    {
        GameObject canvasGo = new GameObject("EyelidOverlayCanvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500; // above gameplay, below editor/debug overlays

        GameObject overlayGo = new GameObject("EyelidOverlay", typeof(Image));
        overlayGo.transform.SetParent(_canvas.transform, false);
        _overlay = overlayGo.GetComponent<Image>();
        _overlay.raycastTarget = false;

        // The shader can come from a serialized reference or be found by name.
        Shader shader = _shader != null ? _shader : Shader.Find("Custom/EyelidOverlay");
        if (shader == null)
        {
            Debug.LogError("[EyelidOverlay] Shader 'Custom/EyelidOverlay' not found — the overlay will not render.", this);
            return;
        }
        _material = new Material(shader);
        _overlay.material = _material;
        _overlay.color = Color.white;

        _overlay.rectTransform.anchorMin = Vector2.zero;
        _overlay.rectTransform.anchorMax = Vector2.one;
        _overlay.rectTransform.offsetMin = Vector2.zero;
        _overlay.rectTransform.offsetMax = Vector2.zero;
    }
}
