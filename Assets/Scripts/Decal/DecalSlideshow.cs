using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives a URP DecalProjector as a diafilm-style slideshow. Slides are packed
/// into a Texture2DArray and scrolled through a fixed projection window with a
/// mechanical film-strip motion — including a configurable gap between frames
/// that visibly passes through the window during each transition, just like a
/// real diaprojector. One draw call per frame; all animation runs on the GPU
/// via a single _ScrollOffset float.
/// </summary>
[RequireComponent(typeof(DecalProjector))]
public class DecalSlideshow : MonoBehaviour
{
    private const string CurrentIndexProperty     = "_CurrentIndex";
    private const string NextIndexProperty        = "_NextIndex";
    private const string ScrollOffsetProperty     = "_ScrollOffset";
    private const string SlideTexturesProperty    = "_SlideTextures";
    private const string BaseColorProperty        = "_BaseColor";
    private const string FrameGapProperty         = "_FrameGap";
    private const string GapColorProperty         = "_GapColor";
    private const string ScrollDirectionProperty  = "_ScrollDirection";
    private const string ArraySizeProperty        = "_ArraySize";
    private const string FlickerFrequencyProperty = "_FlickerFrequency";
    private const string FlickerAmountProperty    = "_FlickerAmount";
    private const string ShaderName               = "Custom/DecalSlideshow";

    private const float DefaultSlideDuration      = 4f;
    private const float DefaultTransitionDuration = 0.6f;
    private const float DefaultFrameGap           = 0.03f;
    private const float DefaultFlickerFrequency   = 25f;
    private const float DefaultFlickerAmount      = 0.15f;
    private const float DurationEpsilon           = 0.001f;

    /// <summary>Direction in which the film strip scrolls.</summary>
    public enum FilmScrollDirection
    {
        Horizontal = 0,
        Vertical   = 1
    }

    [Header("Slides")]
    [Tooltip("Ordered list of textures that form the film strip. All must share the same width, height, and texture format.")]
    [SerializeField] private Texture2D[] _slides;

    [Header("Timing")]
    [Tooltip("How long each frame stays stationary before the film advances to the next one.")]
    [SerializeField] private float _slideDuration = DefaultSlideDuration;

    [Tooltip("Duration of the mechanical scroll transition between two consecutive frames.")]
    [SerializeField] private float _transitionDuration = DefaultTransitionDuration;

    [Tooltip("Curve that drives the scroll progress (0 → 1). A steep start with a gentle stop mimics a mechanical film advance.")]
    [SerializeField] private AnimationCurve _scrollCurve = new AnimationCurve(
        new Keyframe(0f,   0f,    0f, 3f),
        new Keyframe(0.3f, 0.7f, 3f, 0.5f),
        new Keyframe(1f,   1f,    0.5f, 0f));

    [Header("Film Strip")]
    [Tooltip("Gap between frames on the film strip, expressed as a fraction of frame size. 0 = no gap, 0.05 = 5 % black border between frames.")]
    [SerializeField] private float _frameGap = DefaultFrameGap;

    [Tooltip("Colour of the gap between frames. Alpha = 0 means the gap is transparent (surface shows through); alpha = 1 means the gap is opaque.")]
    [SerializeField] private Color _gapColor = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Direction the film strip moves during a transition.")]
    [SerializeField] private FilmScrollDirection _scrollDirection = FilmScrollDirection.Horizontal;

    [Header("Flicker (Projector Shutter)")]
    [Tooltip("Flicker frequency in Hz. 25 Hz mimics a mechanical film projector shutter.")]
    [SerializeField] private float _flickerFrequency = DefaultFlickerFrequency;

    [Tooltip("How much the image dims during each flicker cycle. 0 = no flicker, 1 = full black blink.")]
    [Range(0f, 1f)]
    [SerializeField] private float _flickerAmount = DefaultFlickerAmount;

    [Header("Appearance")]
    [Tooltip("Tint colour multiplied with every frame. Use alpha to control overall decal opacity.")]
    [SerializeField] private Color _baseColor = Color.white;

    [Header("Playback")]
    [Tooltip("When true the slideshow advances automatically on Start.")]
    [SerializeField] private bool _playOnAwake = true;

