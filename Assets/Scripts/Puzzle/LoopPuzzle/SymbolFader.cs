using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Smoothly fades a SpriteRenderer symbol in and out instead of toggling SetActive.
/// Applies soft-additive blending (SrcAlpha * Symbol + 1 * Scene) so the symbol
/// layers additively on top of the painting without replacing it.
///
/// Attach to every symbol GameObject that has a SpriteRenderer.
/// LoopPuzzleController.RefreshSymbol detects this component automatically.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SymbolFader : MonoBehaviour
{
    [Tooltip("Delay in seconds before the fade-in begins.")]
    [SerializeField] private float _showDelay    = 0f;

    [Tooltip("Duration of the fade-in and fade-out animation in seconds.")]
    [SerializeField] private float _fadeDuration = 0.5f;

    [Tooltip("Target alpha when fully visible. Use values below 1 for semi-transparent symbols.")]
    [Range(0f, 1f)]
    [SerializeField] private float _targetAlpha  = 1f;

    [Tooltip("Multiplied onto the sprite's original color. (1,1,1) = keep original. HDR values above 1 boost brightness for Bloom.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _glowColor = Color.white;

    /// <summary>RGB of the SpriteRenderer as set in the prefab/Inspector. Cached once in Awake.</summary>
    private Color _baseColor;

    /// <summary>
    /// The logical target state — true when all puzzle conditions for this symbol are met.
    /// LoopPuzzleController.CheckWinCondition reads this instead of activeSelf.
    /// </summary>
    public bool IsTargetVisible { get; private set; }

    private SpriteRenderer _renderer;
    private Coroutine _fadeCoroutine;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _renderer  = GetComponent<SpriteRenderer>();
        _baseColor = _renderer.color;   // cache original prefab color before we touch it
        SetAlpha(0f);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks this symbol as logically visible and fades it in to _targetAlpha,
    /// optionally waiting _showDelay seconds before starting.
    /// Activates the GameObject first if it was deactivated.
    /// </summary>
    public void Show()
    {
        IsTargetVisible = true;
        StopFade();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);  // triggers Awake on first activation

        _fadeCoroutine = StartCoroutine(ShowSequence());
    }

    /// <summary>
    /// Marks this symbol as logically hidden and fades it out to zero.
    /// Safe to call on an already-inactive GameObject.
    /// </summary>
    public void Hide()
    {
        IsTargetVisible = false;
        if (!gameObject.activeSelf) return;
        StopFade();
        _fadeCoroutine = StartCoroutine(FadeTo(0f));
    }

    /// <summary>
    /// Instantly hides without animation. Used during puzzle reset and initialisation.
    /// Safe to call on an inactive GameObject.
    /// </summary>
    public void HideImmediate()
    {
        IsTargetVisible = false;
        StopFade();
        if (_renderer != null)
            SetAlpha(0f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void StopFade()
    {
        if (_fadeCoroutine == null) return;
        StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = null;
    }

    private IEnumerator ShowSequence()
    {
        if (_showDelay > 0f)
            yield return new WaitForSeconds(_showDelay);

        yield return FadeTo(_targetAlpha);
    }

    private IEnumerator FadeTo(float target)
    {
        float start   = _renderer.color.a;
        float elapsed = 0f;

        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(start, target, elapsed / _fadeDuration));
            yield return null;
        }

        SetAlpha(target);
        _fadeCoroutine = null;
    }

    private void SetAlpha(float alpha)
    {
        _renderer.color = new Color(
            _baseColor.r * _glowColor.r,
            _baseColor.g * _glowColor.g,
            _baseColor.b * _glowColor.b,
            alpha
        );
    }

    /// <summary>
    /// Instances the current sprite material and sets soft-additive blend mode:
    /// Result = SpriteColor * SrcAlpha + SceneColor * One.
    /// Works with both "Sprites/Default" and the URP 2D Sprite Unlit shader.
    /// </summary>
    private void ApplyAdditiveBlend()
    {
        // _renderer.material auto-instances the shared material so we don't corrupt the original.
        var mat = _renderer.material;

        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);

        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", (int)BlendMode.One);
    }
}