    [Tooltip("When true the film loops back to the first frame after the last one.")]
    [SerializeField] private bool _loop = true;

    [Header("Events")]
    [Tooltip("Fired after each frame transition completes. Hook up a clicking sound or other effect here.")]
    [SerializeField] private UnityEvent _onSlideChanged;

    private DecalProjector  _decalProjector;
    private Material        _material;
    private Texture2DArray  _textureArray;
    private Coroutine       _slideshowRoutine;
    private int             _currentIndex;
    private bool            _isPlaying;

    /// <summary>Current zero-based frame index.</summary>
    public int CurrentSlideIndex => _currentIndex;

    /// <summary>Number of frames in the slideshow.</summary>
    public int SlideCount => _slides != null ? _slides.Length : 0;

    /// <summary>Whether the slideshow is currently advancing automatically.</summary>
    public bool IsPlaying => _isPlaying;

    private void Awake()
    {
        _decalProjector = GetComponent<DecalProjector>();
        EnsureMaterial();
        BuildTextureArray();
    }

    private void Start()
    {
        if (_playOnAwake && SlideCount > 1)
            Play();
    }

    private void OnDestroy()
    {
        if (_textureArray != null)
        {
            Destroy(_textureArray);
            _textureArray = null;
        }
    }

    /// <summary>Starts automatic slideshow playback from the current frame.</summary>
    public void Play()
    {
        if (SlideCount <= 1)
            return;

        _isPlaying = true;
        if (_slideshowRoutine == null)
            _slideshowRoutine = StartCoroutine(SlideshowLoop());
    }

    /// <summary>Pauses automatic advancement and freezes on the current frame.</summary>
    public void Pause()
    {
        _isPlaying = false;
        if (_slideshowRoutine != null)
        {
            StopCoroutine(_slideshowRoutine);
            _slideshowRoutine = null;
        }
    }

    /// <summary>
    /// Jumps directly to the specified frame with no scroll transition.
    /// </summary>
    /// <param name="index">Zero-based frame index (clamped to valid range).</param>
    public void SetSlide(int index)
    {
        if (SlideCount == 0 || _material == null)
            return;

        _currentIndex = Mathf.Clamp(index, 0, SlideCount - 1);
        _material.SetFloat(CurrentIndexProperty, _currentIndex);
        _material.SetFloat(NextIndexProperty,    _currentIndex);
        _material.SetFloat(ScrollOffsetProperty, 0f);
    }

    /// <summary>Advances to the next frame (wraps when looping is enabled).</summary>
    public void NextSlide()
    {
        if (SlideCount == 0)
            return;

        int next = _currentIndex + 1;
        if (next >= SlideCount)
        {
            if (!_loop)
                return;
            next = 0;
        }
        SetSlide(next);
    }

    /// <summary>Goes back to the previous frame (wraps when looping is enabled).</summary>
    public void PreviousSlide()
    {
        if (SlideCount == 0)
            return;

        int prev = _currentIndex - 1;
        if (prev < 0)
        {
            if (!_loop)
                return;
            prev = SlideCount - 1;
        }
        SetSlide(prev);
    }

    private void EnsureMaterial()
    {
        Shader decalShader = Shader.Find(ShaderName);
        if (decalShader == null)
        {
            Debug.LogError($"[{nameof(DecalSlideshow)}] Shader '{ShaderName}' not found.", this);
            return;
        }

        _material = new Material(decalShader);
        _material.SetFloat(CurrentIndexProperty,    0f);
        _material.SetFloat(NextIndexProperty,       0f);
        _material.SetFloat(ScrollOffsetProperty,    0f);
        _material.SetFloat(FrameGapProperty,        _frameGap);
        _material.SetColor(GapColorProperty,        _gapColor);
        _material.SetFloat(ScrollDirectionProperty, (float)_scrollDirection);
        _material.SetFloat(ArraySizeProperty,       1f);
        _material.SetFloat(FlickerFrequencyProperty, _flickerFrequency);
        _material.SetFloat(FlickerAmountProperty,    _flickerAmount);
        _material.SetColor(BaseColorProperty,       _baseColor);
        _decalProjector.material = _material;
    }

    private void BuildTextureArray()
    {
        if (_slides == null || _slides.Length == 0)
        {
            Debug.LogWarning($"[{nameof(DecalSlideshow)}] No slides assigned.", this);
            return;
        }

        Texture2D first = _slides[0];
        int width  = first.width;
        int height = first.height;

        for (int i = 1; i < _slides.Length; i++)
        {
            if (_slides[i] == null)
            {
                Debug.LogWarning($"[{nameof(DecalSlideshow)}] Slide {i} is null — skipping.", this);
                continue;
            }

            if (_slides[i].width != width || _slides[i].height != height)
            {
                Debug.LogError(
                    $"[{nameof(DecalSlideshow)}] Slide {i} has dimensions {_slides[i].width}x{_slides[i].height} " +
                    $"but expected {width}x{height}. All slides must match.", this);
                return;
            }
        }

        // Use RGBA32 (uncompressed) for the Texture2DArray instead of the source
        // textures' compressed format. Graphics.CopyTexture can silently fail with
        // certain compressed formats in player builds; GetPixels32/SetPixels32 is
        // reliable across all platforms but requires Read/Write enabled on the
        // source textures.
        bool isLinear = !first.isDataSRGB;
        _textureArray = new Texture2DArray(width, height, _slides.Length, TextureFormat.RGBA32, false, isLinear)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int i = 0; i < _slides.Length; i++)
        {
            if (_slides[i] == null)
                continue;

            if (!_slides[i].isReadable)
            {
                Debug.LogError(
                    $"[{nameof(DecalSlideshow)}] Slide {i} ('{_slides[i].name}') is not readable. " +
                    "Enable Read/Write in the texture import settings.", this);
                continue;
            }

            Color32[] pixels = _slides[i].GetPixels32();
            _textureArray.SetPixels32(pixels, i);
        }

        _textureArray.Apply(false, false);
        _material.SetTexture(SlideTexturesProperty, _textureArray);
        _material.SetFloat(ArraySizeProperty, _slides.Length);
    }

    private IEnumerator SlideshowLoop()
    {
        while (_isPlaying && SlideCount > 1)
        {
            yield return new WaitForSeconds(_slideDuration);

            if (!_isPlaying)
                break;

            int nextIndex = _currentIndex + 1;
            if (nextIndex >= SlideCount)
            {
                if (!_loop)
                {
                    _isPlaying = false;
                    break;
                }
                nextIndex = 0;
            }

            // Always scroll forward during auto-playback, even when wrapping
            // from the last frame back to the first — the shader handles the
            // modulo wrap via _ArraySize, so _CurrentIndex=5 + offset=+1 gives
            // (5+1) % 6 = 0, keeping the film moving in the same direction.
            yield return ScrollTo(nextIndex, forward: true);

            _currentIndex = nextIndex;
            _onSlideChanged?.Invoke();
        }

        _slideshowRoutine = null;
    }

    /// <summary>
    /// Scrolls the film strip from the current frame to <paramref name="nextIndex"/>
    /// using the configured animation curve.
    /// </summary>
    /// <param name="nextIndex">Target frame index.</param>
    /// <param name="forward">When true the film scrolls forward (+1); when false, backward (-1).
    /// The shader resolves the actual slice via modulo, so forward from the last frame
    /// correctly wraps to the first without reversing direction.</param>
    private IEnumerator ScrollTo(int nextIndex, bool forward = true)
    {
        _material.SetFloat(CurrentIndexProperty, _currentIndex);
        _material.SetFloat(NextIndexProperty,    nextIndex);
        _material.SetFloat(ScrollOffsetProperty, 0f);

        float direction = forward ? 1f : -1f;

        float elapsed  = 0f;
        float duration = Mathf.Max(_transitionDuration, DurationEpsilon);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t    = Mathf.Clamp01(elapsed / duration);
            float eased = _scrollCurve.Evaluate(t);
            _material.SetFloat(ScrollOffsetProperty, direction * eased);
            yield return null;
        }

        // Snap to the destination frame
        _material.SetFloat(ScrollOffsetProperty, direction);
        _material.SetFloat(CurrentIndexProperty, nextIndex);
        _material.SetFloat(NextIndexProperty,    nextIndex);
        _material.SetFloat(ScrollOffsetProperty, 0f);
    }
}
